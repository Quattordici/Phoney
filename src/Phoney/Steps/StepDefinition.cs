namespace Phoney.Generation;

/// <summary>
/// One step of a <see cref="StepCheck{TState}"/>: its name, when it may run, how its argument is made and what it does to the state. Every form of
/// <c>Step(…)</c> ends here, so the run treats them alike; the argument is boxed once per step, which is nothing next to the step itself.
/// </summary>
/// <param name="Name">The step's name in the report.</param>
/// <param name="When">When the step may run, or <see langword="null"/> for always.</param>
/// <param name="Arguments">Makes the argument maker of one run (a generator gets a fresh copy per run), or <see langword="null"/> for a step without an argument.</param>
/// <param name="Apply">Applies the step to the state with its argument.</param>
/// <param name="IsAsync">Whether the step is asynchronous, which only <see cref="StepCheck{TState}.CheckAsync"/> runs.</param>
internal sealed record StepDefinition<TState>(
    string Name,
    Func<TState, bool>? When,
    Func<Func<Faker, TState, object?>>? Arguments,
    Func<TState, object?, ValueTask> Apply,
    bool IsAsync)
{
    /// <summary>A step without an argument.</summary>
    public static StepDefinition<TState> Of(string name, Action<TState> apply, Func<TState, bool>? when)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new(name, when, null, (state, _) =>
        {
            apply(state);
            return default;
        }, false);
    }

    /// <summary>An asynchronous step without an argument.</summary>
    public static StepDefinition<TState> Of(string name, Func<TState, Task> apply, Func<TState, bool>? when)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new(name, when, null, (state, _) => new ValueTask(apply(state)), true);
    }

    /// <summary>A step whose argument <paramref name="arguments"/> makes per run.</summary>
    public static StepDefinition<TState> Of<TArg>(string name, Func<Func<Faker, TState, TArg>> arguments, Action<TState, TArg> apply, Func<TState, bool>? when)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new(name, when, Boxed(arguments), (state, argument) =>
        {
            apply(state, (TArg)argument!);
            return default;
        }, false);
    }

    /// <summary>An asynchronous step whose argument <paramref name="arguments"/> makes per run.</summary>
    public static StepDefinition<TState> Of<TArg>(string name, Func<Func<Faker, TState, TArg>> arguments, Func<TState, TArg, Task> apply, Func<TState, bool>? when)
    {
        ArgumentNullException.ThrowIfNull(apply);
        return new(name, when, Boxed(arguments), (state, argument) => new ValueTask(apply(state, (TArg)argument!)), true);
    }

    private static Func<Func<Faker, TState, object?>> Boxed<TArg>(Func<Func<Faker, TState, TArg>> arguments) =>
        () =>
        {
            var make = arguments();
            return (faker, state) => make(faker, state);
        };

    /// <summary>Whether the step may run on <paramref name="state"/>.</summary>
    public bool Enabled(TState state) => When?.Invoke(state) ?? true;
}
