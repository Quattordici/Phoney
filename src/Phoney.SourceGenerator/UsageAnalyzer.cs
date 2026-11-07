using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reports calls that silently do nothing or throw when a generator is first used: results of immutable generators that
/// are thrown away (PHONEY005), <c>Fake.Seeded</c> scopes that are never disposed (PHONEY006), rules for members that
/// can't be generated (PHONEY007), rules that replace each other in one chain (PHONEY008), new generators with a constant
/// seed in a loop (PHONEY010) and constant arguments that make a call throw (PHONEY011).
/// </summary>
/// <remarks>
/// The member checks mirror <c>ObjectGenerator&lt;T&gt;.Compile</c> and use <see cref="ModelBuilder.DescribeMembers"/>, so they
/// are skipped for generators over a hand-written model (<c>new ObjectGenerator&lt;T&gt;(model)</c>), whose members are unknown.
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class UsageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>What a count, size or depth that may not be negative must be.</summary>
    private const string AtLeastZero = "at least 0";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.DiscardedResult, Diagnostics.SeededNotDisposed, Diagnostics.RuleNotGenerated, Diagnostics.RuleReplaced,
            Diagnostics.SeedInLoop, Diagnostics.ArgumentOutOfRange);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var generator = start.Compilation.GetTypeByMetadataName("Phoney.ObjectGenerator`1");
            var fake = start.Compilation.GetTypeByMetadataName("Phoney.Fake");
            if (generator is null || fake is null)
                return;
            var fakeFor = start.Compilation.GetTypeByMetadataName("Phoney.Generation.FakeForAttribute`1");
            var symbols = new Symbols(start.Compilation, generator, fake, fakeFor);
            start.RegisterOperationAction(c => Analyze(c, symbols), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Symbols symbols)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod;
        if (RuleGroup.IsView(method))
        {
            CheckViewCall(context, call);
            return;
        }

        var onFake = AnalyzerSupport.IsDefinition(method.ContainingType, symbols.Fake);
        var onGenerator = !onFake && AnalyzerSupport.IsDefinition(method.ContainingType, symbols.Generator);
        if (!onFake && !onGenerator)
            return;

        if (onFake && method.Name == "Seeded")
            CheckSeeded(context, call);
        else if (method.Name == "Populate")
            CheckPopulate(context, call, onFake);
        else if (onGenerator)
            CheckGenerator(context, call, symbols);

        CheckArguments(context, call, method.Name);
        // A keyed call gives every key its own object, so a constant seed in a loop is the point there (it keeps the mapping stable).
        if (onGenerator && AnalyzerSupport.Terminals.Contains(method.Name) && method.Name is not ("GenerateFor" or "Pseudonymize"))
            CheckSeedInLoop(context, call, symbols);
    }

    /// <summary>PHONEY010: every iteration builds a new generator from the same seed, so every iteration gets the same objects.</summary>
    private static void CheckSeedInLoop(OperationAnalysisContext context, IInvocationOperation terminal, Symbols symbols)
    {
        // The last Seed in the chain wins; a seed that isn't a constant may vary per iteration.
        var seed = FluentChain.Earlier(terminal, symbols.Generator).FirstOrDefault(earlier => earlier.TargetMethod.Name == "Seed");
        if (seed is null || seed.Arguments.FirstOrDefault()?.Value.ConstantValue is not { HasValue: true }
            || !AnalyzerSupport.IsStart(FluentChain.Start(terminal, symbols.Generator), symbols.Fake, symbols.FakeFor))
        {
            return;
        }

        IOperation child = terminal;
        for (var parent = terminal.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent is ILoopOperation loop && RunsEveryIteration(loop, child))
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SeedInLoop, AnalyzerSupport.NameLocation(seed)));
                return;
            }
        }
    }

    /// <summary>Whether <paramref name="part"/> of <paramref name="loop"/> runs again on every iteration (not the collection of a <c>foreach</c> or the initializers of a <c>for</c>).</summary>
    private static bool RunsEveryIteration(ILoopOperation loop, IOperation part) => loop switch
    {
        IForEachLoopOperation forEach => forEach.Collection != part,
        IForLoopOperation forLoop => !forLoop.Before.Contains(part),
        _ => true,
    };

    /// <summary>PHONEY011: constant arguments the runtime rejects (<c>NullProbability(1.5)</c>, <c>Generate(-1)</c>…).</summary>
    /// <param name="context">The analysis context.</param>
    /// <param name="call">The call whose arguments are checked.</param>
    /// <param name="name">The generator method the call is or stands for (<c>WithNormalDistribution</c> for <c>m.Normal</c> in a member view).</param>
    private static void CheckArguments(OperationAnalysisContext context, IInvocationOperation call, string name)
    {
        switch (name)
        {
            case "NullProbability":
                Check(context, call, "probability", v => v is >= 0 and <= 1, "between 0 and 1");
                break;
            case "CollectionSize":
                Check(context, call, "min", v => v >= 0, AtLeastZero);
                if (Constant(call, "min") is { } min)
                    Check(context, call, "max", v => v >= min, $"at least {min.ToString(CultureInfo.InvariantCulture)} (the minimum)");
                break;
            case "MaxDepth":
                Check(context, call, "depth", v => v >= 0, AtLeastZero);
                break;
            case "Generate" or "GenerateParallel" or "Many":
                Check(context, call, "count", v => v >= 0, AtLeastZero);
                Check(context, call, "min", v => v >= 0, AtLeastZero);
                if (Constant(call, "min") is { } low)
                    Check(context, call, "max", v => v >= low, $"at least {low.ToString(CultureInfo.InvariantCulture)} (the minimum)");
                break;
            case "Check" or "CheckAsync":
                Check(context, call, "count", v => v >= 0, AtLeastZero);
                Check(context, call, "maxShrinks", v => v >= 0, AtLeastZero);
                break;
            case RuleCatalog.Unique or "Where":
                Check(context, call, "maxAttempts", v => v >= 1, "at least 1");
                break;
            case "WithNormalDistribution": // NumberModule.ValidateNormal
                Check(context, call, "mean", IsFinite, "to be finite");
                Check(context, call, "standardDeviation", v => IsFinite(v) && v >= 0, "to be finite and not negative");
                Check(context, call, "min", v => !double.IsNaN(v), "to be a number");
                Check(context, call, "max", v => !double.IsNaN(v), "to be a number");
                if (Constant(call, "min") is { } lowest && !double.IsNaN(lowest))
                    Check(context, call, "max", v => double.IsNaN(v) || v >= lowest, $"at least {lowest.ToString(CultureInfo.InvariantCulture)} (the minimum)");
                Check(context, call, "decimals", v => v is >= 0 and <= 15, "between 0 and 15");
                break;
        }
    }

    /// <summary><c>double.IsFinite</c>, which netstandard2.0 lacks.</summary>
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void Check(OperationAnalysisContext context, IInvocationOperation call, string parameter, Func<double, bool> valid, string expected)
    {
        if (Argument(call, parameter) is not { } argument || Constant(call, parameter) is not { } value || valid(value))
            return;
        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.ArgumentOutOfRange,
            argument.Syntax.GetLocation(),
            $"'{call.TargetMethod.Name}' needs {parameter} {expected}, but it is {value.ToString(CultureInfo.InvariantCulture)}; the call fails when it runs."));
    }

    /// <summary>The argument written in the source for <paramref name="parameter"/> (not a default value).</summary>
    private static IArgumentOperation? Argument(IInvocationOperation call, string parameter) =>
        call.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter && a.ArgumentKind != ArgumentKind.DefaultValue);

    /// <summary>The numeric constant passed for <paramref name="parameter"/> (also to a nullable parameter, <c>min: 5</c>), or <see langword="null"/>.</summary>
    private static double? Constant(IInvocationOperation call, string parameter)
    {
        var value = call.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter)?.Value;
        // A constant converted to a nullable type is not a constant itself; its operand is.
        while (value is IConversionOperation { ConstantValue.HasValue: false } conversion)
            value = conversion.Operand;
        return value?.ConstantValue is { HasValue: true, Value: IConvertible number } && number is not (string or bool or char)
            ? number.ToDouble(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>PHONEY005: a configuration method's new generator is dropped, so the call changes nothing.</summary>
    private static void CheckDiscarded(OperationAnalysisContext context, IInvocationOperation call, Symbols symbols)
    {
        if (call.Parent is IExpressionStatementOperation && AnalyzerSupport.IsDefinition(call.TargetMethod.ReturnType as INamedTypeSymbol, symbols.Generator))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DiscardedResult,
                AnalyzerSupport.NameLocation(call),
                $"The result of '{call.TargetMethod.Name}' is discarded. Generators are immutable: every configuration method returns a new generator, so this call has no effect. Use the returned generator."));
        }
    }

    /// <summary>PHONEY005: <c>Populate</c> on a struct returns the populated copy; the original stays as it was.</summary>
    private static void CheckPopulate(OperationAnalysisContext context, IInvocationOperation call, bool onFake)
    {
        if (call.Parent is not IExpressionStatementOperation)
            return;
        var method = call.TargetMethod;
        var type = onFake ? method.TypeArguments.FirstOrDefault() : method.ContainingType.TypeArguments.FirstOrDefault();
        if (type is not { IsValueType: true })
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DiscardedResult,
            AnalyzerSupport.NameLocation(call),
            $"'Populate' returns a populated copy of the struct '{type.Name}' and leaves the original unchanged. Use the returned value."));
    }

    /// <summary>PHONEY006: the scope of <c>Fake.Seeded</c> lasts until it is disposed; dropping it seeds everything after it.</summary>
    private static void CheckSeeded(OperationAnalysisContext context, IInvocationOperation call)
    {
        var dropped = call.Parent switch
        {
            IExpressionStatementOperation => true,
            ISimpleAssignmentOperation { Target: IDiscardOperation } => true,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => IsUnusedPlainLocal(declarator),
            _ => false,
        };
        if (dropped)
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SeededNotDisposed, AnalyzerSupport.NameLocation(call)));
    }

    /// <summary>A local declared without <c>using</c> that nothing refers to afterwards, so it can't be disposed.</summary>
    private static bool IsUnusedPlainLocal(IVariableDeclaratorOperation declarator)
    {
        var group = declarator.Parent?.Parent as IVariableDeclarationGroupOperation;
        if (group?.Parent is IUsingDeclarationOperation or IUsingOperation)
            return false;

        var root = (IOperation)declarator;
        while (root.Parent is { } parent)
            root = parent;
        return !root.DescendantsAndSelf().OfType<ILocalReferenceOperation>()
            .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, declarator.Symbol));
    }

    private static void CheckGenerator(OperationAnalysisContext context, IInvocationOperation call, Symbols symbols)
    {
        CheckDiscarded(context, call, symbols);
        if (RuleCatalog.IsTypeRule(call.TargetMethod.Name))
        {
            CheckTypeRuleReplaced(context, call, symbols);
            return;
        }

        if (RuleGroup.Of(call) is not { Selector.Kind: not SelectorKind.Unknown } group || !CheckSelector(context, call, group, symbols))
            return;
        var rules = group.Rules ?? [];
        for (var i = 0; i < rules.Count; i++)
        {
            CheckRule(context, call, group.Selector, rules[i], symbols);
            if (group.Selector.Kind == SelectorKind.Member && RuleCatalog.OccupiesSlot(rules[i].Name))
                CheckReplaced(context, call, group.Selector, rules, i, symbols);
        }
    }

    /// <summary>A call inside a member view (<c>m =&gt; m.OneOf(…).Unique(0)</c>): a dropped result (PHONEY005) and constant arguments its generator method rejects (PHONEY011).</summary>
    private static void CheckViewCall(OperationAnalysisContext context, IInvocationOperation call)
    {
        if (!RuleCatalog.ViewRules.TryGetValue(call.TargetMethod.Name, out var name))
            return;
        if (call.Parent is IExpressionStatementOperation)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DiscardedResult,
                AnalyzerSupport.NameLocation(call),
                $"The result of '{call.TargetMethod.Name}' is discarded. Member rules are immutable: every rule returns new rules, so this call has no effect. Return the result from the lambda: m => m.{call.TargetMethod.Name}(…)."));
        }

        CheckArguments(context, call, name);
    }

    /// <summary>
    /// PHONEY007 for what is wrong with the selected member whatever the rule: not a member path, or not a member that is generated. <see langword="false"/> when
    /// there is such a problem (reported once, also for a view with several rules), so the rules aren't checked against it.
    /// </summary>
    private static bool CheckSelector(OperationAnalysisContext context, IInvocationOperation call, RuleGroup group, Symbols symbols)
    {
        var selector = group.Selector;
        if (selector.Kind == SelectorKind.NotDirect)
        {
            Report(context, selector.Location, "The rule must select a member of the generated type (x => x.Name), of a nested object (x => x.Address.City) or of each element of a collection (x => x.Lines.Each().Sku). A member of a string, number or date has no members to give a rule to.");
            return false;
        }

        if (selector.Kind == SelectorKind.Combination)
        {
            if (group.Rules is { } rules && rules.All(r => r.Name == RuleCatalog.Unique))
                CheckCombinationParts(context, call, selector, symbols);
            return true;
        }

        if (selector.IsNested || GeneratedMembers(call, symbols) is not var (type, members) || members.Exists(m => m.Name == selector.Name))
            return true;
        Report(context, selector.Location, $"'{type.Name}.{selector.Name}' is not a public settable member or constructor parameter, so it can't be generated and the generator throws when it is first used.");
        return false;
    }

    /// <summary>PHONEY007: what <c>ObjectGenerator&lt;T&gt;.Compile</c> and <c>MemberName</c> reject for this rule on the selected member.</summary>
    private static void CheckRule(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, RuleCall rule, Symbols symbols)
    {
        var location = Where(selector, rule);
        if (selector.Kind == SelectorKind.Combination)
        {
            if (rule.Name != RuleCatalog.Unique)
                Report(context, location, $"{rule.Written} must select one member (x => x.Name); only Unique accepts several members as an anonymous type (x => new {{ x.UserId, x.RoleId }}).");
            return;
        }

        if (selector.IsNested)
        {
            if (NestedRuleProblem(rule, selector) is { } problem)
                Report(context, location, problem);
            return;
        }

        if (!rule.IsDependent || GeneratedMembers(call, symbols) is not var (type, members) || members.Find(m => m.Name == selector.Name) is not { } member)
            return;
        if (member.IsConstructorParameter)
            Report(context, location, $"'{type.Name}.{selector.Name}' is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
        else if (member.FillsExisting)
            Report(context, location, $"'{type.Name}.{selector.Name}' is a read-only collection that is filled in place, so it can't be set from the object. Use a rule without the object parameter.");
    }

    /// <summary>Where a rule's diagnostic goes: the selector of a flat rule, the method name of a rule in a view (the selector is shared by all of them).</summary>
    private static Location Where(RuleSelector selector, RuleCall rule) => rule.InView ? AnalyzerSupport.NameLocation(rule.Call) : selector.Location;

    /// <summary>PHONEY007 for <c>Unique(x =&gt; new { … })</c>: every part must be a member that is generated.</summary>
    private static void CheckCombinationParts(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, Symbols symbols)
    {
        if (GeneratedMembers(call, symbols) is not var (owner, described))
            return;
        foreach (var part in selector.Path!.Split(',').Where(p => !described.Exists(m => m.Name == p)))
        {
            Report(context, selector.Location, $"'{owner.Name}.{part}' is not a public settable member or constructor parameter, so it can't be part of a unique combination and the generator throws when it is first used.");
        }
    }

    /// <summary>Why a rule can't be given to a member of a nested object, or <see langword="null"/> when it can.</summary>
    private static string? NestedRuleProblem(RuleCall rule, RuleSelector selector) => rule.Name switch
    {
        RuleCatalog.Unique => $"Unique must select a member of the generated type directly, like x => x.Name: uniqueness is per member of the generated type, not for '{selector.Path}' in a nested object.",
        "Sequence" or "Cycle" => $"{rule.Name} must select a member of the generated type directly, like x => x.Name: it numbers the generated objects, so it can't give a value to '{selector.Path}' in a nested object.",
        "Where" => $"Where must select a member of the generated type directly, like x => x.Name: a condition on a member applies to the generated object's own members, not to '{selector.Path}' in a nested object.",
        _ when rule.IsDependent => $"'{selector.Path}' is a member of a nested object, so its rule can't depend on the object. Use a rule without the object parameter.",
        _ => null,
    };

    /// <summary>The generated type of the rule's generator and its members as the model describes them, or <see langword="null"/> when they can't be known (a custom model).</summary>
    private static (INamedTypeSymbol Type, List<MemberModel> Members)? GeneratedMembers(IInvocationOperation call, Symbols symbols) =>
        call.TargetMethod.ContainingType.TypeArguments.FirstOrDefault() is INamedTypeSymbol type && FluentChain.Start(call, symbols.Generator) is not IObjectCreationOperation && symbols.Describe(type) is { } members
            ? (type, members)
            : null;

    private static void Report(OperationAnalysisContext context, Location location, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleNotGenerated, location, message));

    /// <summary>PHONEY008: an earlier rule in the same chain (or the same view) set the same member, and this one replaces it.</summary>
    private static void CheckReplaced(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, IReadOnlyList<RuleCall> rules, int index, Symbols symbols)
    {
        // Nearest first: the rules before it in its own view, then those of the earlier calls of the chain for the same member.
        var replaced = Enumerable.Range(0, index).Reverse().Select(i => rules[i])
            .Concat(FluentChain.Earlier(call, symbols.Generator).SelectMany(earlier =>
                RuleGroup.Of(earlier) is { Selector: { Kind: SelectorKind.Member } previous, Rules: { } before } && previous.Path == selector.Path ? before.Reverse() : []))
            .FirstOrDefault(r => RuleCatalog.OccupiesSlot(r.Name));
        if (replaced.Call is null)
            return;
        // The replaced call goes along so the code fix can remove it.
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleReplaced, Where(selector, rules[index]), additionalLocations: new[] { replaced.Call.Syntax.GetLocation() }, selector.Path));
    }

    /// <summary>
    /// PHONEY008 for type registrations: <c>WithGeneratorFor</c> or <c>Subtype</c> for a type that an earlier call of the same method in the same chain
    /// registered replaces it. (<c>WithGeneratorFor</c> and <c>Subtype</c> for one type are not the same thing: the generator wins over the subtype.)
    /// </summary>
    private static void CheckTypeRuleReplaced(OperationAnalysisContext context, IInvocationOperation call, Symbols symbols)
    {
        // WithGeneratorFor<TNested>, WithGeneratorFor<TBase, TGenerated>, Subtype<TBase, T1…> and Subtype<TBase>(types) all register TypeArguments[0].
        if (call.TargetMethod.TypeArguments.FirstOrDefault() is not { } type)
            return;
        var method = call.TargetMethod.Name;
        if (FluentChain.Earlier(call, symbols.Generator).FirstOrDefault(earlier =>
                earlier.TargetMethod.Name == method && earlier.TargetMethod.TypeArguments.FirstOrDefault() is { } previous && SymbolEqualityComparer.Default.Equals(previous, type)) is { } replaced)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleReplaced, AnalyzerSupport.NameLocation(call), additionalLocations: new[] { replaced.Syntax.GetLocation() }, type.Name));
        }
    }

    /// <summary>The symbols of one compilation, with the members of each generated type described once.</summary>
    private sealed class Symbols(Compilation compilation, INamedTypeSymbol generator, INamedTypeSymbol fake, INamedTypeSymbol? fakeFor)
    {
        private readonly ConcurrentDictionary<INamedTypeSymbol, List<MemberModel>?> _members = new(SymbolEqualityComparer.Default);

        public INamedTypeSymbol Generator { get; } = generator;

        public INamedTypeSymbol Fake { get; } = fake;

        public INamedTypeSymbol? FakeFor { get; } = fakeFor;

        /// <summary>The members Phoney generates for <paramref name="type"/>, or <see langword="null"/> when it can't be generated.</summary>
        public List<MemberModel>? Describe(INamedTypeSymbol type) =>
            _members.GetOrAdd(type, t => ModelBuilder.DescribeMembers(compilation, t));
    }
}
