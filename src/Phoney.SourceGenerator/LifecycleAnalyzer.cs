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
        if (sketch.Problems.Count != 0)
            return;
        CheckIdleStamps(context, call, sketch, chain);
        CheckSkippedStamps(context, call, sketch, chain);
    }

    /// <summary>
    /// A <c>Stamp</c> that could never apply is refused when the lifecycle is added: its member already has a rule (which wins), or another lifecycle of the
    /// object already stamps it. Read from the calls earlier in the chain, for a lifecycle on a member of the generated type.
    /// </summary>
    private static void CheckIdleStamps(OperationAnalysisContext context, IInvocationOperation call, LifecycleSketch sketch, Chain chain)
    {
        if (sketch.Status is not { Kind: SelectorKind.Member, IsNested: false, Name: { } status } || Holder(call, chain) is not { } holder
            || EarlierRules.Of(holder, chain) is not { } earlier)
        {
            return;
        }

        var owner = holder.TargetMethod.ContainingType.TypeArguments[0].Name;
        foreach (var stamp in sketch.Stamps)
        {
            if (stamp is not { IsRoot: true, MemberName: { } name } || !earlier.TryGetValue(name, out var rule) && !earlier.Values.Any(e => e.Stamps(name)))
                continue;
            if (rule is { IsUnknown: false, Lifecycle: null })
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StampRefused, stamp.Location,
                    $"{owner}.{name} has a rule of its own, and the lifecycle of {owner}.{status} stamps it. The rule wins, so the Stamp would do nothing. Remove the rule or the Stamp."));
            }
            else if (rule is null && earlier.Where(e => e.Key != status && e.Value.Stamps(name)).OrderBy(e => e.Key, System.StringComparer.Ordinal).FirstOrDefault() is { Key: { } other })
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.StampRefused, stamp.Location,
                    $"{owner}.{name} is stamped by the lifecycles of both {owner}.{other} and {owner}.{status}. Give each member one state."));
            }
        }
    }

    /// <summary>
    /// <c>GenerateHistory()</c> throws when neither the generator nor a generator it uses for a nested type has a lifecycle: reported for chains that start a
    /// generator and add none, directly or through a <c>WithGeneratorFor</c> whose generator can be read. One registered app-wide (<c>Fake.Generators</c>) can't
    /// be seen, so the message names it.
    /// </summary>
    private static void CheckHistory(OperationAnalysisContext context, IInvocationOperation call, Chain chain)
    {
        if (MayHaveLifecycle(call.Instance, chain))
            return;
        var type = call.TargetMethod.ContainingType.TypeArguments[0].Name;
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.LifecycleRefused, AnalyzerSupport.NameLocation(call),
            $"This generator of {type} has no lifecycle, and neither has a generator it uses for a nested type, so its objects have no history and GenerateHistory throws. "
            + "Give a status member one first: .Lifecycle(x => x.Status, l => l.From(a).To(b)), or register a generator with one (WithGeneratorFor, Fake.Generators)."));
    }

    /// <summary>
    /// Whether the generator <paramref name="generator"/> evaluates to may have a lifecycle: one of its calls adds one (or is a view whose rules can't be read),
    /// a <c>WithGeneratorFor</c> brings one, or the chain doesn't start where the analyzer can see it (a variable, a method), so nothing is known.
    /// </summary>
    private static bool MayHaveLifecycle(IOperation? generator, Chain chain)
    {
        while (generator is IConversionOperation conversion)
            generator = conversion.Operand;
        if (generator is not IInvocationOperation last || !AnalyzerSupport.IsDefinition(last.TargetMethod.ContainingType, chain.Generator))
            return !AnalyzerSupport.IsStart(generator, chain.Fake, chain.FakeFor);
        foreach (var call in FluentChain.Earlier(last, chain.Generator).Prepend(last))
        {
            if (RuleGroup.Of(call) is { } group && (group.Rules is null || group.Rules.Any(r => r.Name == "Lifecycle")))
                return true; // a lifecycle, or a view whose rules can't be read
            if (call.TargetMethod.Name == "WithGeneratorFor" && MayHaveLifecycle(call.Arguments.FirstOrDefault()?.Value, chain))
                return true;
        }

        return !chain.Starts(last);
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

    /// <summary>What a member of the generated type has when a later call is made: a rule, a lifecycle (with its stamps), or something that can't be read.</summary>
    private sealed record EarlierRule(LifecycleSketch? Lifecycle, bool IsUnknown)
    {
        public static readonly EarlierRule Plain = new(null, false);

        public static readonly EarlierRule Unknown = new(null, true);

        /// <summary>Whether this is a lifecycle that stamps <paramref name="member"/> with <c>Stamp</c>.</summary>
        public bool Stamps(string member) => Lifecycle is { } lifecycle && lifecycle.Stamps.Any(s => s.IsRoot && s.MemberName == member);
    }

    /// <summary>The rules the calls before a generator call leave on the members of the generated type, as the runtime keeps them: the last rule of a member wins.</summary>
    private static class EarlierRules
    {
        /// <summary>The rules by member name before <paramref name="holder"/>, or <see langword="null"/> when a call selects a member that can't be read.</summary>
        public static Dictionary<string, EarlierRule>? Of(IInvocationOperation holder, Chain chain)
        {
            var rules = new Dictionary<string, EarlierRule>(System.StringComparer.Ordinal);
            foreach (var call in FluentChain.Earlier(holder, chain.Generator).Reverse())
            {
                if (RuleGroup.Of(call) is not { } group)
                    continue;
                switch (group.Selector)
                {
                    case { Kind: SelectorKind.Combination } or { IsNested: true }:
                        continue;
                    case { Kind: SelectorKind.Member, Name: { } name }:
                        if (group.Rules is null)
                        {
                            rules[name] = EarlierRule.Unknown;
                            continue;
                        }

                        foreach (var rule in group.Rules)
                            Apply(rules, name, rule);
                        continue;
                    default:
                        return null;
                }
            }

            return rules;
        }

        /// <summary>What <paramref name="rule"/> leaves on <paramref name="member"/>: a condition or uniqueness adds no rule, and <c>Invalid</c> keeps a lifecycle.</summary>
        private static void Apply(Dictionary<string, EarlierRule> rules, string member, RuleCall rule)
        {
            switch (rule.Name)
            {
                case "Where" or RuleCatalog.Unique:
                    return;
                case "Lifecycle":
                    rules[member] = LifecycleSketch.Read(rule.Call) is { } sketch ? new EarlierRule(sketch, false) : EarlierRule.Unknown;
                    return;
                case "Invalid" when rules.TryGetValue(member, out var kept) && kept.Lifecycle is not null:
                    return;
                default:
                    rules[member] = EarlierRule.Plain;
                    return;
            }
        }
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
            var known = FluentChain.Earlier(holder, chain.Generator).All(call => rules.Read(call, status, replacesLifecycle: false))
                && later.TrueForAll(call => rules.Read(call, status, replacesLifecycle: true));
            return known ? rules : null;
        }

        /// <summary>Records what <paramref name="call"/> does to the members; <see langword="false"/> when that can't be known, or when it replaces the lifecycle of <paramref name="status"/>.</summary>
        private bool Read(IInvocationOperation call, string status, bool replacesLifecycle)
        {
            if (RuleGroup.Of(call) is { } group)
                return ReadGroup(group, status, replacesLifecycle);
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

        /// <summary>Records the member a rule is for; <see langword="false"/> when it can't be read, or when a later rule replaces the lifecycle of <paramref name="status"/>.</summary>
        private bool ReadGroup(RuleGroup group, string status, bool replacesLifecycle)
        {
            switch (group.Selector)
            {
                case { Kind: SelectorKind.Combination } or { IsNested: true }:
                    return true;
                case { Kind: SelectorKind.Member, Name: { } name }:
                    if (replacesLifecycle && name == status && (group.Rules is null || group.Rules.Any(r => r.Name is not ("Invalid" or "Where" or RuleCatalog.Unique))))
                        return false; // the lifecycle is replaced, so its stamps don't apply
                    Ruled.Add(name);
                    return true;
                default:
                    return false;
            }
        }
    }
}
