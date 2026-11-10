namespace Phoney.Generation;

/// <summary>
/// What must hold for the state of a <see cref="StepCheck{TState}"/> after it is created and after every step: a predicate, or an assertion that throws.
/// </summary>
/// <param name="Text">The invariant as written (<c>a =&gt; a.Balance &gt;= 0</c>), for the report.</param>
/// <param name="Holds">Whether the invariant holds; an exception is a failure too.</param>
/// <param name="IsAsync">Whether the invariant is asynchronous, which only <see cref="StepCheck{TState}.CheckAsync"/> runs.</param>
internal sealed record StepInvariant<TState>(string Text, Func<TState, ValueTask<bool>> Holds, bool IsAsync)
{
    public static StepInvariant<TState> Of(Func<TState, bool> invariant, string? text)
    {
        ArgumentNullException.ThrowIfNull(invariant);
        return new(TextOf(text), state => new ValueTask<bool>(invariant(state)), false);
    }

    public static StepInvariant<TState> Of(Action<TState> invariant, string? text)
    {
        ArgumentNullException.ThrowIfNull(invariant);
        return new(TextOf(text), state =>
        {
            invariant(state);
            return new ValueTask<bool>(true);
        }, false);
    }

    public static StepInvariant<TState> Of(Func<TState, Task> invariant, string? text)
    {
        ArgumentNullException.ThrowIfNull(invariant);
        return new(TextOf(text), async state =>
        {
            await invariant(state).ConfigureAwait(false);
            return true;
        }, true);
    }

    private static string TextOf(string? text) => string.IsNullOrWhiteSpace(text) ? "invariant" : text.Trim();
}
