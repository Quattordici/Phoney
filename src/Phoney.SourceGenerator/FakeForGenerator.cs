using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Phoney.SourceGenerator;

/// <summary>
/// Generates reflection-free <c>FakeModel&lt;T&gt;</c> implementations for every type listed with
/// <c>[FakeFor&lt;T&gt;]</c> on a partial class (plus the types they contain), a <c>ObjectGenerator&lt;T&gt;</c> property
/// per listed type, and a module initializer that registers the models so <c>Fake.For&lt;T&gt;()</c> uses them too.
/// Types passed for <c>[FakeForTypeArgument]</c> type parameters get registered models as well, in one generated class.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class FakeForGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Phoney.Generation.FakeForAttribute`1";
    private const string SubtypeAttributeMetadataName = "Phoney.Generation.FakeSubtypeAttribute`2";
    private const string TypeArgumentAttributeMetadataName = "Phoney.Generation.FakeForTypeArgumentAttribute";
    private const string TypeArgumentHintName = "Phoney.TypeArgumentModels.g.cs";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (node, _) => IsClassOrRecord(node),
            static (ctx, ct) => Analyze(ctx, ct));

        // Classes with only [FakeSubtype] attributes; classes that also have [FakeFor] are handled above.
        var subtypeOnly = context.SyntaxProvider.ForAttributeWithMetadataName(
            SubtypeAttributeMetadataName,
            static (node, _) => IsClassOrRecord(node),
            static (ctx, ct) => HasAttribute((INamedTypeSymbol)ctx.TargetSymbol, AttributeMetadataName) ? null : Analyze(ctx, ct));

        context.RegisterSourceOutput(targets, static (spc, result) => Report(spc, result));
        context.RegisterSourceOutput(subtypeOnly, static (spc, result) => Report(spc, result));

        // Closed type arguments of [FakeForTypeArgument] type parameters (TestDataBuilder<CustomerBuilder, Customer>, Factory.Create<Order>()).
        var typeArguments = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is GenericNameSyntax,
                static (ctx, ct) => DiscoverTypeArguments(ctx, ct))
            .Where(static found => found.Length > 0)
            .Collect()
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) => AnalyzeTypeArguments(pair.Left, pair.Right, ct));
        context.RegisterSourceOutput(typeArguments, static (spc, result) => Report(spc, result));
    }

    /// <summary>
    /// The types passed for type parameters marked <c>[FakeForTypeArgument]</c> at one generic name, with where they were passed.
    /// Type parameters, abstract types, interfaces and framework types are left out: there is nothing to generate for them.
    /// </summary>
    private static ImmutableArray<(INamedTypeSymbol Type, Location Location)> DiscoverTypeArguments(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var name = (GenericNameSyntax)context.Node;
        var info = context.SemanticModel.GetSymbolInfo(name, cancellationToken);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
        var (parameters, arguments) = symbol switch
        {
            INamedTypeSymbol type => (type.OriginalDefinition.TypeParameters, type.TypeArguments),
            IMethodSymbol method => (method.OriginalDefinition.TypeParameters, method.TypeArguments),
            _ => (ImmutableArray<ITypeParameterSymbol>.Empty, ImmutableArray<ITypeSymbol>.Empty),
        };

        var found = ImmutableArray.CreateBuilder<(INamedTypeSymbol, Location)>();
        for (var index = 0; index < parameters.Length && index < arguments.Length; index++)
        {
            if (arguments[index] is INamedTypeSymbol argument && IsTypeArgumentRoot(argument) &&
                parameters[index].GetAttributes().Any(a => IsAttribute(a, TypeArgumentAttributeMetadataName)))
            {
                var location = index < name.TypeArgumentList.Arguments.Count ? name.TypeArgumentList.Arguments[index].GetLocation() : name.GetLocation();
                found.Add((argument, location));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>Whether a model can be written for <paramref name="type"/> as a type argument (the rest is skipped silently).</summary>
    private static bool IsTypeArgumentRoot(INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false, SpecialType: SpecialType.None } &&
        !ModelBuilder.IsFrameworkType(type) &&
        !ContainsTypeParameter(type);

    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type is ITypeParameterSymbol || (type is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Any(ContainsTypeParameter));

    /// <summary>Builds one class with the models of all discovered type arguments, registered at startup like <c>[FakeFor]</c> models.</summary>
    private static GenerationResult? AnalyzeTypeArguments(
        ImmutableArray<ImmutableArray<(INamedTypeSymbol Type, Location Location)>> found, Compilation compilation, CancellationToken cancellationToken)
    {
        var roots = new Dictionary<INamedTypeSymbol, Location>(SymbolEqualityComparer.Default);
        foreach (var (type, location) in found.SelectMany(f => f))
        {
            if (!roots.ContainsKey(type))
                roots.Add(type, location);
        }

        if (roots.Count == 0)
            return null;

        var diagnostics = new List<DiagnosticInfo>();
        var builder = new ModelBuilder(compilation, compilation.Assembly, compilation.AssemblyName ?? "this assembly", diagnostics, LocationInfo.From(Location.None));
        foreach (var root in roots.OrderBy(r => r.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            // Build each root (and what it contains) before the next, so diagnostics point at the place that requested it.
            builder.DiagnosticLocation = LocationInfo.From(root.Value);
            builder.Enqueue(root.Key);
            builder.BuildAll(cancellationToken);
        }

        if (builder.Models.Count == 0)
            return new GenerationResult(TypeArgumentHintName, null, diagnostics);

        var className = "__PhoneyTypeArgumentModels_" + Sanitize(compilation.AssemblyName ?? "Assembly");
        return new GenerationResult(TypeArgumentHintName, Emitter.EmitStandalone(className, builder.Models, builder.DerivedTypes), diagnostics);
    }

    /// <summary>Classes and records can carry the attributes; structs are rejected by the attributes' usage.</summary>
    private static bool IsClassOrRecord(SyntaxNode node) => node is ClassDeclarationSyntax or RecordDeclarationSyntax;

    private static void Report(SourceProductionContext context, GenerationResult? result)
    {
        if (result is null)
            return;
        foreach (var diagnostic in result.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        if (result.Source is not null)
            context.AddSource(result.HintName, result.Source);
    }

    /// <summary>Whether <paramref name="symbol"/> has an attribute whose generic definition is <paramref name="metadataName"/>.</summary>
    private static bool HasAttribute(INamedTypeSymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(a => IsAttribute(a, metadataName));

    /// <summary>The types listed with <c>[FakeFor&lt;T&gt;]</c> on <paramref name="owner"/>.</summary>
    internal static List<INamedTypeSymbol> Roots(INamedTypeSymbol owner) =>
        owner.GetAttributes()
            .Where(a => IsAttribute(a, AttributeMetadataName))
            .Select(a => a.AttributeClass?.TypeArguments.FirstOrDefault())
            .OfType<INamedTypeSymbol>()
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

    /// <summary>The <c>[FakeSubtype&lt;TBase, TSubtype&gt;]</c> pairs on <paramref name="owner"/>.</summary>
    internal static IEnumerable<(ITypeSymbol Base, INamedTypeSymbol Subtype)> SubtypePairs(INamedTypeSymbol owner)
    {
        foreach (var attribute in owner.GetAttributes().Where(a => IsAttribute(a, SubtypeAttributeMetadataName)))
        {
            var arguments = attribute.AttributeClass!.TypeArguments;
            if (arguments.Length == 2 && arguments[1] is INamedTypeSymbol subtype)
                yield return (arguments[0], subtype);
        }
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        var definition = attribute.AttributeClass?.OriginalDefinition;
        return definition is not null && definition.ContainingNamespace?.ToDisplayString() + "." + definition.MetadataName == metadataName;
    }

    /// <summary>Builds the generated source for one annotated class. Returns only strings so results cache well.</summary>
    private static GenerationResult Analyze(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)context.TargetNode;
        var location = LocationInfo.From(declaration.Identifier.GetLocation());
        var diagnostics = new List<DiagnosticInfo>();
        var hintName = Sanitize(symbol.ToDisplayString()) + ".Phoney.g.cs";

        // The class and every containing type must be partial and non-generic so we can add members to them.
        for (var type = symbol; type is not null; type = type.ContainingType)
        {
            if (type.IsGenericType)
            {
                diagnostics.Add(new DiagnosticInfo(Diagnostics.GenericContainer, location, type.Name));
                return new GenerationResult(hintName, null, diagnostics);
            }

            var isPartial = type.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax(cancellationToken))
                .OfType<TypeDeclarationSyntax>()
                .Any(d => d.Modifiers.Any(SyntaxKind.PartialKeyword));
            if (!isPartial)
            {
                diagnostics.Add(new DiagnosticInfo(Diagnostics.NotPartial, location, type.Name));
                return new GenerationResult(hintName, null, diagnostics);
            }
        }

        var compilation = context.SemanticModel.Compilation;
        var roots = Roots(symbol);

        // [FakeSubtype<TBase, TSubtype>]: register the pair at startup and generate a model for the subtype.
        var subtypes = new List<(string Base, string Subtype)>();
        var builder = new ModelBuilder(compilation, symbol, diagnostics, location);
        foreach (var (baseType, subtype) in SubtypePairs(symbol))
        {
            // Registering a subtype that can't be created would make the module initializer throw when the app starts.
            if (NotCreatable(subtype) is { } reason)
            {
                diagnostics.Add(new DiagnosticInfo(Diagnostics.SubtypeNotRegistrable, location, baseType.ToDisplayString(), subtype.ToDisplayString(), reason));
                continue;
            }

            subtypes.Add((baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), subtype.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            builder.Enqueue(subtype);
        }

        foreach (var root in roots)
            builder.Enqueue(root);
        builder.BuildAll(cancellationToken);

        // A subtype that got no model (PHONEY002: inaccessible, no public constructor) can't be generated, so it isn't registered either.
        var generated = new HashSet<string>(builder.Models.Select(m => m.FullName));
        subtypes.RemoveAll(s => !generated.Contains(s.Subtype));

        var source = Emitter.Emit(symbol, roots, builder.Models, builder.DerivedTypes, subtypes);
        return new GenerationResult(hintName, source, diagnostics);
    }

    /// <summary>Why <paramref name="type"/> can't be registered as a subtype, or <see langword="null"/> when it is a concrete class or struct.</summary>
    private static string? NotCreatable(INamedTypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Interface } => "it is an interface",
        { IsAbstract: true } => "it is abstract",
        { TypeKind: TypeKind.Class or TypeKind.Struct } => null,
        _ => "only classes and structs can be created",
    };

    /// <summary>Turns a display name into an identifier fragment (<c>Shop.Customer</c> → <c>Shop_Customer</c>).</summary>
    internal static string Sanitize(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }
}

/// <summary>Diagnostics reported by the generator.</summary>
internal static class Diagnostics
{
    /// <summary>
    /// Category of the advisory rules (analyzers and code-use hints), so a project can switch them off together with
    /// <c>dotnet_analyzer_diagnostic.category-Phoney.Usage.severity = none</c> without silencing the generator's errors (category <c>Phoney</c>).
    /// </summary>
    private const string UsageCategory = "Phoney.Usage";

    /// <summary>Category of the generator's own diagnostics.</summary>
    private const string GeneratorCategory = "Phoney";

    public static readonly DiagnosticDescriptor NotPartial = new(
        "PHONEY001", "Class must be partial",
        "'{0}' must be declared partial so Phoney can add the generated fake data members",
        GeneratorCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CannotGenerate = new(
        "PHONEY002", "Type cannot be generated",
        "Phoney cannot generate '{0}': {1}. Members of this type are left at their default value.",
        GeneratorCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StrictUncovered = new(
        "PHONEY004", "Strict generator leaves members without a value",
        "Strict generator for '{0}' has no rule, convention or annotation for: {1}. Add .With(...) or .Ignore(...) for them, or it throws when generating.",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DiscardedResult = new(
        "PHONEY005", "Result of an immutable Phoney call is discarded",
        "{0}",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SeededNotDisposed = new(
        "PHONEY006", "Seeded scope is never disposed",
        "Fake.Seeded returns a scope that stays active until it is disposed, so everything after this call in the same async flow stays seeded. Use 'using var _ = Fake.Seeded(...);'.",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RuleNotGenerated = new(
        "PHONEY007", "Rule can't be applied",
        "{0}",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RuleReplaced = new(
        "PHONEY008", "Rule is replaced by a later rule",
        "This rule replaces the earlier rule for '{0}' in the same chain, which has no effect. Remove one of them.",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SeedInLoop = new(
        "PHONEY010", "Constant seed in a loop repeats the same objects",
        "A new generator with a constant seed is created on every iteration, so every iteration generates the same objects. Create the generator before the loop, or derive the seed from the loop variable.",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ArgumentOutOfRange = new(
        "PHONEY011", "Argument is out of range",
        "{0}",
        UsageCategory, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoGeneratedModel = new(
        "PHONEY012", "Type is generated with reflection",
        "'{0}' is generated with reflection. Add [FakeFor<{0}>] to a partial class for a source-generated model that is also trim and Native AOT safe.",
        UsageCategory, DiagnosticSeverity.Info, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SubtypeNotRegistrable = new(
        "PHONEY009", "Subtype cannot be registered",
        "[FakeSubtype<{0}, {1}>] can't register '{1}': {2}. Use a concrete class or struct; the registration is left out.",
        GeneratorCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericContainer = new(
        "PHONEY003", "Generic classes are not supported",
        "[FakeFor] cannot be used inside generic type '{0}'; declare it on a non-generic partial class",
        GeneratorCategory, DiagnosticSeverity.Error, isEnabledByDefault: true);
}

/// <summary>A diagnostic captured as plain data so generator results stay equatable for incremental caching.</summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo location, params string[] arguments)
    {
        Descriptor = descriptor;
        Location = location;
        Arguments = arguments;
    }

    /// <summary>The descriptor to report.</summary>
    public DiagnosticDescriptor Descriptor { get; }

    /// <summary>Where to report it.</summary>
    public LocationInfo Location { get; }

    /// <summary>Message format arguments.</summary>
    public string[] Arguments { get; }

    /// <summary>Creates the Roslyn diagnostic.</summary>
    public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location.ToLocation(), Arguments);

    /// <inheritdoc />
    public bool Equals(DiagnosticInfo? other) =>
        other is not null && Descriptor.Id == other.Descriptor.Id && Location.Equals(other.Location) && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    /// <inheritdoc />
    public override int GetHashCode() => Descriptor.Id.GetHashCode() ^ Location.GetHashCode();
}

/// <summary>A source location as plain data.</summary>
internal sealed class LocationInfo : IEquatable<LocationInfo>
{
    private LocationInfo(string path, Microsoft.CodeAnalysis.Text.TextSpan span, Microsoft.CodeAnalysis.Text.LinePositionSpan lines)
    {
        Path = path;
        Span = span;
        Lines = lines;
    }

    /// <summary>Source file path.</summary>
    public string Path { get; }

    /// <summary>Character span in the file.</summary>
    public Microsoft.CodeAnalysis.Text.TextSpan Span { get; }

    /// <summary>Line/column span in the file.</summary>
    public Microsoft.CodeAnalysis.Text.LinePositionSpan Lines { get; }

    /// <summary>Captures a Roslyn location.</summary>
    public static LocationInfo From(Location location) =>
        new(location.SourceTree?.FilePath ?? "", location.SourceSpan, location.GetLineSpan().Span);

    /// <summary>Recreates the Roslyn location.</summary>
    public Location ToLocation() => Location.Create(Path, Span, Lines);

    /// <inheritdoc />
    public bool Equals(LocationInfo? other) => other is not null && Path == other.Path && Span.Equals(other.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocationInfo);

    /// <inheritdoc />
    public override int GetHashCode() => Path.GetHashCode() ^ Span.GetHashCode();
}

/// <summary>The output for one annotated class.</summary>
internal sealed class GenerationResult : IEquatable<GenerationResult>
{
    public GenerationResult(string hintName, string? source, List<DiagnosticInfo> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics.ToImmutableArray();
    }

    /// <summary>File name of the generated source.</summary>
    public string HintName { get; }

    /// <summary>Generated source, or <see langword="null"/> when generation failed.</summary>
    public string? Source { get; }

    /// <summary>Diagnostics to report.</summary>
    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <inheritdoc />
    public bool Equals(GenerationResult? other) =>
        other is not null && HintName == other.HintName && Source == other.Source && Diagnostics.SequenceEqual(other.Diagnostics);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GenerationResult);

    /// <inheritdoc />
    public override int GetHashCode() => HintName.GetHashCode() ^ (Source?.Length ?? 0);
}
