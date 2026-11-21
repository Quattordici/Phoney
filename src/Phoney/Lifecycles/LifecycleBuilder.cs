using System.Collections.Immutable;
using System.Linq.Expressions;

namespace Phoney;

/// <summary>
/// The states a member can be in and the transitions between them, for <see cref="ObjectGenerator{T}.Lifecycle{TState}"/>:
/// <c>l =&gt; l.From(Placed).To(Paid, 4).To(Cancelled, 1).From(Paid).To(Shipped)</c>. Each <see cref="To"/> is a transition from the state of the last
/// <see cref="From"/>; the first <see cref="From"/> is the start, and a state with no transition out is terminal.
/// </summary>
/// <remarks>
/// <para>
/// Every object walks from the start: at each state it stays (weight 1 unless <see cref="Stays"/> says otherwise) or takes one of its transitions, by weight,
/// until it stays, reaches a terminal state or has taken <see cref="MaxTransitions"/> transitions. With <see cref="EndsIn"/> the end state is drawn first, by
/// weight, and the walk goes there. The builder is immutable: every method returns a new one.
/// </para>
/// <para>
/// Each transition takes time (<c>after:</c>, an hour to a week by default) from the object's creation on, scaled down when the walk would pass the reference
/// date. A date member named after a state (<c>PaidAt</c>, <c>PaidOn</c>, <c>PaidDate</c>) or tied to one with <see cref="Stamp"/> gets the time the object
/// entered it, or <see langword="null"/> when it never did; an "updated" member gets the time of the last transition.
/// </para>
/// </remarks>
/// <typeparam name="T">The type of the generated objects; stamps are selected from it like the lifecycle's member.</typeparam>
/// <typeparam name="TState">The type of the states, usually an enum; any type compared with <see cref="EqualityComparer{T}.Default"/> works.</typeparam>
public sealed class LifecycleBuilder<T, TState>
{
    /// <summary>The default for <see cref="MaxTransitions"/>.</summary>
    public const int DefaultMaxTransitions = 16;

    internal LifecycleBuilder()
        : this(new Spec([], [], [], [], [], default!, false, default!, false, DefaultMaxTransitions))
    {
    }

    private LifecycleBuilder(Spec spec) => Current = spec;

    internal Spec Current { get; }

    /// <summary>Makes <paramref name="state"/> the state the following <see cref="To"/> and <see cref="Stays"/> calls are about. The first one is the start state.</summary>
    /// <param name="state">The state the transitions leave from.</param>
    public LifecycleBuilder<T, TState> From(TState state) => new(Current with
    {
        States = Current.Add(state),
        From = state,
        HasFrom = true,
    });

    /// <summary>Adds a transition from the state of the last <see cref="From"/> to <paramref name="state"/>.</summary>
    /// <param name="state">The state the transition goes to.</param>
    /// <param name="weight">How likely the transition is next to the state's other transitions and staying: positive and finite, 1 by default.</param>
    /// <param name="after">
    /// How long after entering the state of the last <see cref="From"/> the transition happens, drawn evenly between the two; an hour to a week by default.
    /// </param>
    /// <exception cref="InvalidOperationException">No <see cref="From"/> came before.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="weight"/> is zero, negative, not a number or infinite, or <paramref name="after"/> is negative or its maximum is below its minimum.
    /// </exception>
    /// <exception cref="ArgumentException">The transition was added before.</exception>
    public LifecycleBuilder<T, TState> To(TState state, double weight = 1, (TimeSpan Min, TimeSpan Max)? after = null)
    {
        var from = RequireFrom(nameof(To));
        Positive(weight, nameof(weight));
        var (min, max) = after ?? (Generation.LifecycleDefinition.DefaultMinGap, Generation.LifecycleDefinition.DefaultMaxGap);
        if (min < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(after), min, "A transition can't happen before the state it leaves was entered: give after a minimum of zero or more.");
        if (max < min)
            throw new ArgumentOutOfRangeException(nameof(after), max, $"The longest gap ({max}) is shorter than the shortest ({min}). Give after as (shortest, longest).");
        if (Current.Transitions.Any(t => Same(t.From, from) && Same(t.To, state)))
            throw new ArgumentException($"The transition from {Text(from)} to {Text(state)} is added twice. Add each transition once.", nameof(state));
        return new(Current with
        {
            States = Current.Add(state),
            Transitions = Current.Transitions.Add(new Transition(from, state, weight, min, max)),
        });
    }

    /// <summary>
    /// Sets how likely an object is to stay in the state of the last <see cref="From"/> next to its transitions (1 by default); 0 means an object that gets
    /// there always moves on. It only matters for a state with transitions out, in a lifecycle without <see cref="EndsIn"/> (a walk towards an end state
    /// never stays), so anything else is refused when the lifecycle is added.
    /// </summary>
    /// <param name="weight">Zero or more, finite.</param>
    /// <exception cref="InvalidOperationException">No <see cref="From"/> came before, or it was given twice for the state.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is negative, not a number or infinite.</exception>
    public LifecycleBuilder<T, TState> Stays(double weight)
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
    public LifecycleBuilder<T, TState> Start(TState state) => new(Current with { States = Current.Add(state), Start = state, HasStart = true });

    /// <summary>
    /// Draws the state an object ends in first, by weight, and walks there: <c>.EndsIn(Delivered, 70).EndsIn(Cancelled, 10).EndsIn(Paid, 20)</c> gives
    /// exactly that mix. Without it the end state is wherever the walk stops.
    /// </summary>
    /// <param name="state">A state objects may end in.</param>
    /// <param name="weight">How likely it is next to the other end states: positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weight"/> is zero, negative, not a number or infinite.</exception>
    /// <exception cref="ArgumentException">The state is given twice.</exception>
    public LifecycleBuilder<T, TState> EndsIn(TState state, double weight)
    {
        Positive(weight, nameof(weight));
        if (Current.Ends.Any(e => Same(e.State, state)))
            throw new ArgumentException($"EndsIn({Text(state)}) is given twice. Give each end state once.", nameof(state));
        return new(Current with { States = Current.Add(state), Ends = Current.Ends.Add((state, weight)) });
    }

    /// <summary>The most transitions an object takes (16 by default), which stops a walk round a cycle: an object that has taken them stays where it is.</summary>
    /// <param name="count">One or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 1.</exception>
    public LifecycleBuilder<T, TState> MaxTransitions(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new(Current with { Max = count });
    }

    /// <summary>
    /// Ties <paramref name="member"/> to <paramref name="state"/>: it gets the time the object last entered the state, or <see langword="null"/> when it never
    /// did. Members named after a state (<c>PaidAt</c>, <c>PaidOn</c>, <c>PaidDate</c>) are tied without it. A member on a nested object is selected like the
    /// lifecycle's member: <c>o =&gt; o.Lines.Each().PackedAt</c> for <c>o =&gt; o.Lines.Each().Status</c>.
    /// </summary>
    /// <typeparam name="TValue">A <see cref="DateTimeOffset"/>, <see cref="DateTime"/> (UTC) or <see cref="DateOnly"/>, nullable unless every object reaches the state.</typeparam>
    /// <param name="state">One of the lifecycle's states.</param>
    /// <param name="member">The member, on the object that holds the lifecycle's member.</param>
    /// <remarks>
    /// A rule for the member added after the lifecycle replaces the stamp. A member that already has a rule, or that another lifecycle of the object already
    /// stamps, is refused when the lifecycle is added, because the stamp would do nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    public LifecycleBuilder<T, TState> Stamp<TValue>(TState state, Expression<Func<T, TValue>> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(Current with { Stamps = Current.Stamps.Add((state, member)) });
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
    internal readonly record struct Transition(TState From, TState To, double Weight, TimeSpan MinGap, TimeSpan MaxGap);

    /// <summary>Everything written so far: the states in the order they appear, transitions, stay weights, end states and stamps.</summary>
    internal sealed record Spec(
        ImmutableList<TState> States,
        ImmutableList<Transition> Transitions,
        ImmutableList<(TState State, double Weight)> Stays,
        ImmutableList<(TState State, double Weight)> Ends,
        ImmutableList<(TState State, LambdaExpression Member)> Stamps,
        TState From,
        bool HasFrom,
        TState Start,
        bool HasStart,
        int Max)
    {
        public ImmutableList<TState> Add(TState state) => States.Any(s => Same(s, state)) ? States : States.Add(state);
    }
}
