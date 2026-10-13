using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reports at compile time what <c>Generator&lt;T&gt;.Strict()</c> would throw at runtime: scalar members that have
/// neither a rule, a name convention nor a data annotation defining their value.
/// </summary>
/// <remarks>
/// Only fluent chains that start a generator (<c>Fake.For&lt;T&gt;()</c>, <c>new Generator&lt;T&gt;(…)</c> or a
/// <c>[FakeFor]</c> property) and end in <c>Generate</c>, <c>Stream</c>, <c>GenerateParallel</c> or <c>Populate</c> are
/// checked, because only then are all rules known. App-wide <c>Fake.Conventions</c> are registered at runtime and
/// can't be seen, so a member they cover is still reported; suppress it with <c>.Ignore(...)</c> or <c>#pragma</c>.
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class StrictAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> MemberRules = new(StringComparer.Ordinal) { "With", "WithOneOf", "Ignore", "Unique" };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.StrictUncovered);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var generator = start.Compilation.GetTypeByMetadataName("Phoney.Generator`1");
            var fake = start.Compilation.GetTypeByMetadataName("Phoney.Fake");
            var fakeFor = start.Compilation.GetTypeByMetadataName("Phoney.Generation.FakeForAttribute`1");
            if (generator is null)
                return;
            start.RegisterOperationAction(c => Analyze(c, generator, fake, fakeFor), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol generator, INamedTypeSymbol? fake, INamedTypeSymbol? fakeFor)
    {
        var strict = (IInvocationOperation)context.Operation;
        if (strict.TargetMethod.Name != "Strict" || !IsGeneratorMethod(strict.TargetMethod, generator) || !IsTrue(strict.Arguments))
            return;

        // Later calls in the chain (outer invocations): the chain must end in a terminal call, and a later Strict(...) is analyzed on its own.
        IOperation current = strict;
        var later = new List<IInvocationOperation>();
        while (current.Parent is IInvocationOperation outer && outer.Instance == current && IsGeneratorMethod(outer.TargetMethod, generator))
        {
            later.Add(outer);
            current = outer;
        }

        if (later.Count == 0 || !AnalyzerSupport.Terminals.Contains(later[later.Count - 1].TargetMethod.Name) || later.Any(i => i.TargetMethod.Name == "Strict"))
            return;

        // Earlier calls (receivers), down to where the generator was started.
        var earlier = new List<IInvocationOperation>();
        var receiver = strict.Instance;
        while (receiver is IInvocationOperation inner && IsGeneratorMethod(inner.TargetMethod, generator))
        {
            earlier.Add(inner);
            receiver = inner.Instance;
        }

        if (!AnalyzerSupport.IsStart(receiver, fake, fakeFor))
            return;

        var type = ((INamedTypeSymbol)strict.TargetMethod.ContainingType).TypeArguments[0] as INamedTypeSymbol;
        if (type is null || type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        var covered = new HashSet<string>(StringComparer.Ordinal);
        var conventionTypes = new List<ITypeSymbol>();
        var useConventions = true;
        foreach (var call in earlier.Concat(later))
        {
            var name = call.TargetMethod.Name;
            if (MemberRules.Contains(name))
            {
                if (SelectedMember(call) is not { } member)
                    return; // the member can't be read from the source, so don't guess
                covered.Add(member);
            }
            else if (name == "WithConvention")
            {
                conventionTypes.Add(call.TargetMethod.TypeArguments[0]);
            }
            else if (name == "UseConventions")
            {
                var argument = call.Arguments.FirstOrDefault();
                if (argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue)
                    useConventions = true;
                else if (argument.Value.ConstantValue is { HasValue: true, Value: bool value })
                    useConventions = value;
                else
                    return; // decided at runtime
            }
        }

        var members = ModelBuilder.DescribeMembers(context.Compilation, type);
        if (members is null)
            return;

        var uncovered = members
            .Where(m => m.IsScalar && !m.DefinesValue && !covered.Contains(m.Name)
                && (m.Convention == nameof(ConventionKind.None) || !useConventions)
                && !conventionTypes.Any(t => Matches(t, m.Type)))
            .Select(m => m.Name)
            .ToList();
        if (uncovered.Count == 0)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StrictUncovered, AnalyzerSupport.NameLocation(strict), type.Name, string.Join(", ", uncovered)));
    }

    private static bool IsGeneratorMethod(IMethodSymbol method, INamedTypeSymbol generator) =>
        AnalyzerSupport.IsDefinition(method.ContainingType, generator);

    /// <summary>Whether a <c>bool</c> parameter is left at its default (<see langword="true"/>) or passed a constant <see langword="true"/>.</summary>
    private static bool IsTrue(ImmutableArray<IArgumentOperation> arguments)
    {
        var argument = arguments.FirstOrDefault();
        return argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue || argument.Value.ConstantValue is { HasValue: true, Value: true };
    }

    /// <summary>The member named by the first argument, <c>x =&gt; x.Name</c> (also through a cast), or <see langword="null"/>.</summary>
    private static string? SelectedMember(IInvocationOperation call) =>
        RuleSelector.Read(call) is { Kind: SelectorKind.Member, Name: { } name } ? name : null;

    /// <summary>Whether a <c>WithConvention&lt;TValue&gt;</c> applies to a member type (also its nullable form), like <c>CustomConvention.Matches</c>.</summary>
    private static bool Matches(ITypeSymbol conventionType, ITypeSymbol? memberType)
    {
        if (memberType is null)
            return false;
        if (memberType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            memberType = nullable.TypeArguments[0];
        return SymbolEqualityComparer.Default.Equals(conventionType.WithNullableAnnotation(NullableAnnotation.None), memberType.WithNullableAnnotation(NullableAnnotation.None));
    }
}
