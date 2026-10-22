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
/// Reports at compile time what <c>ObjectGenerator&lt;T&gt;.Strict()</c> would throw at runtime: scalar members that have
/// neither a rule, a name convention nor a data annotation defining their value.
/// </summary>
/// <remarks>
/// Only fluent chains that start a generator (<c>Fake.For&lt;T&gt;()</c>, <c>new ObjectGenerator&lt;T&gt;(…)</c> or a
/// <c>[FakeFor]</c> property) and end in <c>Generate</c>, <c>Stream</c>, <c>GenerateParallel</c> or <c>Populate</c> are
/// checked, because only then are all rules known. App-wide <c>Fake.Conventions</c> are registered at runtime and
/// can't be seen, so a member they cover is still reported; suppress it with <c>.Ignore(...)</c> or <c>#pragma</c>.
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class StrictAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> MemberRules = new(StringComparer.Ordinal) { "With", "WithNormalDistribution", "WithOneOf", "WithSubsetOf", "Ignore", "Unique", "Invalid", "Boundary", "Sequence", "Cycle" };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.StrictUncovered);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var generator = start.Compilation.GetTypeByMetadataName("Phoney.ObjectGenerator`1");
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

        var calls = Chain(strict, generator, fake, fakeFor);
        if (calls is null)
            return;

        var type = strict.TargetMethod.ContainingType.TypeArguments[0] as INamedTypeSymbol;
        if (type is null || type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        var coverage = new Coverage();
        if (!calls.TrueForAll(coverage.Read))
            return;

        var members = ModelBuilder.DescribeMembers(context.Compilation, type);
        if (members is null)
            return;

        var uncovered = members.Where(coverage.LeavesUncovered).Select(m => m.Name).ToList();
        if (uncovered.Count == 0)
            return;

        // The member names go along so the code fix can add .Ignore(...) for them.
        var properties = ImmutableDictionary<string, string?>.Empty.Add(MembersProperty, string.Join(",", uncovered));
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StrictUncovered, AnalyzerSupport.NameLocation(strict), properties: properties, type.Name, string.Join(", ", uncovered)));
    }

    /// <summary>The diagnostic property with the uncovered member names, comma separated.</summary>
    public const string MembersProperty = "Members";

    /// <summary>
    /// The other calls of the chain <paramref name="strict"/> is in, earlier ones first, or <see langword="null"/> when the chain doesn't start a generator,
    /// doesn't end in a terminal call, or has a later <c>Strict(...)</c> (which is analyzed on its own).
    /// </summary>
    private static List<IInvocationOperation>? Chain(IInvocationOperation strict, INamedTypeSymbol generator, INamedTypeSymbol? fake, INamedTypeSymbol? fakeFor)
    {
        // Later calls in the chain (outer invocations).
        IOperation current = strict;
        var later = new List<IInvocationOperation>();
        while (current.Parent is IInvocationOperation outer && outer.Instance == current && IsGeneratorMethod(outer.TargetMethod, generator))
        {
            later.Add(outer);
            current = outer;
        }

        if (later.Count == 0 || !AnalyzerSupport.Terminals.Contains(later[later.Count - 1].TargetMethod.Name) || later.Exists(i => i.TargetMethod.Name == "Strict"))
            return null;

        // Earlier calls (receivers), down to where the generator was started.
        var earlier = new List<IInvocationOperation>();
        var receiver = strict.Instance;
        while (receiver is IInvocationOperation inner && IsGeneratorMethod(inner.TargetMethod, generator))
        {
            earlier.Add(inner);
            receiver = inner.Instance;
        }

        if (!AnalyzerSupport.IsStart(receiver, fake, fakeFor))
            return null;
        earlier.Reverse(); // collected from Strict outward; the calls run first to last
        earlier.AddRange(later);
        return earlier;
    }

    /// <summary>What the calls of a chain give values to: members with rules, types with conventions, and whether name conventions apply.</summary>
    private sealed class Coverage
    {
        private readonly HashSet<string> _covered = new(StringComparer.Ordinal);
        private readonly List<ITypeSymbol> _conventionTypes = [];
        private bool _useConventions = true;
        private bool _useParameterDefaults;

        /// <summary>Records what <paramref name="call"/> covers; <see langword="false"/> when that can't be known from the source, so nothing is reported.</summary>
        public bool Read(IInvocationOperation call)
        {
            var name = call.TargetMethod.Name;
            if (MemberRules.Contains(name))
                return ReadMemberRule(call);
            switch (name)
            {
                case "WithFeed":
                    return false; // the columns of the feed are read at runtime: which members they cover can't be known here
                case "WithConvention" or "WithEveryMemberOfType":
                    _conventionTypes.Add(call.TargetMethod.TypeArguments[0]);
                    return true;
                case "WithNameConventions":
                    return ReadFlag(call, ref _useConventions);
                case "WithParameterDefaults":
                    return ReadFlag(call, ref _useParameterDefaults);
                default:
                    return true;
            }
        }

        /// <summary>Whether a member is a scalar that nothing gives a value to.</summary>
        public bool LeavesUncovered(MemberModel member) =>
            member.IsScalar && !member.DefinesValue && !_covered.Contains(member.Name) && !(_useParameterDefaults && member is { IsConstructorParameter: true, DefaultValue: not null })
            && (member.Convention == nameof(ConventionKind.None) || !_useConventions)
            && !_conventionTypes.Exists(t => Matches(t, member.Type));

        private bool ReadMemberRule(IInvocationOperation call)
        {
            var selector = RuleSelector.Read(call);
            if (selector.Kind == SelectorKind.Combination)
                return true; // a unique combination checks members, it doesn't give them values
            if (selector.Kind != SelectorKind.Member)
                return false; // the member can't be read from the source, so don't guess
            if (!selector.IsNested)
                _covered.Add(selector.Name!); // a rule for Billing.City is not a rule for Billing
            return true;
        }

        /// <summary>Reads the <c>bool</c> argument of an on/off call (<see langword="true"/> when left out); <see langword="false"/> when it is decided at runtime.</summary>
        private static bool ReadFlag(IInvocationOperation call, ref bool flag)
        {
            var argument = call.Arguments.FirstOrDefault();
            if (argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue)
                flag = true;
            else if (argument.Value.ConstantValue is { HasValue: true, Value: bool value })
                flag = value;
            else
                return false; // decided at runtime
            return true;
        }

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

    private static bool IsGeneratorMethod(IMethodSymbol method, INamedTypeSymbol generator) =>
        AnalyzerSupport.IsDefinition(method.ContainingType, generator);

    /// <summary>Whether a <c>bool</c> parameter is left at its default (<see langword="true"/>) or passed a constant <see langword="true"/>.</summary>
    private static bool IsTrue(ImmutableArray<IArgumentOperation> arguments)
    {
        var argument = arguments.FirstOrDefault();
        return argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue || argument.Value.ConstantValue is { HasValue: true, Value: true };
    }

}
