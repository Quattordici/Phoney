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
    /// <summary>The calls that register something for a type (<c>TypeArguments[0]</c>): a later one for the same type replaces an earlier one of the same kind.</summary>
    private static readonly HashSet<string> TypeRules = new(StringComparer.Ordinal) { "WithGeneratorFor", "Subtype" };

    private static readonly HashSet<string> RuleMethods = new(StringComparer.Ordinal) { "With", "WithNormalDistribution", "WithOneOf", "WithSubsetOf", "Ignore", "Unique", "Invalid", "Boundary", "Sequence", "Cycle" };

    /// <summary>The rules that occupy a member's one rule slot; <c>Unique</c> is kept separately and combines with them.</summary>
    private static readonly HashSet<string> SlotRules = new(StringComparer.Ordinal) { "With", "WithNormalDistribution", "WithOneOf", "WithSubsetOf", "Ignore", "Invalid", "Boundary", "Sequence", "Cycle" };

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

        CheckArguments(context, call);
        // A keyed call gives every key its own object, so a constant seed in a loop is the point there (it keeps the mapping stable).
        if (onGenerator && AnalyzerSupport.Terminals.Contains(method.Name) && method.Name is not ("GenerateFor" or "Pseudonymize"))
            CheckSeedInLoop(context, call, symbols);
    }

    /// <summary>PHONEY010: every iteration builds a new generator from the same seed, so every iteration gets the same objects.</summary>
    private static void CheckSeedInLoop(OperationAnalysisContext context, IInvocationOperation terminal, Symbols symbols)
    {
        // The last Seed in the chain wins; a seed that isn't a constant may vary per iteration.
        IInvocationOperation? seed = null;
        var receiver = terminal.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, symbols.Generator))
        {
            if (seed is null && earlier.TargetMethod.Name == "Seed")
                seed = earlier;
            receiver = earlier.Instance;
        }

        if (seed is null || seed.Arguments.FirstOrDefault()?.Value.ConstantValue is not { HasValue: true } || !AnalyzerSupport.IsStart(receiver, symbols.Fake, symbols.FakeFor))
            return;

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
    private static void CheckArguments(OperationAnalysisContext context, IInvocationOperation call)
    {
        switch (call.TargetMethod.Name)
        {
            case "NullProbability":
                Check(context, call, "probability", v => v is >= 0 and <= 1, "between 0 and 1");
                break;
            case "CollectionSize":
                Check(context, call, "min", v => v >= 0, "at least 0");
                if (Constant(call, "min") is { } min)
                    Check(context, call, "max", v => v >= min, $"at least {min.ToString(CultureInfo.InvariantCulture)} (the minimum)");
                break;
            case "MaxDepth":
                Check(context, call, "depth", v => v >= 0, "at least 0");
                break;
            case "Generate" or "GenerateParallel" or "Many":
                Check(context, call, "count", v => v >= 0, "at least 0");
                Check(context, call, "min", v => v >= 0, "at least 0");
                if (Constant(call, "min") is { } low)
                    Check(context, call, "max", v => v >= low, $"at least {low.ToString(CultureInfo.InvariantCulture)} (the minimum)");
                break;
            case "Unique" or "Where":
                Check(context, call, "maxAttempts", v => v >= 1, "at least 1");
                break;
        }
    }

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

    /// <summary>The numeric constant passed for <paramref name="parameter"/>, or <see langword="null"/>.</summary>
    private static double? Constant(IInvocationOperation call, string parameter) =>
        call.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter)?.Value.ConstantValue is { HasValue: true, Value: IConvertible value } && value is not (string or bool or char)
            ? value.ToDouble(CultureInfo.InvariantCulture)
            : null;

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
        var name = call.TargetMethod.Name;
        if (TypeRules.Contains(name))
        {
            CheckTypeRuleReplaced(context, call, symbols);
            return;
        }

        if (!RuleMethods.Contains(name) && !IsMemberCondition(call.TargetMethod))
            return;

        var selector = RuleSelector.Read(call);
        if (selector.Kind == SelectorKind.Unknown)
            return;
        CheckRule(context, call, selector, symbols);
        if (selector.Kind == SelectorKind.Member && SlotRules.Contains(name))
            CheckReplaced(context, call, selector, symbols);
    }

    /// <summary>PHONEY007: what <c>ObjectGenerator&lt;T&gt;.Compile</c> and <c>MemberName</c> reject at runtime.</summary>
    private static void CheckRule(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, Symbols symbols)
    {
        var method = call.TargetMethod;
        if (selector.Kind == SelectorKind.Combination)
        {
            if (method.Name != "Unique")
                Report(context, selector, $"{method.Name} must select one member (x => x.Name); only Unique accepts several members as an anonymous type (x => new {{ x.UserId, x.RoleId }}).");
            return;
        }

        if (selector.Kind == SelectorKind.NotDirect)
        {
            Report(context, selector, "The rule must select a member of the generated type (x => x.Name), of a nested object (x => x.Address.City) or of each element of a collection (x => x.Lines.Each().Sku). A member of a string, number or date has no members to give a rule to.");
            return;
        }

        if (selector.IsNested)
        {
            if (method.Name == "Unique")
            {
                Report(context, selector, $"Unique must select a member of the generated type directly, like x => x.Name: uniqueness is per member of the generated type, not for '{selector.Path}' in a nested object.");
                return;
            }

            if (method.Name is "Sequence" or "Cycle")
            {
                Report(context, selector, $"{method.Name} must select a member of the generated type directly, like x => x.Name: it numbers the generated objects, so it can't give a value to '{selector.Path}' in a nested object.");
                return;
            }

            if (method.Name == "Where")
            {
                Report(context, selector, $"Where must select a member of the generated type directly, like x => x.Name: a condition on a member applies to the generated object's own members, not to '{selector.Path}' in a nested object.");
                return;
            }

            if (IsDependentRule(method))
            {
                Report(context, selector, $"'{selector.Path}' is a member of a nested object, so its rule can't depend on the object. Use a rule without the object parameter.");
                return;
            }
        }

        if (method.ContainingType.TypeArguments.FirstOrDefault() is not INamedTypeSymbol type || StartsFromCustomModel(call, symbols))
            return;
        if (symbols.Describe(type) is not { } members)
            return;

        var member = members.FirstOrDefault(m => m.Name == selector.Name);
        if (member is null)
        {
            Report(context, selector, $"'{type.Name}.{selector.Name}' is not a public settable member or constructor parameter, so it can't be generated and the generator throws when it is first used.");
        }
        else if (member.IsConstructorParameter && IsDependentRule(method))
        {
            Report(context, selector, $"'{type.Name}.{selector.Name}' is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
        }
        else if (member.FillsExisting && IsDependentRule(method))
        {
            Report(context, selector, $"'{type.Name}.{selector.Name}' is a read-only collection that is filled in place, so it can't be set from the object. Use a rule without the object parameter.");
        }
    }

    /// <summary><c>Where(x =&gt; x.Member, condition)</c>: a condition on one member, unlike <c>Where(x =&gt; …)</c> on the whole object.</summary>
    private static bool IsMemberCondition(IMethodSymbol method) =>
        method.Name == "Where" && method.Parameters.Length >= 2 && method.Parameters[0].Type is INamedTypeSymbol { Name: "Expression" };

    private static void Report(OperationAnalysisContext context, RuleSelector selector, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleNotGenerated, selector.Location, message));

    /// <summary><c>With(member, (faker, obj) =&gt; …)</c>: the rule reads the object after it is created.</summary>
    private static bool IsDependentRule(IMethodSymbol method) =>
        method.Name == "With" && method.Parameters.Length == 2 && method.Parameters[1].Type is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 3 };

    /// <summary>PHONEY008: an earlier call in the same chain set the same member, and this one replaces it.</summary>
    private static void CheckReplaced(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, Symbols symbols)
    {
        if (HasEarlierInChain(call, symbols, earlier =>
                SlotRules.Contains(earlier.TargetMethod.Name) && RuleSelector.Read(earlier) is { Kind: SelectorKind.Member } previous && previous.Path == selector.Path))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleReplaced, selector.Location, selector.Path));
        }
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
        if (HasEarlierInChain(call, symbols, earlier =>
                earlier.TargetMethod.Name == method && earlier.TargetMethod.TypeArguments.FirstOrDefault() is { } previous && SymbolEqualityComparer.Default.Equals(previous, type)))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleReplaced, AnalyzerSupport.NameLocation(call), type.Name));
        }
    }

    /// <summary>Whether a generator call earlier in the same fluent chain (not in a variable the chain started from) satisfies <paramref name="matches"/>.</summary>
    private static bool HasEarlierInChain(IInvocationOperation call, Symbols symbols, Func<IInvocationOperation, bool> matches)
    {
        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, symbols.Generator))
        {
            if (matches(earlier))
                return true;
            receiver = earlier.Instance;
        }

        return false;
    }

    /// <summary>Whether the chain starts with <c>new ObjectGenerator&lt;T&gt;(model)</c>, whose members are whatever the model says.</summary>
    private static bool StartsFromCustomModel(IInvocationOperation call, Symbols symbols)
    {
        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, symbols.Generator))
            receiver = earlier.Instance;
        return receiver is IObjectCreationOperation;
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
