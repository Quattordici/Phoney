using System.Collections.Immutable;

namespace Phoney;

/// <summary>
/// The states a member can be in and the transitions between them, for <see cref="ObjectGenerator{T}.Lifecycle{TState}"/>:
/// <c>l =&gt; l.From(Placed).To(Paid, 4).To(Cancelled, 1).From(Paid).To(Shipped)</c>. Each <see cref="To"/> is a transition from the state of the last
/// <see cref="From"/>; the first <see cref="From"/> is the start, and a state with no transition out is terminal.
/// </summary>
/// <remarks>
/// Every object walks from the start: at each state it stays (weight 1 unless <see cref="Stays"/> says otherwise) or takes one of its transitions, by weight,
/// until it stays, reaches a terminal state or has taken <see cref="MaxTransitions"/> transitions. With <see cref="EndsIn"/> the end state is drawn first, by
/// weight, and the walk goes there. The builder is immutable: every method returns a new one.
/// </remarks>
/// <typeparam name="TState">The type of the states, usually an enum; any type compared with <see cref="EqualityComparer{T}.Default"/> works.</typeparam>
public sealed class LifecycleBuilder<TState>
{
    /// <summary>The default for <see cref="MaxTransitions"/>.</summary>
    public const int DefaultMaxTransitions = 16;

    internal LifecycleBuilder()
        : this(new Spec([], [], [], [], default!, false, default!, false, DefaultMaxTransitions))
    {
    }

    private LifecycleBuilder(Spec spec) => Current = spec;

    internal Spec Current { get; }

    /// <summary>Makes <paramref name="state"/> the state the following <see cref="To"/> and <see cref="Stays"/> calls are about. The first one is the start state.</summary>
    /// <param name="state">The state the transitions leave from.</param>
    public LifecycleBuilder<TState> From(TState state) => new(Current with
    {
        States = Current.Add(state),
        From = state,
        HasFrom = true,
    });

    /// <summary>Adds a transition from the state of the last <see cref="From"/> to <paramref name="state"/>.</summary>
    /// <param name="state">The state the transition goes to.</param>
    /// <param name="weight">How likely the transition is next to the state's other transitions and staying: positive and finite, 1 by default.</param>
    /// <exception cref="InvalidOperationException">No <see cref="From"/> came before.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is zero, negative, not a number or infinite.</exception>
    /// <exception cref="ArgumentException">The transition was added before.</exception>
    public LifecycleBuilder<TState> To(TState state, double weight = 1)
    {
        var from = RequireFrom(nameof(To));
        Positive(weight, nameof(weight));
        if (Current.Transitions.Any(t => Same(t.From, from) && Same(t.To, state)))
            throw new ArgumentException($"The transition from {Text(from)} to {Text(state)} is added twice. Add each transition once.", nameof(state));
        return new(Current with
        {
            States = Current.Add(state),
            Transitions = Current.Transitions.Add(new Transition(from, state, weight)),
        });
    }

    /// <summary>
    /// Sets how likely an object is to stay in the state of the last <see cref="From"/> next to its transitions (1 by default); 0 means an object that gets
    /// there always moves on.
    /// </summary>
    /// <param name="weight">Zero or more, finite.</param>
    /// <exception cref="InvalidOperationException">No <see cref="From"/> came before, or it was given twice for the state.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is negative, not a number or infinite.</exception>
    public LifecycleBuilder<TState> Stays(double weight)
    {
        var from = RequireFrom(nameof(Stays));
        if (!double.IsFinite(weight) || weight < 0)
            throw new ArgumentOutOfRangeException(nameof(weight), weight, "A stay weight is zero or more and finite.");
        if (Current.Stays.Any(s => Same(s.State, from)))
            throw new InvalidOperationException($"Stays is given twice for {Text(from)}. Give it once.");
        return new(Current with { Stays = Current.Stays.Add((from, weight)) });
    }

    /// <summary>Makes <paramref name="state"/> the start state instead of the state of the first <see cref="From"/>.</summary>
    /// <param name="state">The state every object starts in.</param>
    public LifecycleBuilder<TState> Start(TState state) => new(Current with { States = Current.Add(state), Start = state, HasStart = true });

    /// <summary>
    /// Draws the state an object ends in first, by weight, and walks there: <c>.EndsIn(Delivered, 70).EndsIn(Cancelled, 10).EndsIn(Paid, 20)</c> gives
    /// exactly that mix. Without it the end state is wherever the walk stops.
    /// </summary>
    /// <param name="state">A state objects may end in.</param>
    /// <param name="weight">How likely it is next to the other end states: positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is zero, negative, not a number or infinite.</exception>
    /// <exception cref="ArgumentException">The state is given twice.</exception>
    public LifecycleBuilder<TState> EndsIn(TState state, double weight)
    {
        Positive(weight, nameof(weight));
        if (Current.Ends.Any(e => Same(e.State, state)))
            throw new ArgumentException($"EndsIn({Text(state)}) is given twice. Give each end state once.", nameof(state));
        return new(Current with { States = Current.Add(state), Ends = Current.Ends.Add((state, weight)) });
    }

    /// <summary>The most transitions an object takes (16 by default), which stops a walk round a cycle: an object that has taken them stays where it is.</summary>
    /// <param name="count">One or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 1.</exception>
    public LifecycleBuilder<TState> MaxTransitions(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new(Current with { Max = count });
    }

    private TState RequireFrom(string method) =>
        Current.HasFrom ? Current.From : throw new InvalidOperationException($"{method} needs a state to leave from: start with .From(state), e.g. l => l.From(Placed).To(Paid).");

    private static void Positive(double weight, string name)
    {
        if (!double.IsFinite(weight) || weight <= 0)
            throw new ArgumentOutOfRangeException(name, weight, "A weight is greater than zero and finite.");
    }

    internal static bool Same(TState a, TState b) => EqualityComparer<TState>.Default.Equals(a, b);

    internal static string Text(TState state) => state?.ToString() ?? "null";

    /// <summary>One transition, as written.</summary>
    internal readonly record struct Transition(TState From, TState To, double Weight);

    /// <summary>Everything written so far: the states in the order they appear, transitions, stay weights and end states.</summary>
    internal sealed record Spec(
        ImmutableList<TState> States,
        ImmutableList<Transition> Transitions,
        ImmutableList<(TState State, double Weight)> Stays,
        ImmutableList<(TState State, double Weight)> Ends,
        TState From,
        bool HasFrom,
        TState Start,
        bool HasStart,
        int Max)
    {
        public ImmutableList<TState> Add(TState state) => States.Any(s => Same(s, state)) ? States : States.Add(state);
    }
}
