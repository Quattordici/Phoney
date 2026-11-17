using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reports lifecycles the runtime refuses: a lifecycle that can't work as written (PHONEY013: a refused builder call, no transitions, an undefined enum value, a
/// state the start can't reach, an end state too far away, or <c>GenerateHistory</c> on a generator without a lifecycle) and a stamp that can't be filled
/// (PHONEY014: not a date, not next to the status, a state the lifecycle doesn't have, a member stamped twice, or a non-nullable member of a state some
/// objects never enter).
/// </summary>
/// <remarks>
/// The lifecycle is replayed from the lambda's builder calls (<see cref="LifecycleSketch"/>) and checked with the graph the runtime checks it with, so only
/// constant arguments count: a state, weight or limit decided at runtime leaves what depends on it unchecked. A stamp that some objects skip is only reported
/// for chains that start a generator and generate, because only then does nothing else give the member its value.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LifecycleAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Diagnostics.LifecycleRefused, Diagnostics.StampRefused);

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
            start.RegisterOperationAction(c => Analyze(c, new Chain(generator, fake, fakeFor)), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Chain chain)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod;
        var onGenerator = AnalyzerSupport.IsDefinition(method.ContainingType, chain.Generator);
        if (method.Name == "GenerateHistory" && onGenerator)
        {
            CheckHistory(context, call, chain);
            return;
        }

        if (method.Name != "Lifecycle" || !(onGenerator || RuleGroup.IsView(method)) || LifecycleSketch.Read(call) is not { } sketch)
            return;
        foreach (var problem in sketch.Problems)
            context.ReportDiagnostic(Diagnostic.Create(problem.IsStamp ? Diagnostics.StampRefused : Diagnostics.LifecycleRefused, problem.Location, problem.Message));
        if (sketch.Problems.Count == 0)
            CheckSkippedStamps(context, call, sketch, chain);
    }

    /// <summary><c>GenerateHistory()</c> throws when the generator has no lifecycle: reported for chains that start a generator and add none.</summary>
    private static void CheckHistory(OperationAnalysisContext context, IInvocationOperation call, Chain chain)
    {
        if (!chain.Starts(call))
            return;
        foreach (var earlier in FluentChain.Earlier(call, chain.Generator))
        {
            if (RuleGroup.Of(earlier) is { } group && (group.Rules is null || group.Rules.Any(r => r.Name == "Lifecycle")))
                return; // a lifecycle, or a view whose rules can't be read
        }

        var type = call.TargetMethod.ContainingType.TypeArguments[0].Name;
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.LifecycleRefused, AnalyzerSupport.NameLocation(call),
            $"This generator of {type} has no lifecycle, so its objects have no history and GenerateHistory throws. Give a status member one first: .Lifecycle(x => x.Status, l => l.From(a).To(b))."));
    }

    /// <summary>
    /// A stamp that can't be <see langword="null"/> of a state some objects never enter throws when the generator compiles its rules: reported for the stamps of
    /// a lifecycle on a member of the generated type, written with <c>Stamp</c> or named after a state (<c>PaidAt</c>), when nothing else rules the member.
    /// </summary>
    private static void CheckSkippedStamps(OperationAnalysisContext context, IInvocationOperation call, LifecycleSketch sketch, Chain chain)
    {
        if (sketch.Graph is not { } graph || sketch.Status is not { Kind: SelectorKind.Member, IsNested: false, Name: { } status } || Holder(call, chain) is not { } holder
            || !chain.Starts(holder) || !FluentChain.Later(holder, chain.Generator).Any(c => AnalyzerSupport.Terminals.Contains(c.TargetMethod.Name)))
        {
            return;
        }

        var rules = Rules.Of(holder, chain, status);
        if (rules is null)
            return;
        foreach (var stamp in sketch.Stamps)
        {
            if (stamp is { IsRoot: true, State: >= 0, Type: { } type, MemberName: { } name } && LifecycleSketch.IsNonNullableValue(type) && !rules.Ruled.Contains(name)
                && graph.IsSkippable(stamp.State))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StampRefused, stamp.Location, Skipped(holder, name, sketch.StateText(stamp.State), type)));
            }
        }

        if (!rules.NameConventions || rules.HasFeed || holder.TargetMethod.ContainingType.TypeArguments[0] is not INamedTypeSymbol owner
            || ModelBuilder.DescribeMembers(context.Compilation, owner) is not { } members)
        {
            return;
        }

        foreach (var member in members)
        {
            if (member.Type is { } type && LifecycleSketch.CanHold(type) && LifecycleSketch.IsNonNullableValue(type) && !rules.Ruled.Contains(member.Name)
                && !sketch.Stamps.Any(s => s.IsRoot && s.MemberName == member.Name) && sketch.StateNamed(member.Name) is var state and >= 0 && graph.IsSkippable(state))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StampRefused, AnalyzerSupport.NameLocation(call), Skipped(holder, member.Name, sketch.StateText(state), type)));
            }
        }
    }

    private static string Skipped(IInvocationOperation holder, string member, string state, ITypeSymbol type) =>
        $"{holder.TargetMethod.ContainingType.TypeArguments[0].Name}.{member} is the time an object entered {state}, but some objects never get there, and a {type.Name} can't be null. "
        + $"Make it nullable ({type.Name}?), or leave it out with .Ignore(x => x.{member}).";

    /// <summary>The generator call a lifecycle is added with: the <c>Lifecycle</c> call itself, or the <c>Member</c> call around a view's.</summary>
    private static IInvocationOperation? Holder(IInvocationOperation call, Chain chain)
    {
        if (!RuleGroup.IsView(call.TargetMethod))
            return call;
        for (var parent = call.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IInvocationOperation candidate && AnalyzerSupport.IsDefinition(candidate.TargetMethod.ContainingType, chain.Generator))
                return candidate.TargetMethod.Name == RuleCatalog.Member ? candidate : null;
        }

        return null;
    }

    /// <summary>The generator type and what starts a chain, for <see cref="AnalyzerSupport.IsStart"/>.</summary>
    private sealed record Chain(INamedTypeSymbol Generator, INamedTypeSymbol? Fake, INamedTypeSymbol? FakeFor)
    {
        public bool Starts(IInvocationOperation call) => AnalyzerSupport.IsStart(FluentChain.Start(call, Generator), Fake, FakeFor);
    }

    /// <summary>What the other calls of a chain say about the members of the generated type: which have rules, whether names give values, whether a feed does.</summary>
    private sealed class Rules
    {
        public HashSet<string> Ruled { get; } = new(System.StringComparer.Ordinal);

        public bool NameConventions { get; private set; } = true;

        public bool HasFeed { get; private set; }

        /// <summary>
        /// Reads the chain around <paramref name="holder"/>, or returns <see langword="null"/> when what it does to the members can't be known (a selector or flag
        /// decided at runtime) or a later rule on <paramref name="status"/> replaces the lifecycle.
        /// </summary>
        public static Rules? Of(IInvocationOperation holder, Chain chain, string status)
        {
            var rules = new Rules();
            var later = FluentChain.Later(holder, chain.Generator).ToList();
            foreach (var call in FluentChain.Earlier(holder, chain.Generator).Concat(later))
            {
                if (!rules.Read(call, status, replacesLifecycle: later.Contains(call)))
                    return null;
            }

            return rules;
        }

        /// <summary>Records what <paramref name="call"/> does to the members; <see langword="false"/> when that can't be known, or when it replaces the lifecycle of <paramref name="status"/>.</summary>
        private bool Read(IInvocationOperation call, string status, bool replacesLifecycle)
        {
            if (RuleGroup.Of(call) is { } group)
            {
                if (group.Selector.Kind == SelectorKind.Combination)
                    return true;
                if (group.Selector is not { Kind: SelectorKind.Member, Name: { } name })
                    return false;
                if (group.Selector.IsNested)
                    return true;
                if (replacesLifecycle && name == status && (group.Rules is null || group.Rules.Any(r => r.Name is not ("Invalid" or "Where" or RuleCatalog.Unique))))
                    return false; // the lifecycle is replaced, so its stamps don't apply
                Ruled.Add(name);
                return true;
            }

            switch (call.TargetMethod.Name)
            {
                case "WithFeed":
                    HasFeed = true;
                    return true;
                case "WithNameConventions":
                    var argument = call.Arguments.FirstOrDefault();
                    if (argument is null || argument.ArgumentKind == ArgumentKind.DefaultValue)
                        NameConventions = true;
                    else if (argument.Value.ConstantValue is { HasValue: true, Value: bool on })
                        NameConventions = on;
                    else
                        return false;
                    return true;
                default:
                    return true;
            }
        }
    }
}
