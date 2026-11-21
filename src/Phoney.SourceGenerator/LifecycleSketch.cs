using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>A problem a lifecycle written in the source has: where to report it, what it is, and whether it is about a stamp (PHONEY014) or the lifecycle (PHONEY013).</summary>
internal readonly record struct LifecycleProblem(Location Location, string Message, bool IsStamp);

/// <summary>A <c>Stamp(state, x =&gt; x.PaidAt)</c> as written: its state (when it is a constant), the member it selects and that member's type.</summary>
internal sealed record SketchStamp(int State, bool StateKnown, RuleSelector Selector, ITypeSymbol? Type, Location Location)
{
    /// <summary>The name of the stamped member (the last one of the path), or <see langword="null"/> when the selector can't be read.</summary>
    public string? MemberName => Selector is { Kind: SelectorKind.Member, Path: { } path } ? path.Substring(path.LastIndexOf('.') + 1) : null;

    /// <summary>Whether the stamp's member is a member of the generated type itself.</summary>
    public bool IsRoot => Selector is { Kind: SelectorKind.Member, IsNested: false };
}

/// <summary>
/// A lifecycle as the source writes it, <c>l =&gt; l.From(Placed).To(Paid).Stamp(Paid, o =&gt; o.PaidAt)</c>, replayed call by call like
/// <c>LifecycleBuilder&lt;T, TState&gt;</c> collects it and checked like <c>LifecycleDefinition.Of</c> checks it, so the analyzers report what adding the rule
/// would throw. Only constant arguments are read; anything else makes the sketch incomplete, and what depends on it is not checked.
/// </summary>
internal sealed class LifecycleSketch
{
    /// <summary>The name of the state parameter of the builder's methods.</summary>
    private const string StateParameter = "state";

    private readonly List<object?> _states = [];
    private readonly List<(int From, int To)> _moves = [];
    private readonly List<(int State, double Weight)> _stays = [];
    private readonly List<int> _ends = [];
    private readonly List<LifecycleProblem> _problems = [];
    private readonly List<SketchStamp> _stamps = [];
    private readonly ITypeSymbol _stateType;
    private readonly string _member;
    private int _from = -1;
    private bool _hasFrom;
    private int _start = -1;
    private bool _hasStart;
    private int _max = 16;
    private int _transitions;
    private bool _unknownShape;
    private readonly List<(object? Value, bool Known, RuleSelector Selector, ITypeSymbol? Type)> _written = [];

    private LifecycleSketch(ITypeSymbol stateType, string member, RuleSelector status)
    {
        _stateType = stateType;
        _member = member;
        Status = status;
    }

    /// <summary>The status member as the lifecycle rule selects it.</summary>
    public RuleSelector Status { get; }

    /// <summary>The problems found, in the order the runtime would meet them (as far as it reads them).</summary>
    public IReadOnlyList<LifecycleProblem> Problems => _problems;

    /// <summary>The stamps, in the order they are written.</summary>
    public IReadOnlyList<SketchStamp> Stamps => _stamps;

    /// <summary>
    /// The shape of the lifecycle, or <see langword="null"/> when a state, the start or <c>MaxTransitions</c> isn't a constant, or a builder call is refused,
    /// so where walks go can't be known.
    /// </summary>
    public LifecycleGraph? Graph { get; private set; }

    /// <summary>Whether every state is a constant, so the states are all known.</summary>
    public bool StatesKnown { get; private set; } = true;

    /// <summary>
    /// Reads the lifecycle of <paramref name="call"/>, a <c>Lifecycle(member, l =&gt; …)</c> of a generator or <c>m.Lifecycle(l =&gt; …)</c> of a member view, or
    /// returns <see langword="null"/> when its lambda is not a chain of builder calls (<c>l =&gt; Shop(l)</c>).
    /// </summary>
    public static LifecycleSketch? Read(IInvocationOperation call)
    {
        var argument = call.Arguments.FirstOrDefault(a => a.Parameter?.Name == "lifecycle");
        if (argument is null || RuleSelector.Lambda(argument.Value) is not { } lambda || lambda.Symbol.Parameters.FirstOrDefault() is not { } parameter
            || parameter.Type is not INamedTypeSymbol { TypeArguments.Length: 2 } builder)
        {
            return null;
        }

        if (lambda.Body.Operations.Length != 1 || lambda.Body.Operations[0] is not IReturnOperation { ReturnedValue: { } current })
            return null;
        var calls = new List<IInvocationOperation>();
        while (current is IInvocationOperation step && SymbolEqualityComparer.Default.Equals(step.TargetMethod.ContainingType, builder))
        {
            calls.Add(step);
            current = step.Instance!;
        }

        if (!(current is IParameterReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter)))
            return null;

        var status = StatusOf(call);
        var owner = builder.TypeArguments[0].Name;
        var sketch = new LifecycleSketch(builder.TypeArguments[1], status.Path is { } path ? $"{owner}.{path}" : owner, status);
        calls.Reverse();
        foreach (var step in calls)
            sketch.Replay(step);
        sketch.Finish(AnalyzerSupport.NameLocation(call));
        return sketch;
    }

    /// <summary>The state a date member is named after (<c>PaidAt</c>, <c>PaidOn</c>, <c>PaidDate</c>, any case), like <c>LifecycleDefinition.StateNamed</c>, or -1.</summary>
    public int StateNamed(string member)
    {
        for (var s = 0; s < _states.Count; s++)
        {
            var name = StateText(s);
            if (member.Length > name.Length && member.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && member.Substring(name.Length) is var suffix
                && (suffix.Equals("At", StringComparison.OrdinalIgnoreCase) || suffix.Equals("On", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Date", StringComparison.OrdinalIgnoreCase)))
                return s;
        }

        return -1;
    }

    /// <summary>The state with number <paramref name="state"/> as the runtime writes it: an enum value by its name.</summary>
    public string StateText(int state) => Text(_states[state]);

    /// <summary>Whether a member of <paramref name="type"/> can hold a stamp, like <c>LifecycleStamps.CanHold</c>: a date, nullable or not, or a moment of an add-on.</summary>
    public static bool CanHold(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        return ConventionRules.ConventionCategoryOf(name, type.TypeKind == TypeKind.Enum) is TypeCategory.DateTimeOffset or TypeCategory.DateTime or TypeCategory.DateOnly;
    }

    /// <summary>Whether a member of <paramref name="type"/> can't be <see langword="null"/>: a struct that isn't <see cref="Nullable{T}"/>.</summary>
    public static bool IsNonNullableValue(ITypeSymbol type) =>
        type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;

    /// <summary>The status member: the selector of a generator's <c>Lifecycle</c>, or of the <c>Member</c> call around a view's.</summary>
    private static RuleSelector StatusOf(IInvocationOperation call)
    {
        if (!RuleGroup.IsView(call.TargetMethod))
            return RuleSelector.Read(call);
        for (var parent = call.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IInvocationOperation { TargetMethod.Name: RuleCatalog.Member } member && !RuleGroup.IsView(member.TargetMethod))
                return RuleSelector.Read(member);
        }

        return new RuleSelector(SelectorKind.Unknown, null, call.Syntax.GetLocation());
    }

    private void Replay(IInvocationOperation step)
    {
        var at = AnalyzerSupport.NameLocation(step);
        switch (step.TargetMethod.Name)
        {
            case "From":
                _from = State(Argument(step, StateParameter));
                _hasFrom = true;
                break;
            case "To":
                ReplayTo(step, at);
                break;
            case "Stays":
                ReplayStays(step, at);
                break;
            case "Start":
                _start = State(Argument(step, StateParameter));
                _hasStart = true;
                break;
            case "EndsIn":
                ReplayEndsIn(step, at);
                break;
            case "MaxTransitions":
                if (Number(Argument(step, "count")) is not { } count)
                    _unknownShape = true; // decided at runtime
                else if (count < 1)
                    Refuse(at, $"MaxTransitions({Show(count)}) is refused: an object takes at least one transition. Give 1 or more.");
                else
                    _max = (int)count;
                break;
            case "Stamp":
                ReplayStamp(step);
                break;
        }
    }

    private void ReplayTo(IInvocationOperation step, Location at)
    {
        _transitions++;
        var to = State(Argument(step, StateParameter));
        if (!RequireFrom(at, "To"))
            return;
        if (Number(Argument(step, "weight")) is { } weight && !(weight > 0 && !double.IsInfinity(weight)))
            Refuse(at, $"The weight {Show(weight)} of the transition from {Text(_from)} to {Text(to)} is refused: a weight is greater than zero and finite.");
        if (Gap(Argument(step, "after")) is { } after)
        {
            if (after.Min < 0)
                Refuse(at, "after starts below zero: a transition can't happen before the state it leaves was entered. Give after a minimum of zero or more.");
            else if (after.Max < after.Min)
                Refuse(at, "after ends before it starts: its longest gap is shorter than its shortest. Give after as (shortest, longest).");
        }

        if (_from >= 0 && to >= 0)
        {
            if (_moves.Contains((_from, to)))
                Refuse(at, $"The transition from {Text(_from)} to {Text(to)} is added twice. Add each transition once.");
            else
                _moves.Add((_from, to));
        }
    }

    private void ReplayStays(IInvocationOperation step, Location at)
    {
        if (!RequireFrom(at, "Stays"))
            return;
        var weight = Number(Argument(step, "weight"));
        if (weight is { } w && !(w >= 0 && !double.IsInfinity(w)))
            Refuse(at, $"The stay weight {Show(w)} is refused: a stay weight is zero or more and finite.");
        if (_from < 0)
            return;
        if (_stays.Exists(s => s.State == _from))
            Refuse(at, $"Stays is given twice for {Text(_from)}. Give it once.");
        else if (weight is { } known)
            _stays.Add((_from, known));
        else
            _unknownShape = true;
    }

    private void ReplayEndsIn(IInvocationOperation step, Location at)
    {
        var state = State(Argument(step, StateParameter));
        if (Number(Argument(step, "weight")) is { } weight && !(weight > 0 && !double.IsInfinity(weight)))
            Refuse(at, $"The weight {Show(weight)} of EndsIn({Text(state)}) is refused: a weight is greater than zero and finite.");
        if (state < 0)
            return;
        if (_ends.Contains(state))
            Refuse(at, $"EndsIn({Text(state)}) is given twice. Give each end state once.");
        else
            _ends.Add(state);
    }

    private void ReplayStamp(IInvocationOperation step)
    {
        var constant = Argument(step, StateParameter)?.Value.ConstantValue;
        var member = Argument(step, "member");
        var selector = RuleSelector.Read(member, step.Syntax.GetLocation());
        ITypeSymbol? type = null;
        if (member is not null && RuleSelector.Lambda(member.Value) is { Body.Operations.Length: 1 } lambda && lambda.Body.Operations[0] is IReturnOperation { ReturnedValue: { } returned })
        {
            while (returned is IConversionOperation conversion)
                returned = conversion.Operand;
            type = returned switch
            {
                IPropertyReferenceOperation property => property.Property.Type,
                IFieldReferenceOperation field => field.Field.Type,
                _ => null,
            };
        }

        _written.Add((constant is { HasValue: true } c ? c.Value : null, constant is { HasValue: true }, selector, type));
    }

    /// <summary>The checks of the whole lifecycle, which the runtime makes once the lambda has returned; reported at <paramref name="at"/>, the <c>Lifecycle</c> call.</summary>
    private void Finish(Location at)
    {
        var refused = _problems.Count > 0;
        if (!refused && _transitions == 0)
        {
            _problems.Add(new LifecycleProblem(at, $"The lifecycle of {_member} has no transitions. Add them with .From(state).To(next), e.g. l => l.From(Placed).To(Paid).", false));
            refused = true;
        }

        if (Undefined() is { } undefined)
        {
            _problems.Add(new LifecycleProblem(at, $"The lifecycle of {_member} uses {Text(_states[undefined])}, which is not a value of {_stateType.Name}. Use one of its defined values.", false));
            refused = true;
        }

        foreach (var (value, known, selector, type) in _written)
            _stamps.Add(new SketchStamp(known ? _states.FindIndex(s => Equals(s, value)) : -1, known, selector, type, selector.Location));
        CheckStamps();
        if (refused || _unknownShape || _transitions != _moves.Count)
            return;
        var graph = Build();
        var start = Text(graph.Start);
        if (graph.FirstUnreachable() is var unreachable and >= 0)
            _problems.Add(new LifecycleProblem(at, $"The lifecycle of {_member} can't reach {Text(unreachable)} from its start state {start}. Add a transition into it, or remove it.", false));
        else if (graph.FirstEndTooFar() is var far and >= 0)
            _problems.Add(new LifecycleProblem(at, $"The lifecycle of {_member} can't end in {Text(far)}: it takes {Show(graph.Distance(graph.Start, far))} transitions from {start}, and MaxTransitions is {Show(graph.Max)}. Raise MaxTransitions, or remove the EndsIn.", false));
        else if (graph.IdleStay(_stays.Select(s => s.State), _member, Text) is { } idle)
            _problems.Add(new LifecycleProblem(at, idle, false));
        if (!_problems.Exists(p => !p.IsStamp))
            Graph = graph;
    }

    /// <summary>The graph of a lifecycle whose states, transitions, stays, start and limit are all constants.</summary>
    private LifecycleGraph Build()
    {
        var stays = Enumerable.Repeat(1.0, _states.Count).ToArray();
        foreach (var (state, weight) in _stays)
            stays[state] = weight;
        var start = _hasStart ? _start : _moves[0].From;
        return new LifecycleGraph(start, _states.Count, _moves, stays, _ends, _max);
    }

    /// <summary>The checks <c>CheckStamps</c> and <c>StampMember</c> make when the rule is added: the member, its type, its state, and stamping it once.</summary>
    private void CheckStamps()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var statusPath = Status is { Kind: SelectorKind.Member, Path: { } p } ? p.Split('.') : null;
        foreach (var stamp in _stamps)
        {
            if (stamp.MemberName is { } name && Refusal(stamp, name, statusPath, seen) is { } message)
                Stamp(stamp, message);
        }
    }

    /// <summary>Why the runtime refuses <paramref name="stamp"/>, or <see langword="null"/>; <paramref name="seen"/> collects the members stamped so far.</summary>
    private string? Refusal(SketchStamp stamp, string name, string[]? statusPath, HashSet<string> seen)
    {
        var path = stamp.Selector.Path!.Split('.');
        if (statusPath is not null && (path.Length != statusPath.Length || !path.Take(path.Length - 1).SequenceEqual(statusPath.Take(statusPath.Length - 1))))
        {
            var holder = statusPath.Length == 1 ? _member.Substring(0, _member.IndexOf('.')) : string.Join(".", statusPath.Take(statusPath.Length - 1));
            return $"The lifecycle of {_member} stamps {stamp.Selector.Path}, which is not a member of the object that holds the status ({holder}). Select a member next to it.";
        }

        if (stamp.Type is { } type && !CanHold(type))
            return $"The lifecycle of {_member} stamps {name}, a {type.Name}: a stamp is a DateTimeOffset, DateTime or DateOnly (nullable unless every object reaches the state).";
        if (stamp.StateKnown && StatesKnown && stamp.State < 0)
            return $"The lifecycle of {_member} stamps {name} with a state that is not one of its states. Stamp a state it has.";
        return seen.Add(name) ? null : $"The lifecycle of {_member} stamps {name} twice. Give each member one state.";
    }

    private void Stamp(SketchStamp stamp, string message) => _problems.Add(new LifecycleProblem(stamp.Location, message, true));

    private void Refuse(Location at, string message) => _problems.Add(new LifecycleProblem(at, message, false));

    private bool RequireFrom(Location at, string method)
    {
        if (_hasFrom)
            return true;
        Refuse(at, $"{method} needs a state to leave from: start with .From(state), e.g. l => l.From(Placed).To(Paid).");
        return false;
    }

    /// <summary>The number of a constant state, added when it is new, or -1 when the state is decided at runtime.</summary>
    private int State(IArgumentOperation? argument)
    {
        if (argument?.Value.ConstantValue is not { HasValue: true } constant)
        {
            StatesKnown = false;
            _unknownShape = true;
            return -1;
        }

        var index = _states.FindIndex(s => Equals(s, constant.Value));
        if (index >= 0)
            return index;
        _states.Add(constant.Value);
        return _states.Count - 1;
    }

    /// <summary>The first state that is not a defined value of its enum, or <see langword="null"/>.</summary>
    private int? Undefined()
    {
        var type = _stateType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : _stateType;
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            return null;
        var defined = enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).Select(f => f.ConstantValue).ToList();
        var index = _states.FindIndex(s => s is not null && !defined.Exists(d => Equals(d, s)));
        return index < 0 ? null : index;
    }

    private string Text(int state) => state < 0 ? "…" : Text(_states[state]);

    private string Text(object? value)
    {
        var type = _stateType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : _stateType;
        if (value is not null && type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            && enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value)) is { } field)
        {
            return field.Name;
        }

        return value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
    }

    private static IArgumentOperation? Argument(IInvocationOperation call, string name) =>
        call.Arguments.FirstOrDefault(a => a.Parameter?.Name == name && a.ArgumentKind != ArgumentKind.DefaultValue);

    private static double? Number(IArgumentOperation? argument) =>
        argument?.Value.ConstantValue is { HasValue: true, Value: { } value } && value is not string ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : null;

    private static string Show(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The <c>after:</c> range in ticks when both ends are written as <c>TimeSpan.Zero</c> or <c>TimeSpan.FromX(constant)</c>, else <see langword="null"/>.</summary>
    private static (double Min, double Max)? Gap(IArgumentOperation? argument)
    {
        var value = argument?.Value;
        while (value is IConversionOperation conversion)
            value = conversion.Operand;
        if (value is not ITupleOperation { Elements.Length: 2 } tuple)
            return null;
        return Ticks(tuple.Elements[0]) is { } min && Ticks(tuple.Elements[1]) is { } max ? (min, max) : null;
    }

    private static double? Ticks(IOperation value)
    {
        while (value is IConversionOperation conversion)
            value = conversion.Operand;
        switch (value)
        {
            case IFieldReferenceOperation { Field: { Name: "Zero", IsStatic: true, ContainingType.Name: "TimeSpan" } }:
                return 0;
            case IInvocationOperation { TargetMethod: { IsStatic: true, ContainingType.Name: "TimeSpan" } method, Arguments.Length: 1 } call
                when Number(call.Arguments[0]) is { } amount:
                return method.Name switch
                {
                    "FromDays" => amount * TimeSpan.TicksPerDay,
                    "FromHours" => amount * TimeSpan.TicksPerHour,
                    "FromMinutes" => amount * TimeSpan.TicksPerMinute,
                    "FromSeconds" => amount * TimeSpan.TicksPerSecond,
                    "FromMilliseconds" => amount * TimeSpan.TicksPerMillisecond,
                    "FromTicks" => amount,
                    _ => null,
                };
            default:
                return null;
        }
    }
}
