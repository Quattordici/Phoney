using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Phony.SourceGenerator;

/// <summary>
/// Generates reflection-free <c>FakeModel&lt;T&gt;</c> implementations for every type listed with
/// <c>[FakeFor&lt;T&gt;]</c> on a partial class (plus the types they contain), a <c>Generator&lt;T&gt;</c> property
/// per listed type, and a module initializer that registers the models so <c>Fake.For&lt;T&gt;()</c> uses them too.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class FakeForGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "Phony.Generation.FakeForAttribute`1";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, ct) => Analyze(ctx, ct));

        context.RegisterSourceOutput(targets, static (spc, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
                spc.ReportDiagnostic(diagnostic.ToDiagnostic());
            if (result.Source is not null)
                spc.AddSource(result.HintName, result.Source);
        });
    }

    /// <summary>Builds the generated source for one annotated class. Returns only strings so results cache well.</summary>
    private static GenerationResult Analyze(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var declaration = (ClassDeclarationSyntax)context.TargetNode;
        var location = LocationInfo.From(declaration.Identifier.GetLocation());
        var diagnostics = new List<DiagnosticInfo>();
        var hintName = Sanitize(symbol.ToDisplayString()) + ".Phony.g.cs";

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
        var roots = context.Attributes
            .Select(a => a.AttributeClass?.TypeArguments.FirstOrDefault())
            .OfType<INamedTypeSymbol>()
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToList();

        var builder = new ModelBuilder(compilation, symbol, diagnostics, location);
        foreach (var root in roots)
            builder.Enqueue(root);
        builder.BuildAll(cancellationToken);

        var source = Emitter.Emit(symbol, roots, builder.Models);
        return new GenerationResult(hintName, source, diagnostics);
    }

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
    public static readonly DiagnosticDescriptor NotPartial = new(
        "PHONY001", "Class must be partial",
        "'{0}' must be declared partial so Phony can add the generated fake data members",
        "Phony", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CannotGenerate = new(
        "PHONY002", "Type cannot be generated",
        "Phony cannot generate '{0}': {1}. Members of this type are left at their default value.",
        "Phony", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenericContainer = new(
        "PHONY003", "Generic classes are not supported",
        "[FakeFor] cannot be used inside generic type '{0}'; declare it on a non-generic partial class",
        "Phony", DiagnosticSeverity.Error, isEnabledByDefault: true);
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
