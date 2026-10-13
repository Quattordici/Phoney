using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.SourceGenerator;

/// <summary>
/// Suggests <c>[FakeFor&lt;T&gt;]</c> (PHONEY012) where <c>Fake.For&lt;T&gt;()</c>, <c>One</c>, <c>Many</c> or <c>Populate</c>
/// generate a type with reflection, because no source-generated model covers it.
/// </summary>
/// <remarks>
/// A type has a model when it is listed with <c>[FakeFor]</c>/<c>[FakeSubtype]</c> or contained in a listed type, in this
/// compilation or in a referenced assembly that references Phoney. The diagnostic is informational: reflection is fine
/// unless the app is trimmed or published with Native AOT, where the compiler already warns at the call.
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class ModelSuggestionAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> EntryPoints = new(StringComparer.Ordinal) { "For", "One", "Many", "Populate" };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.NoGeneratedModel);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var fake = start.Compilation.GetTypeByMetadataName("Phoney.Fake");
            if (fake is null)
                return;
            var modelled = new Lazy<HashSet<string>>(() => ModelledTypes(start.Compilation, fake.ContainingAssembly.Name, start.CancellationToken), LazyThreadSafetyMode.ExecutionAndPublication);
            start.RegisterOperationAction(c => Analyze(c, fake, modelled), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol fake, Lazy<HashSet<string>> modelled)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod;
        if (!EntryPoints.Contains(method.Name) || !SymbolEqualityComparer.Default.Equals(method.ContainingType, fake) ||
            method.TypeArguments.FirstOrDefault() is not INamedTypeSymbol type)
        {
            return;
        }

        if (!IsCandidate(context.Compilation, type) || modelled.Value.Contains(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            return;
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoGeneratedModel, AnalyzerSupport.NameLocation(call), type.Name));
    }

    /// <summary>Whether a source-generated model could be written for <paramref name="type"/> from this compilation.</summary>
    internal static bool IsCandidate(Compilation compilation, INamedTypeSymbol type) =>
        type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false, IsGenericType: false, SpecialType: SpecialType.None } &&
        !ModelBuilder.IsFrameworkType(type) &&
        compilation.IsSymbolAccessibleWithin(type, compilation.Assembly) &&
        ModelBuilder.DescribeMembers(compilation, type) is not null;

    /// <summary>The fully qualified names of all types the generator writes models for, as the generator would compute them.</summary>
    private static HashSet<string> ModelledTypes(Compilation compilation, string phoneyAssembly, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in ModelOwners(compilation, phoneyAssembly, cancellationToken))
        {
            var builder = new ModelBuilder(compilation, owner, new List<DiagnosticInfo>(), LocationInfo.From(Location.None));
            foreach (var root in FakeForGenerator.Roots(owner))
                builder.Enqueue(root);
            foreach (var pair in FakeForGenerator.SubtypePairs(owner))
                builder.Enqueue(pair.Subtype);
            builder.BuildAll(cancellationToken);
            names.UnionWith(builder.Models.Select(m => m.FullName));
        }

        return names;
    }

    /// <summary>Types carrying <c>[FakeFor]</c> or <c>[FakeSubtype]</c>: declared in the source, or in referenced assemblies that use Phoney.</summary>
    private static IEnumerable<INamedTypeSymbol> ModelOwners(Compilation compilation, string phoneyAssembly, CancellationToken cancellationToken)
    {
        // Source types: look at the attribute names in the syntax first, so only candidates have their attributes bound.
        foreach (var type in AllTypes(compilation.Assembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.DeclaringSyntaxReferences.Any(r => r.GetSyntax(cancellationToken) is TypeDeclarationSyntax d && d.AttributeLists.Any(list => list.Attributes.Any(a => IsModelAttribute(a.Name)))))
                yield return type;
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly &&
                assembly.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == phoneyAssembly)))
            {
                foreach (var type in AllTypes(assembly.GlobalNamespace))
                {
                    if (type.GetAttributes().Any(a => a.AttributeClass is { Name: "FakeForAttribute" or "FakeSubtypeAttribute" }))
                        yield return type;
                }
            }
        }
    }

    private static bool IsModelAttribute(NameSyntax name)
    {
        var simple = name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax alias => alias.Name,
            SimpleNameSyntax s => s,
            _ => null,
        };
        return simple?.Identifier.ValueText is "FakeFor" or "FakeForAttribute" or "FakeSubtype" or "FakeSubtypeAttribute";
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamespaceSymbol child)
            {
                foreach (var type in AllTypes(child))
                    yield return type;
            }
            else if (member is INamedTypeSymbol named)
            {
                foreach (var type in WithNested(named))
                    yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var inner in WithNested(nested))
                yield return inner;
        }
    }
}
