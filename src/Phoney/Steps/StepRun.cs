using System.Globalization;
using System.Text;
using Phoney.Generation;

namespace Phoney;

/// <summary>
/// One run of a <see cref="StepCheck{TState}"/>: the steps it took, in order, and the state they left. A failed check reports the run it started from as
/// <see cref="FakeCheckException{T}.Original"/> and the shortest failing run shrinking found as <see cref="FakeCheckException{T}.Counterexample"/>.
/// </summary>
/// <typeparam name="TState">The type of the state the steps act on.</typeparam>
public sealed class StepRun<TState>
{
    private readonly List<TakenStep> _steps = [];

    internal StepRun(Faker faker) => Faker = faker;

    /// <summary>The steps of the run, in the order they were taken, up to the one that failed.</summary>
    public IReadOnlyList<TakenStep> Steps => _steps;

    /// <summary>The state after the last step (disposed by then when it is disposable), or its default when creating it failed.</summary>
    public TState State { get; internal set; } = default!;

    /// <summary>
    /// The number of the step after which the run failed (1 is the first step), 0 when it failed right after the state was created, or <see langword="null"/> when
    /// it passed.
    /// </summary>
    public int? FailedAt { get; private set; }

    /// <summary>Why the run failed, for the report, or <see langword="null"/> when it passed.</summary>
    internal string? Reason { get; private set; }

    /// <summary>The faker the run draws from, while it runs.</summary>
    internal Faker Faker { get; }

    internal void Add(TakenStep step) => _steps.Add(step);

    internal void Fail(string reason)
    {
        FailedAt = _steps.Count;
        Reason = reason;
    }

    /// <summary>The steps, numbered, one per line (<c>1. deposit 10</c>), or <c>(no steps)</c>.</summary>
    public override string ToString() => Describe(Environment.NewLine);

    /// <summary>The steps, numbered, separated by <paramref name="separator"/>.</summary>
    internal string Describe(string separator)
    {
        if (_steps.Count == 0)
            return "(no steps)";
        var text = new StringBuilder();
        for (var i = 0; i < _steps.Count; i++)
        {
            if (i > 0)
                text.Append(separator);
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {_steps[i]}");
        }

        return text.ToString();
    }
}

/// <summary>A step a <see cref="StepRun{TState}"/> took: the step's name and the argument it was given.</summary>
public readonly record struct TakenStep
{
    internal TakenStep(string name, bool hasArgument, object? argument)
    {
        Name = name;
        HasArgument = hasArgument;
        Argument = argument;
    }

    /// <summary>The name the step was registered with.</summary>
    public string Name { get; }

    /// <summary>Whether the step takes an argument.</summary>
    public bool HasArgument { get; }

    /// <summary>The argument the step was given, or <see langword="null"/> for a step without one.</summary>
    public object? Argument { get; }

    /// <summary>The name, then the argument when the step takes one: <c>deposit 10</c>, <c>add Cart { … }</c>.</summary>
    public override string ToString() => HasArgument ? $"{Name} {ObjectDescription.Of(Argument)}" : Name;
}
