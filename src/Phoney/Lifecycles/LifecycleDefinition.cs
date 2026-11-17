using System.Globalization;

namespace Phoney.Generation;

/// <summary>
/// A checked lifecycle with its states numbered, ready to walk. A walk is the list of state numbers an object went through, the start first; it is drawn once
/// per object and kept on its scope (<see cref="FakeScope.LifecyclePath"/>), so every member that reads it sees the same path.
/// </summary>
/// <remarks>
/// The draws, which the shrinker relies on: with end states, one <see cref="SeededRandom.WeightedIndex(ReadOnlySpan{double})"/> draw for the end state first.
/// Then one such draw per state that isn't terminal, over [stay, then its transitions in the order they were written] (towards an end state: over the transitions
/// that can still reach it in time, never staying). Staying comes first, so the simplest draw stays and the simplest object is in its start state.
/// </remarks>
internal abstract class LifecycleDefinition
{
    private const int Stay = -1;

    private readonly int _start;
    private readonly int[][] _targets;
    private readonly double[][] _totals;
    private readonly int[][] _moves;
    private readonly double[][] _moveWeights;
    private readonly int[]? _ends;
    private readonly double[]? _endTotals;
    private readonly int[][] _distance;
    private readonly int _max;

    protected LifecycleDefinition(int start, int stateCount, IReadOnlyList<(int From, int To, double Weight)> transitions, double[] stays, IReadOnlyList<(int State, double Weight)> ends, int max)
    {
        _start = start;
        _max = max;
        _targets = new int[stateCount][];
        _totals = new double[stateCount][];
        _moves = new int[stateCount][];
        _moveWeights = new double[stateCount][];
        for (var s = 0; s < stateCount; s++)
        {
            var moves = transitions.Where(t => t.From == s).ToArray();
            _moves[s] = [.. moves.Select(t => t.To)];
            _moveWeights[s] = [.. moves.Select(t => t.Weight)];
            var targets = new List<int>();
            var totals = new List<double>();
            var total = 0.0;
            if (moves.Length > 0 && stays[s] > 0)
            {
                total += stays[s];
                targets.Add(Stay);
                totals.Add(total);
            }

            foreach (var move in moves)
            {
                total += move.Weight;
                targets.Add(move.To);
                totals.Add(total);
            }

            _targets[s] = [.. targets];
            _totals[s] = [.. totals];
        }

        _distance = Distances(stateCount);
        if (ends.Count > 0)
        {
            _ends = [.. ends.Select(e => e.State)];
            var total = 0.0;
            _endTotals = [.. ends.Select(e => total += e.Weight)];
        }
    }

    /// <summary>The states an object goes through, the start first and its status last.</summary>
    public int[] Walk(SeededRandom random)
    {
        var path = new List<int>(4) { _start };
        if (_ends is not null)
        {
            WalkInto(random, path, _ends[random.WeightedIndex(_endTotals!)]);
            return [.. path];
        }

        var current = _start;
        for (var taken = 0; taken < _max; taken++)
        {
            var targets = _targets[current];
            if (targets.Length == 0)
                break; // terminal
            var next = targets[random.WeightedIndex(_totals[current])];
            if (next == Stay)
                break;
            current = next;
            path.Add(current);
        }

        return [.. path];
    }

    /// <summary>The number of transitions the shortest way from <paramref name="from"/> to <paramref name="to"/> takes, or <see cref="int.MaxValue"/>.</summary>
    protected int Distance(int from, int to) => _distance[from][to];

    private void WalkInto(SeededRandom random, List<int> path, int end)
    {
        var widest = _moves.Max(m => m.Length);
        Span<double> totals = widest <= 32 ? stackalloc double[widest] : new double[widest];
        Span<int> candidates = widest <= 32 ? stackalloc int[widest] : new int[widest];
        var current = _start;
        var left = _max;
        while (current != end)
        {
            var moves = _moves[current];
            var weights = _moveWeights[current];
            var count = 0;
            var total = 0.0;
            for (var i = 0; i < moves.Length; i++)
            {
                if (_distance[moves[i]][end] > left - 1)
                    continue; // from there the end can't be reached in time
                candidates[count] = moves[i];
                totals[count++] = total += weights[i];
            }

            current = candidates[random.WeightedIndex(totals[..count])];
            path.Add(current);
            left--;
        }
    }

    private int[][] Distances(int count)
    {
        var distance = new int[count][];
        for (var from = 0; from < count; from++)
        {
            var row = Enumerable.Repeat(int.MaxValue, count).ToArray();
            row[from] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.TryDequeue(out var state))
            {
                foreach (var next in _moves[state])
                {
                    if (row[next] != int.MaxValue)
                        continue;
                    row[next] = row[state] + 1;
                    queue.Enqueue(next);
                }
            }

            distance[from] = row;
        }

        return distance;
    }

    /// <summary>The state with number <paramref name="state"/>, boxed.</summary>
    public abstract object? StateAt(int state);
}

/// <summary>A checked lifecycle of <typeparamref name="TState"/> values; <see cref="Of"/> checks what <see cref="LifecycleBuilder{TState}"/> collected.</summary>
internal sealed class LifecycleDefinition<TState> : LifecycleDefinition
{
    private readonly TState[] _states;

    private LifecycleDefinition(TState[] states, int start, IReadOnlyList<(int, int, double)> transitions, double[] stays, IReadOnlyList<(int, double)> ends, int max)
        : base(start, states.Length, transitions, stays, ends, max) => _states = states;

    public override object? StateAt(int state) => _states[state];

    /// <summary>Checks the lifecycle <paramref name="lifecycle"/> writes; <paramref name="member"/> names it in messages.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="lifecycle"/> returned <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No transitions, a state the start can't reach, an undefined enum value, or an end state the walk can't reach in time.</exception>
    public static LifecycleDefinition<TState> Of(Func<LifecycleBuilder<TState>, LifecycleBuilder<TState>> lifecycle, string member)
    {
        var spec = (lifecycle(new LifecycleBuilder<TState>()) ?? throw new InvalidOperationException(
            $"The lifecycle of {member} returned null. Return the builder the lambda was given, with the transitions added: l => l.From(a).To(b).")).Current;
        if (spec.Transitions.IsEmpty)
            throw new ArgumentException($"The lifecycle of {member} has no transitions. Add them with .From(state).To(next), e.g. l => l.From(Placed).To(Paid).", nameof(lifecycle));

        var states = spec.States.ToArray();
        int Index(TState state) => Array.FindIndex(states, s => LifecycleBuilder<TState>.Same(s, state));
        if (Undefined(states) is { } undefined)
            throw new ArgumentException($"The lifecycle of {member} uses {undefined}, which is not a value of {EnumType!.Name}. Use one of its defined values.", nameof(lifecycle));

        var start = Index(spec.HasStart ? spec.Start : spec.Transitions[0].From);
        var transitions = spec.Transitions.Select(t => (Index(t.From), Index(t.To), t.Weight)).ToList();
        var stays = Enumerable.Repeat(1.0, states.Length).ToArray();
        foreach (var (state, weight) in spec.Stays)
            stays[Index(state)] = weight;
        var ends = spec.Ends.Select(e => (Index(e.State), e.Weight)).ToList();
        var definition = new LifecycleDefinition<TState>(states, start, transitions, stays, ends, spec.Max);

        var startText = LifecycleBuilder<TState>.Text(states[start]);
        for (var s = 0; s < states.Length; s++)
        {
            if (definition.Distance(start, s) == int.MaxValue)
            {
                throw new ArgumentException(
                    $"The lifecycle of {member} can't reach {LifecycleBuilder<TState>.Text(states[s])} from its start state {startText}. Add a transition into it, or remove it.", nameof(lifecycle));
            }
        }

        foreach (var (end, _) in ends)
        {
            if (definition.Distance(start, end) > spec.Max)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"The lifecycle of {member} can't end in {LifecycleBuilder<TState>.Text(states[end])}: it takes {definition.Distance(start, end)} transitions from {startText}, ")
                    + string.Create(CultureInfo.InvariantCulture, $"and MaxTransitions is {spec.Max}. Raise MaxTransitions, or remove the EndsIn."),
                    nameof(lifecycle));
            }
        }

        return definition;
    }

    /// <summary>The enum type of the states (their underlying type when nullable), or <see langword="null"/> when they aren't enum values.</summary>
    private static Type? EnumType { get; } = Nullable.GetUnderlyingType(typeof(TState)) is { IsEnum: true } underlying ? underlying : typeof(TState).IsEnum ? typeof(TState) : null;

    /// <summary>The first state that is not a defined value of its enum, or <see langword="null"/> when there is none (or the states aren't enum values).</summary>
    private static object? Undefined(TState[] states)
    {
        if (EnumType is not { } type)
            return null;
        var index = Array.FindIndex(states, state => state is not null && !Enum.IsDefined(type, state));
        return index < 0 ? null : states[index];
    }
}
