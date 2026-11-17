using System.Globalization;
using System.Linq.Expressions;

namespace Phoney.Generation;

/// <summary>One transition of a checked lifecycle, by state number, with the range its gap is drawn from in ticks.</summary>
internal readonly record struct LifecycleMove(int From, int To, double Weight, long MinGap, long MaxGap);

/// <summary>
/// A checked lifecycle with its states numbered, ready to walk. A walk (<see cref="LifecycleWalk"/>) is the states an object went through, the start first,
/// with the gap before each transition; it is drawn once per object and kept on its scope (<see cref="FakeScope.LifecyclePath"/>), so the status and every stamp
/// see the same path.
/// </summary>
/// <remarks>
/// The draws, which the shrinker relies on: with end states, one <see cref="SeededRandom.WeightedIndex(ReadOnlySpan{double})"/> draw for the end state first.
/// Then, per state that isn't terminal, one such draw over [stay, then its transitions in the order they were written] (towards an end state: over the
/// transitions that can still reach it in time, never staying) and, when it moves, one <see cref="SeededRandom.Double()"/> draw for the gap. Staying comes first
/// and a gap of 0 is the shortest, so the simplest draws stay and the simplest object is in its start state; a transition's two draws lie together.
/// </remarks>
internal abstract class LifecycleDefinition
{
    /// <summary>The shortest time a transition takes when <c>after:</c> isn't given.</summary>
    internal static readonly TimeSpan DefaultMinGap = TimeSpan.FromHours(1);

    /// <summary>The longest time a transition takes when <c>after:</c> isn't given.</summary>
    internal static readonly TimeSpan DefaultMaxGap = TimeSpan.FromDays(7);

    /// <summary>The name of the status member, for histories: <c>Status</c>.</summary>
    public string Member { get; init; } = "";

    private const int Stay = -1;

    private readonly int _start;
    private readonly int[][] _targets;
    private readonly double[][] _totals;
    private readonly LifecycleMove[][] _targetMoves;
    private readonly LifecycleMove[][] _moves;
    private readonly int[]? _ends;
    private readonly double[]? _endTotals;
    private readonly LifecycleGraph _graph;
    private readonly int _max;

    protected LifecycleDefinition(int start, int stateCount, IReadOnlyList<LifecycleMove> transitions, double[] stays, IReadOnlyList<(int State, double Weight)> ends, int max)
    {
        _start = start;
        _max = max;
        _targets = new int[stateCount][];
        _totals = new double[stateCount][];
        _targetMoves = new LifecycleMove[stateCount][];
        _moves = new LifecycleMove[stateCount][];
        for (var s = 0; s < stateCount; s++)
        {
            var moves = transitions.Where(t => t.From == s).ToArray();
            _moves[s] = moves;
            var targets = new List<int>();
            var targetMoves = new List<LifecycleMove>();
            var totals = new List<double>();
            var total = 0.0;
            if (moves.Length > 0 && stays[s] > 0)
            {
                total += stays[s];
                targets.Add(Stay);
                targetMoves.Add(default);
                totals.Add(total);
            }

            foreach (var move in moves)
            {
                total += move.Weight;
                targets.Add(move.To);
                targetMoves.Add(move);
                totals.Add(total);
            }

            _targets[s] = [.. targets];
            _targetMoves[s] = [.. targetMoves];
            _totals[s] = [.. totals];
        }

        _graph = new LifecycleGraph(start, stateCount, [.. transitions.Select(t => (t.From, t.To))], stays, [.. ends.Select(e => e.State)], max);
        if (ends.Count > 0)
        {
            _ends = [.. ends.Select(e => e.State)];
            _endTotals = new double[ends.Count];
            var total = 0.0;
            for (var i = 0; i < ends.Count; i++)
            {
                total += ends[i].Weight;
                _endTotals[i] = total;
            }
        }
    }

    /// <summary>The number of states.</summary>
    public int StateCount => _targets.Length;

    /// <summary>The members tied to a state with <c>Stamp</c>, by member name, on the object that holds the lifecycle's member.</summary>
    public IReadOnlyList<(int State, string Member)> Stamps { get; protected init; } = [];

    /// <summary>The state objects start in.</summary>
    public int Start => _start;

    /// <summary>
    /// The ways an <c>Invalid</c> rule on the status breaks a stamp, one of which each walk takes (see <see cref="LifecycleCases"/>), or <see langword="null"/> for a
    /// valid lifecycle.
    /// </summary>
    public LifecycleBreak[]? Breaks { get; private set; }

    /// <summary>The status of the object <paramref name="scope"/> makes: the last state of its walk, the producer of the status member.</summary>
    public object? Status(FakeScope scope) => StateAt(scope.LifecyclePath(this).Status);

    /// <summary>
    /// A copy of this lifecycle whose walks each break a stamp in one of the <paramref name="breaks"/> ways (picked per object with one draw when there are
    /// several), walking into the state the break needs. A copy, so its objects keep walks of their own.
    /// </summary>
    public LifecycleDefinition WithBreaks(LifecycleBreak[] breaks)
    {
        var copy = (LifecycleDefinition)MemberwiseClone();
        copy.Breaks = breaks;
        return copy;
    }

    /// <summary>Whether a walk can get from the start into <paramref name="state"/> within the transitions it may take.</summary>
    public bool Reaches(int state) => _graph.Reaches(state);

    /// <summary>
    /// The state of a status <c>Populate</c> keeps, which the walk then ends in; <paramref name="member"/> names the status in messages.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="value"/> is not one of the states, or the walk can't get there within <c>MaxTransitions</c>.</exception>
    public int Kept(object? value, string member)
    {
        var state = StateOf(value);
        if (state < 0)
        {
            var states = string.Join(", ", Enumerable.Range(0, StateCount).Select(StateText));
            throw new InvalidOperationException(
                $"Populate keeps {member} = {value ?? "null"}, which is not one of its states in the lifecycle ({states}). Set one of them, or populate with PopulateMode.All to draw a new status.");
        }

        if (!Reaches(state))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Populate keeps {member} = {StateText(state)}, but its lifecycle can't get there: it takes {_graph.Distance(_start, state)} transitions from {StateText(_start)}, ")
                + string.Create(CultureInfo.InvariantCulture, $"and MaxTransitions is {_max}. Raise MaxTransitions, keep a nearer state, or populate with PopulateMode.All to draw a new status."));
        }

        return state;
    }

    /// <summary>
    /// The walk of one object: the states it goes through, the start first and its status last, and the gap before each transition. With <paramref name="end"/>
    /// (a status <c>Populate</c> keeps) it walks into that state as towards an end state, without drawing one.
    /// </summary>
    public LifecycleWalk Walk(SeededRandom random, int? end = null)
    {
        var path = new List<int>(4) { _start };
        var gaps = new List<long>(4);
        if (Breaks is { } breaks)
        {
            var broken = breaks[breaks.Length == 1 ? 0 : random.Index(breaks.Length)];
            WalkInto(random, path, gaps, broken.End);
            return new LifecycleWalk([.. path], [.. gaps]) { Break = broken };
        }

        if (end is not null || _ends is not null)
        {
            WalkInto(random, path, gaps, end ?? _ends![random.WeightedIndex(_endTotals!)]);
            return new LifecycleWalk([.. path], [.. gaps]);
        }

        var current = _start;
        for (var taken = 0; taken < _max; taken++)
        {
            var targets = _targets[current];
            if (targets.Length == 0)
                break; // terminal
            var picked = random.WeightedIndex(_totals[current]);
            if (targets[picked] == Stay)
                break;
            gaps.Add(Gap(random, _targetMoves[current][picked]));
            current = targets[picked];
            path.Add(current);
        }

        return new LifecycleWalk([.. path], [.. gaps]);
    }

    /// <summary>
    /// Whether some object can end its walk without entering <paramref name="state"/> (so a stamp of it must be able to hold <see langword="null"/>): it stops
    /// before it, or walks round it, or ends in a state that doesn't need it.
    /// </summary>
    public bool IsSkippable(int state) => _graph.IsSkippable(state);

    /// <summary>The state a date member is named after (<c>PaidAt</c>, <c>PaidOn</c>, <c>PaidDate</c>, any case), or -1.</summary>
    public int StateNamed(string member)
    {
        for (var s = 0; s < StateCount; s++)
        {
            var name = StateText(s);
            if (member.Length > name.Length && member.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && member.AsSpan(name.Length) is var suffix
                && (suffix.Equals("At", StringComparison.OrdinalIgnoreCase) || suffix.Equals("On", StringComparison.OrdinalIgnoreCase) || suffix.Equals("Date", StringComparison.OrdinalIgnoreCase)))
                return s;
        }

        return -1;
    }

    /// <summary>The shape of the lifecycle: where its walks can go.</summary>
    protected LifecycleGraph Graph => _graph;

    /// <summary>The state with number <paramref name="state"/>, boxed.</summary>
    public abstract object? StateAt(int state);

    /// <summary>The number of the state <paramref name="value"/> is, or -1 when it is none of them.</summary>
    public abstract int StateOf(object? value);

    /// <summary>The state with number <paramref name="state"/> as text, for messages and stamp names.</summary>
    public abstract string StateText(int state);

    /// <summary>The time between entering a state and taking <paramref name="move"/> out of it: one draw, the shortest gap for the simplest one.</summary>
    private static long Gap(SeededRandom random, LifecycleMove move) =>
        move.MinGap + (long)(random.Double() * (move.MaxGap - move.MinGap));

    private void WalkInto(SeededRandom random, List<int> path, List<long> gaps, int end)
    {
        var widest = _moves.Max(m => m.Length);
        Span<double> totals = widest <= 32 ? stackalloc double[widest] : new double[widest];
        Span<int> candidates = widest <= 32 ? stackalloc int[widest] : new int[widest];
        var current = _start;
        var left = _max;
        while (current != end)
        {
            var moves = _moves[current];
            var count = 0;
            var total = 0.0;
            for (var i = 0; i < moves.Length; i++)
            {
                if (_graph.Distance(moves[i].To, end) > left - 1)
                    continue; // from there the end can't be reached in time
                total += moves[i].Weight;
                candidates[count] = i;
                totals[count] = total;
                count++;
            }

            var move = moves[candidates[random.WeightedIndex(totals[..count])]];
            gaps.Add(Gap(random, move));
            current = move.To;
            path.Add(current);
            left--;
        }
    }
}

/// <summary>A checked lifecycle of <typeparamref name="TState"/> values; <see cref="Of"/> checks what <see cref="LifecycleBuilder{T, TState}"/> collected.</summary>
internal sealed class LifecycleDefinition<TState> : LifecycleDefinition
{
    private readonly TState[] _states;

    private LifecycleDefinition(TState[] states, int start, IReadOnlyList<LifecycleMove> transitions, double[] stays, IReadOnlyList<(int, double)> ends, int max)
        : base(start, states.Length, transitions, stays, ends, max) => _states = states;

    public override object? StateAt(int state) => _states[state];

    public override string StateText(int state) => Text(_states[state]);

    public override int StateOf(object? value) =>
        value is TState state ? Array.FindIndex(_states, s => EqualityComparer<TState>.Default.Equals(s, state)) : Array.FindIndex(_states, s => s is null && value is null);

    /// <summary>
    /// Checks the lifecycle <paramref name="lifecycle"/> writes; <paramref name="member"/> names it in messages, and <paramref name="stampMember"/> checks a
    /// stamp's selector and returns the name of its member.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="lifecycle"/> returned <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// No transitions, a state the start can't reach, an undefined enum value, an end state the walk can't reach in time, or a stamp of a state the lifecycle
    /// doesn't have or of a member stamped twice.
    /// </exception>
    public static LifecycleDefinition<TState> Of<T>(
        Func<LifecycleBuilder<T, TState>, LifecycleBuilder<T, TState>> lifecycle, string member, Func<LambdaExpression, string> stampMember)
    {
        var spec = (lifecycle(new LifecycleBuilder<T, TState>()) ?? throw new InvalidOperationException(
            $"The lifecycle of {member} returned null. Return the builder the lambda was given, with the transitions added: l => l.From(a).To(b).")).Current;
        if (spec.Transitions.IsEmpty)
            throw new ArgumentException($"The lifecycle of {member} has no transitions. Add them with .From(state).To(next), e.g. l => l.From(Placed).To(Paid).", nameof(lifecycle));

        var states = spec.States.ToArray();
        int Index(TState state) => Array.FindIndex(states, s => LifecycleBuilder<T, TState>.Same(s, state));
        if (Undefined(states) is { } undefined)
            throw new ArgumentException($"The lifecycle of {member} uses {undefined}, which is not a value of {EnumType!.Name}. Use one of its defined values.", nameof(lifecycle));

        var start = Index(spec.HasStart ? spec.Start : spec.Transitions[0].From);
        var transitions = spec.Transitions.Select(t => new LifecycleMove(Index(t.From), Index(t.To), t.Weight, t.MinGap.Ticks, t.MaxGap.Ticks)).ToList();
        var stays = Enumerable.Repeat(1.0, states.Length).ToArray();
        foreach (var (state, weight) in spec.Stays)
            stays[Index(state)] = weight;
        var ends = spec.Ends.Select(e => (Index(e.State), e.Weight)).ToList();
        var definition = new LifecycleDefinition<TState>(states, start, transitions, stays, ends, spec.Max)
        {
            Stamps = CheckStamps(spec, Index, member, stampMember, nameof(lifecycle)),
            Member = member[(member.LastIndexOf('.') + 1)..],
        };

        var startText = Text(states[start]);
        var graph = definition.Graph;
        if (graph.FirstUnreachable() is var unreachable and >= 0)
        {
            throw new ArgumentException(
                $"The lifecycle of {member} can't reach {Text(states[unreachable])} from its start state {startText}. Add a transition into it, or remove it.", nameof(lifecycle));
        }

        if (graph.FirstEndTooFar() is var far and >= 0)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The lifecycle of {member} can't end in {Text(states[far])}: it takes {graph.Distance(start, far)} transitions from {startText}, ")
                + string.Create(CultureInfo.InvariantCulture, $"and MaxTransitions is {spec.Max}. Raise MaxTransitions, or remove the EndsIn."),
                nameof(lifecycle));
        }

        return definition;
    }

    /// <summary>The stamps by state number and member name; a stamp of a state the lifecycle doesn't have, or a member stamped twice, is refused.</summary>
    private static (int State, string Member)[] CheckStamps<T>(
        LifecycleBuilder<T, TState>.Spec spec, Func<TState, int> index, string member, Func<LambdaExpression, string> stampMember, string paramName)
    {
        var stamps = new List<(int State, string Member)>(spec.Stamps.Count);
        foreach (var (state, selector) in spec.Stamps)
        {
            var name = stampMember(selector);
            var number = index(state);
            if (number < 0)
                throw new ArgumentException($"The lifecycle of {member} stamps {name} with {Text(state)}, which is not one of its states. Stamp a state it has.", paramName);
            if (stamps.Exists(s => s.Member == name))
                throw new ArgumentException($"The lifecycle of {member} stamps {name} twice. Give each member one state.", paramName);
            stamps.Add((number, name));
        }

        return [.. stamps];
    }

    private static string Text(TState state) => state?.ToString() ?? "null";

    /// <summary>The enum type of the states (their underlying type when nullable), or <see langword="null"/> when they aren't enum values.</summary>
    private static Type? EnumType { get; } = (Nullable.GetUnderlyingType(typeof(TState)) ?? typeof(TState)) is { IsEnum: true } type ? type : null;

    /// <summary>The first state that is not a defined value of its enum, or <see langword="null"/> when there is none (or the states aren't enum values).</summary>
    private static object? Undefined(TState[] states)
    {
        if (EnumType is not { } type)
            return null;
        var index = Array.FindIndex(states, state => state is not null && !Enum.IsDefined(type, state));
        return index < 0 ? null : states[index];
    }
}
