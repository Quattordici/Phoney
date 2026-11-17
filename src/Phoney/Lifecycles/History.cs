using System.Collections.Immutable;
using System.Linq.Expressions;

namespace Phoney;

/// <summary>
/// One generated object and the transitions its lifecycles took, from <see cref="ObjectGenerator{T}.GenerateHistory"/>: the object is exactly the one
/// <see cref="ObjectGenerator{T}.Generate(int)"/> makes at the same position, and the transitions are those of every lifecycle in its graph (its own and those of
/// nested objects and collection elements), in time order.
/// </summary>
/// <typeparam name="T">The type of the generated object.</typeparam>
public sealed class History<T>
{
    private readonly ImmutableHashSet<string> _lifecycles;

    internal History(T value, DateTimeOffset created, IReadOnlyList<Transition> transitions, ImmutableHashSet<string> lifecycles)
    {
        Object = value;
        Created = created;
        Transitions = transitions;
        _lifecycles = lifecycles;
    }

    /// <summary>The generated object, in the state its lifecycles ended in.</summary>
    public T Object { get; }

    /// <summary>When the object was created, which is when it entered the start state of its own lifecycles.</summary>
    public DateTimeOffset Created { get; }

    /// <summary>
    /// Every transition in the object's graph, earliest first (transitions at the same time in the order they were drawn). Start states are not transitions:
    /// an object is in them from its creation.
    /// </summary>
    public IReadOnlyList<Transition> Transitions { get; }

    /// <summary>
    /// The transitions of the lifecycle of <paramref name="member"/>, typed, earliest first: <c>h.Of(o =&gt; o.Status)</c>, or
    /// <c>h.Of(o =&gt; o.Lines.Each().Status)</c> for the lifecycles of every line.
    /// </summary>
    /// <typeparam name="TState">The type of the states.</typeparam>
    /// <param name="member">The status member, selected as for <see cref="ObjectGenerator{T}.Lifecycle{TState}"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="member"/> has no lifecycle.</exception>
    public IReadOnlyList<Transition<TState>> Of<TState>(Expression<Func<T, TState>> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var path = string.Join('.', ObjectGenerator<T>.MemberPath(member));
        if (!_lifecycles.Contains(path))
        {
            throw new ArgumentException(
                $"{typeof(T).Name}.{path} has no lifecycle, so it has no transitions. Give it one with .Lifecycle({member}, l => …), or ask for a member that has one.",
                nameof(member));
        }

        var typed = new List<Transition<TState>>();
        foreach (var transition in Transitions)
        {
            if (transition.Lifecycle == path)
                typed.Add(new Transition<TState>(transition.Member, transition.Path, transition.Owner, (TState)transition.From!, (TState)transition.To!, transition.At));
        }

        return typed;
    }
}

/// <summary>One move of an object's lifecycle, as <see cref="History{T}.Transitions"/> lists it.</summary>
public sealed class Transition
{
    internal Transition(string member, string path, string lifecycle, object owner, object? from, object? to, DateTimeOffset at)
    {
        Member = member;
        Path = path;
        Lifecycle = lifecycle;
        Owner = owner;
        From = from;
        To = to;
        At = at;
    }

    /// <summary>The name of the status member that changed: <c>Status</c>.</summary>
    public string Member { get; }

    /// <summary>Where the member is in the graph, from the generated object: <c>Status</c>, or <c>Lines[2].Status</c> for a collection element.</summary>
    public string Path { get; }

    /// <summary>The object whose member changed.</summary>
    public object Owner { get; }

    /// <summary>The state the object left.</summary>
    public object? From { get; }

    /// <summary>The state the object entered.</summary>
    public object? To { get; }

    /// <summary>When the object entered <see cref="To"/>.</summary>
    public DateTimeOffset At { get; }

    /// <summary>The path without element positions (<c>Lines.Status</c>), which names the lifecycle for <see cref="History{T}.Of{TState}"/>.</summary>
    internal string Lifecycle { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Path}: {From} → {To} at {At:O}";
}

/// <summary>One move of a lifecycle with states of type <typeparamref name="TState"/>, from <see cref="History{T}.Of{TState}"/>.</summary>
/// <typeparam name="TState">The type of the states.</typeparam>
/// <param name="Member">The name of the status member that changed: <c>Status</c>.</param>
/// <param name="Path">Where the member is in the graph, from the generated object: <c>Status</c>, or <c>Lines[2].Status</c>.</param>
/// <param name="Owner">The object whose member changed.</param>
/// <param name="From">The state the object left.</param>
/// <param name="To">The state the object entered.</param>
/// <param name="At">When the object entered <paramref name="To"/>.</param>
public sealed record Transition<TState>(string Member, string Path, object Owner, TState From, TState To, DateTimeOffset At);
