namespace Phoney;

// Stateful checks: Fake.Steps(() => new BankAccount()).Step(…).Invariant(…).Check().
public static partial class Fake
{
    /// <summary>
    /// Starts a stateful property check of the states <paramref name="create"/> makes: add steps with <see cref="StepCheck{TState}.Step(string, Action{TState}, Func{TState, bool}?)"/>
    /// and invariants with <see cref="StepCheck{TState}.Invariant(Func{TState, bool}, string?)"/>, then <see cref="StepCheck{TState}.Check"/> runs random sequences
    /// of the steps and shrinks a failing one to the shortest that still fails.
    /// </summary>
    /// <remarks>Inside a seeded scope (<see cref="Seeded(long, string?)"/>, <c>[FakeSeed]</c>) the check takes its seed, locale and reference date from the facade.</remarks>
    /// <example>
    /// <code>
    /// Fake.Steps(() =&gt; new BankAccount())
    ///     .Step("deposit", f =&gt; f.Finance.Amount(1, 100), (a, amount) =&gt; a.Deposit(amount))
    ///     .Invariant(a =&gt; a.Balance &gt;= 0)
    ///     .Check();
    /// </code>
    /// </example>
    /// <typeparam name="TState">The type of the state the steps act on.</typeparam>
    /// <param name="create">Makes a fresh state for every run.</param>
    /// <returns>A check without steps.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is <see langword="null"/>.</exception>
    public static StepCheck<TState> Steps<TState>(Func<TState> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        var steps = new StepCheck<TState>(create);
        return Facade() is { NewSeed: { } seed } facade ? steps.Seed(seed).ReferenceDate(facade.Reference).Locale(facade.LocaleCode!) : steps;
    }
}
