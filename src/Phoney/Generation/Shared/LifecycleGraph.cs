// Compiled into both the runtime and the netstandard2.0 generator (like ConventionRules.cs): keep it free of newer APIs.
using System.Collections.Generic;
using System.Linq;

namespace Phoney.Generation;

/// <summary>
/// The shape of a lifecycle by state number: where its walks can go, and so which states every object reaches. The runtime checks a lifecycle with it when the
/// rule is added, and the analyzer checks the lifecycle written in the source with the same code, so the two agree on what can't work.
/// </summary>
/// <remarks>
/// A walk starts at <see cref="Start"/> and takes at most <see cref="Max"/> transitions. Without end states it stops in a state that has no transitions, in one
/// it may stay in (a stay weight above zero) or when it has used up its transitions; with end states it walks into one of them, taking only transitions from
/// which that end can still be reached in time.
/// </remarks>
internal sealed class LifecycleGraph
{
    private readonly int[][] _targets;
    private readonly bool[] _canStop;
    private readonly int[]? _ends;
    private readonly int[][] _distance;

    /// <summary>Describes a lifecycle.</summary>
    /// <param name="start">The state every walk starts in.</param>
    /// <param name="stateCount">The number of states.</param>
    /// <param name="moves">The transitions, by state number.</param>
    /// <param name="stays">The stay weight of each state; zero means a walk that gets there moves on.</param>
    /// <param name="ends">The end states, or none when the end is wherever a walk stops.</param>
    /// <param name="max">The most transitions a walk takes.</param>
    public LifecycleGraph(int start, int stateCount, IReadOnlyList<(int From, int To)> moves, IReadOnlyList<double> stays, IReadOnlyList<int> ends, int max)
    {
        Start = start;
        Max = max;
        var targets = new List<int>[stateCount];
        for (var s = 0; s < stateCount; s++)
            targets[s] = [];
        foreach (var (from, to) in moves)
            targets[from].Add(to);

        _targets = new int[stateCount][];
        _canStop = new bool[stateCount];
        for (var s = 0; s < stateCount; s++)
        {
            _targets[s] = [.. targets[s]];
            _canStop[s] = _targets[s].Length == 0 || stays[s] > 0;
        }

        _ends = ends.Count == 0 ? null : [.. ends];
        _distance = new int[stateCount][];
        for (var s = 0; s < stateCount; s++)
            _distance[s] = DistancesFrom(s);
    }

    /// <summary>The state walks start in.</summary>
    public int Start { get; }

    /// <summary>The most transitions a walk takes.</summary>
    public int Max { get; }

    /// <summary>The number of states.</summary>
    public int StateCount => _targets.Length;

    /// <summary>The number of transitions the shortest way from <paramref name="from"/> to <paramref name="to"/> takes, or <see cref="int.MaxValue"/>.</summary>
    public int Distance(int from, int to) => _distance[from][to];

    /// <summary>Whether a walk can get from the start into <paramref name="state"/> within the transitions it may take.</summary>
    public bool Reaches(int state) => _distance[Start][state] <= Max;

    /// <summary>The first state the start has no way into at all, or -1 when it reaches every one.</summary>
    public int FirstUnreachable()
    {
        for (var s = 0; s < StateCount; s++)
        {
            if (_distance[Start][s] == int.MaxValue)
                return s;
        }

        return -1;
    }

    /// <summary>The first end state a walk can't get to within the transitions it may take, or -1.</summary>
    public int FirstEndTooFar()
    {
        if (_ends is null)
            return -1;
        var index = System.Array.FindIndex(_ends, end => !Reaches(end));
        return index < 0 ? -1 : _ends[index];
    }

    /// <summary>
    /// Why a <c>Stays</c> of one of the <paramref name="stayed"/> states (in the order they were written) can have no effect, or <see langword="null"/>: with end
    /// states a walk goes straight to the one drawn and never stays, and an object in a state with no transitions out always stays. One message for the
    /// runtime and the analyzer; <paramref name="member"/> names the lifecycle and <paramref name="text"/> writes a state.
    /// </summary>
    public string? IdleStay(IEnumerable<int> stayed, string member, System.Func<int, string> text)
    {
        foreach (var state in stayed)
        {
            if (_ends is not null)
            {
                return $"The lifecycle of {member} gives {text(state)} a stay weight, but it has EndsIn: the end state is drawn first and the walk goes straight there, "
                    + "so Stays has no effect. Remove Stays, or the EndsIn calls.";
            }

            if (_targets[state].Length == 0)
            {
                return $"The lifecycle of {member} gives {text(state)} a stay weight, but {text(state)} has no transitions out, so an object there always stays. "
                    + $"Remove Stays, or add a transition out of {text(state)}.";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether some object can end its walk without entering <paramref name="state"/> (so a stamp of it must be able to hold <see langword="null"/>): it stops
    /// before it, or walks round it, or ends in a state that doesn't need it.
    /// </summary>
    public bool IsSkippable(int state)
    {
        if (state == Start)
            return false;
        if (_ends is null)
            return CanStopAvoiding(state);
        return _ends.Any(end => end != state && CanArriveAvoiding(state, end));
    }

    /// <summary>Whether a walk without end states can stop (stay, reach a terminal state or use up its transitions) without entering <paramref name="avoided"/>.</summary>
    private bool CanStopAvoiding(int avoided)
    {
        var seen = new HashSet<(int State, int Taken)> { (Start, 0) };
        var queue = new Queue<(int State, int Taken)>();
        queue.Enqueue((Start, 0));
        while (queue.Count > 0)
        {
            var (state, taken) = queue.Dequeue();
            if (taken == Max || _canStop[state])
                return true;
            foreach (var to in _targets[state].Where(to => to != avoided && seen.Add((to, taken + 1))))
                queue.Enqueue((to, taken + 1));
        }

        return false;
    }

    /// <summary>Whether a walk towards <paramref name="end"/> can arrive without entering <paramref name="avoided"/>, taking only the moves the walk may take.</summary>
    private bool CanArriveAvoiding(int avoided, int end)
    {
        var seen = new HashSet<(int State, int Left)> { (Start, Max) };
        var queue = new Queue<(int State, int Left)>();
        queue.Enqueue((Start, Max));
        while (queue.Count > 0)
        {
            var (state, left) = queue.Dequeue();
            if (state == end)
                return true;
            foreach (var to in _targets[state].Where(to => to != avoided && _distance[to][end] <= left - 1 && seen.Add((to, left - 1))))
                queue.Enqueue((to, left - 1));
        }

        return false;
    }

    private int[] DistancesFrom(int from)
    {
        var row = new int[StateCount];
        for (var s = 0; s < row.Length; s++)
            row[s] = int.MaxValue;
        row[from] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (var to in _targets[state].Where(to => row[to] == int.MaxValue))
            {
                row[to] = row[state] + 1;
                queue.Enqueue(to);
            }
        }

        return row;
    }
}
