using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Phoney.Data;
using Phoney.Generation;

namespace Phoney;

/// <summary>
/// A stateful property check: random sequences of steps run against a fresh state, the invariants are checked after the state is created and after every
/// step, and a failing sequence is <em>shrunk</em> to the shortest, simplest one that still fails the same way. Start one with
/// <see cref="Fake.Steps{TState}(Func{TState})"/>.
/// </summary>
/// <remarks>
/// <para>
/// Immutable: every method returns a new check, so a base check can be shared and extended. Every choice (whether the run goes on, which step, its argument)
/// is a draw from one seeded faker per run, so a run is replayed from its seed and shrinks like a generated object: whole steps are deleted, and arguments
/// become smaller and simpler, made by the same rules.
/// </para>
/// <para>
/// The seed is the one set with <see cref="Seed"/> (or the facade's, inside <see cref="Fake.Seeded(long, string?)"/> or <c>[FakeSeed]</c>), else
/// <c>PHONEY_SEED</c>, else a new random one that the failure reports. Run <c>n</c> depends only on the seed and <c>n</c>, so it is the same whatever the
/// number of runs. A state that is <see cref="IDisposable"/> or <see cref="IAsyncDisposable"/> is disposed after every run, shrink candidates included.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Fake.Steps(() =&gt; new BankAccount())
///     .Step("deposit", f =&gt; f.Finance.Amount(1, 100), (a, amount) =&gt; a.Deposit(amount))
///     .Step("withdraw", f =&gt; f.Finance.Amount(1, 100), (a, amount) =&gt; a.Withdraw(amount), when: a =&gt; a.Balance &gt; 0)
///     .Invariant(a =&gt; a.Balance &gt;= 0)
///     .Check();
/// </code>
/// </example>
/// <typeparam name="TState">The type of the state the steps act on: the system under test, or a pair of it and a model to compare it with.</typeparam>
public sealed class StepCheck<TState>
{
    /// <summary>How the report says to pin the seed.</summary>
    private const string Pin = "Seed({0}) on the steps";

    private readonly Func<TState> _create;
    private readonly ImmutableArray<StepDefinition<TState>> _steps;
    private readonly ImmutableArray<StepInvariant<TState>> _invariants;
    private readonly long? _seed;
    private readonly string? _locale;
    private readonly DateTimeOffset? _referenceDate;

    internal StepCheck(Func<TState> create)
        : this(create, [], [], null, null, null)
    {
    }

    private StepCheck(
        Func<TState> create,
        ImmutableArray<StepDefinition<TState>> steps,
        ImmutableArray<StepInvariant<TState>> invariants,
        long? seed,
        string? locale,
        DateTimeOffset? referenceDate)
    {
        _create = create;
        _steps = steps;
        _invariants = invariants;
        _seed = seed;
        _locale = locale;
        _referenceDate = referenceDate;
    }

    /// <summary>Adds a step without an argument.</summary>
    /// <param name="name">The step's name in the report.</param>
    /// <param name="apply">What the step does to the state; an exception fails the run, so assertions in it are postconditions.</param>
    /// <param name="when">When the step may run (a precondition), or <see langword="null"/> for always.</param>
    /// <returns>A new check with the step.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="apply"/> is <see langword="null"/>.</exception>
    public StepCheck<TState> Step(string name, Action<TState> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), apply, when));

    /// <summary>Adds an asynchronous step without an argument; only <see cref="CheckAsync"/> runs it.</summary>
    /// <inheritdoc cref="Step(string, Action{TState}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step(string name, Func<TState, Task> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), apply, when));

    /// <summary>Adds a step whose argument is made by <paramref name="argument"/> before it runs: <c>Step("deposit", f =&gt; f.Finance.Amount(), (a, x) =&gt; a.Deposit(x))</c>.</summary>
    /// <typeparam name="TArg">The type of the argument.</typeparam>
    /// <param name="name">The step's name in the report, which shows the argument after it (<c>deposit 10</c>).</param>
    /// <param name="argument">Makes the argument from the run's faker.</param>
    /// <param name="apply">What the step does to the state with the argument; an exception fails the run, so assertions in it are postconditions.</param>
    /// <param name="when">When the step may run (a precondition), or <see langword="null"/> for always.</param>
    /// <returns>A new check with the step.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="argument"/> or <paramref name="apply"/> is <see langword="null"/>.</exception>
    public StepCheck<TState> Step<TArg>(string name, Func<Faker, TArg> argument, Action<TState, TArg> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), Ignoring(argument), apply, when));

    /// <summary>Adds an asynchronous step whose argument is made by <paramref name="argument"/>; only <see cref="CheckAsync"/> runs it.</summary>
    /// <inheritdoc cref="Step{TArg}(string, Func{Faker, TArg}, Action{TState, TArg}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step<TArg>(string name, Func<Faker, TArg> argument, Func<TState, TArg, Task> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), Ignoring(argument), apply, when));

    /// <summary>
    /// Adds a step whose argument is made from the run's faker and the current state, e.g. a key that is already in a cache:
    /// <c>Step("get", (f, c) =&gt; f.PickRandom(c.Keys), (c, key) =&gt; c.Get(key), when: c =&gt; c.Count &gt; 0)</c>.
    /// </summary>
    /// <inheritdoc cref="Step{TArg}(string, Func{Faker, TArg}, Action{TState, TArg}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step<TArg>(string name, Func<Faker, TState, TArg> argument, Action<TState, TArg> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), Always(argument), apply, when));

    /// <summary>Adds an asynchronous step whose argument is made from the run's faker and the current state; only <see cref="CheckAsync"/> runs it.</summary>
    /// <inheritdoc cref="Step{TArg}(string, Func{Faker, TArg}, Action{TState, TArg}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step<TArg>(string name, Func<Faker, TState, TArg> argument, Func<TState, TArg, Task> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), Always(argument), apply, when));

    /// <summary>
    /// Adds a step whose argument is an object of <paramref name="argument"/>, made with its rules from the run's draws, so it shrinks like the rest of the
    /// run: <c>Step("add", Fake.For&lt;Customer&gt;(), (repo, c) =&gt; repo.Add(c))</c>. A generator with unique values starts afresh for every run.
    /// </summary>
    /// <exception cref="ArgumentException">The generator can't make objects for another one (it is keyed, verifies its rules, numbers its objects or has a feed).</exception>
    /// <inheritdoc cref="Step{TArg}(string, Func{Faker, TArg}, Action{TState, TArg}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step<TArg>(string name, ObjectGenerator<TArg> argument, Action<TState, TArg> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), FromGenerator(argument), apply, when));

    /// <summary>Adds an asynchronous step whose argument is an object of <paramref name="argument"/>; only <see cref="CheckAsync"/> runs it.</summary>
    /// <inheritdoc cref="Step{TArg}(string, ObjectGenerator{TArg}, Action{TState, TArg}, Func{TState, bool}?)"/>
    public StepCheck<TState> Step<TArg>(string name, ObjectGenerator<TArg> argument, Func<TState, TArg, Task> apply, Func<TState, bool>? when = null) =>
        With(StepDefinition<TState>.Of(NameOf(name), FromGenerator(argument), apply, when));

    /// <summary>Adds an invariant that must hold after the state is created and after every step; the report shows it as written.</summary>
    /// <param name="invariant">What must hold for the state.</param>
    /// <param name="description">How the report names the invariant; the expression as written by default.</param>
    /// <returns>A new check with the invariant.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="invariant"/> is <see langword="null"/>.</exception>
    public StepCheck<TState> Invariant(Func<TState, bool> invariant, [CallerArgumentExpression(nameof(invariant))] string? description = null) =>
        With(StepInvariant<TState>.Of(invariant, description));

    /// <summary>Adds an invariant that is an assertion (of any test framework), which fails by throwing, after the state is created and after every step.</summary>
    /// <inheritdoc cref="Invariant(Func{TState, bool}, string?)"/>
    public StepCheck<TState> Invariant(Action<TState> invariant, [CallerArgumentExpression(nameof(invariant))] string? description = null) =>
        With(StepInvariant<TState>.Of(invariant, description));

    /// <summary>Adds an asynchronous invariant, which fails when its task faults; only <see cref="CheckAsync"/> runs it.</summary>
    /// <inheritdoc cref="Invariant(Func{TState, bool}, string?)"/>
    public StepCheck<TState> Invariant(Func<TState, Task> invariant, [CallerArgumentExpression(nameof(invariant))] string? description = null) =>
        With(StepInvariant<TState>.Of(invariant, description));

    /// <summary>Seeds the check, so it runs the same steps and finds and shrinks the same failure every time.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>A new check with the seed.</returns>
    public StepCheck<TState> Seed(long seed) => new(_create, _steps, _invariants, seed, _locale, _referenceDate);

    /// <summary>The locale of the faker the arguments are made with.</summary>
    /// <param name="locale">Locale code or culture name; see <see cref="Locales.All"/>.</param>
    /// <returns>A new check with the locale.</returns>
    /// <exception cref="ArgumentException">The locale is unknown.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is <see langword="null"/>.</exception>
    public StepCheck<TState> Locale(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        return new(_create, _steps, _invariants, _seed, LocaleStore.Get(locale).Info.Code, _referenceDate);
    }

    /// <summary>The "now" that relative dates of the arguments are based on; <see cref="Faker.DefaultSeededReferenceDate"/> by default.</summary>
    /// <param name="referenceDate">The reference date.</param>
    /// <returns>A new check with the reference date.</returns>
    public StepCheck<TState> ReferenceDate(DateTimeOffset referenceDate) => new(_create, _steps, _invariants, _seed, _locale, referenceDate);

    /// <summary>
    /// Runs <paramref name="runs"/> random sequences of up to <paramref name="maxSteps"/> steps and checks the invariants after every step; a sequence that fails
    /// is shrunk to the shortest, simplest one that fails the same way (an invariant that is false, or the same exception type).
    /// </summary>
    /// <param name="runs">How many sequences to run; 0 runs none.</param>
    /// <param name="maxSteps">The most steps a sequence takes.</param>
    /// <param name="maxShrinks">How many simpler sequences shrinking may try; 0 reports the original one.</param>
    /// <exception cref="FakeCheckException{T}">A run failed; its <see cref="FakeCheckException{T}.Counterexample"/> is the shrunk <see cref="StepRun{TState}"/>.</exception>
    /// <exception cref="InvalidOperationException">There is no step, or a step or invariant is asynchronous (use <see cref="CheckAsync"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="runs"/> or <paramref name="maxShrinks"/> is negative, or <paramref name="maxSteps"/> is not positive.</exception>
    public void Check(int runs = 100, int maxSteps = 50, int maxShrinks = 1000)
    {
        if (_steps.FirstOrDefault(s => s.IsAsync) is { } step)
            throw Asynchronous($"The step '{step.Name}' is asynchronous");
        if (_invariants.FirstOrDefault(i => i.IsAsync) is { } invariant)
            throw Asynchronous($"The invariant {invariant.Text} is asynchronous");

        var (check, execution) = Start(runs, maxSteps, maxShrinks);
        check.Run(run => Synchronously(execution.Run(run)));
    }

    /// <summary>The asynchronous form of <see cref="Check"/>, which also runs asynchronous steps and invariants, one at a time and in order.</summary>
    /// <param name="runs">How many sequences to run; 0 runs none.</param>
    /// <param name="maxSteps">The most steps a sequence takes.</param>
    /// <param name="maxShrinks">How many simpler sequences shrinking may try; 0 reports the original one.</param>
    /// <param name="cancellationToken">Stops the check between runs.</param>
    /// <returns>A task that completes when every run passed, and faults with the <see cref="FakeCheckException{T}"/> when one failed.</returns>
    /// <exception cref="InvalidOperationException">There is no step.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="runs"/> or <paramref name="maxShrinks"/> is negative, or <paramref name="maxSteps"/> is not positive.</exception>
    public Task CheckAsync(int runs = 100, int maxSteps = 50, int maxShrinks = 1000, CancellationToken cancellationToken = default)
    {
        var (check, execution) = Start(runs, maxSteps, maxShrinks);
        return check.RunAsync(execution.Run, cancellationToken);
    }

    /// <summary>
    /// The property check of the runs: run <c>n</c> gets a faker seeded with <c>DeriveSeed(seed, n)</c> whose draws are on the run's tape until it ends, so the
    /// shrinker replays it from edited draws.
    /// </summary>
    private (PropertyCheck<StepRun<TState>> Check, StepExecution<TState> Execution) Start(int runs, int maxSteps, int maxShrinks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(runs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSteps);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShrinks);
        if (_steps.IsEmpty)
            throw new InvalidOperationException($"The {typeof(TState).Name} steps have nothing to run. Add one with .Step(\"name\", state => …).");

        var seed = CheckSeed.Choose(_seed, Pin);
        var data = LocaleStore.Get(_locale ?? Fake.Locale);
        var referenceDate = _referenceDate ?? Faker.DefaultSeededReferenceDate;

        StepRun<TState> OnTape(long index, ChoiceTape tape)
        {
            var faker = new Faker(data, new SeededRandom(SeededRandom.DeriveSeed(seed.Value, index)) { Tape = tape }) { ReferenceDate = referenceDate };
            return new StepRun<TState>(faker);
        }

        var check = new PropertyCheck<StepRun<TState>>(
            OnTape,
            OnTape,
            runs,
            maxShrinks,
            seed,
            "run",
            run => run.Describe(Environment.NewLine + "  "),
            (run, _) => run.Reason ?? "the run failed");
        return (check, new StepExecution<TState>(_create, _steps, _invariants, maxSteps));
    }

    /// <summary>The outcome of a run whose steps are all synchronous: complete already, except for a state that is only <see cref="IAsyncDisposable"/>.</summary>
    private static PropertyCheck<StepRun<TState>>.Outcome Synchronously(ValueTask<PropertyCheck<StepRun<TState>>.Outcome> outcome) =>
        outcome.IsCompleted ? outcome.Result : outcome.AsTask().GetAwaiter().GetResult();

    private static InvalidOperationException Asynchronous(string what) =>
        new($"{what}, and Check would not wait for it. Use await CheckAsync(…).");

    private StepCheck<TState> With(StepDefinition<TState> step) => new(_create, _steps.Add(step), _invariants, _seed, _locale, _referenceDate);

    private StepCheck<TState> With(StepInvariant<TState> invariant) => new(_create, _steps, _invariants.Add(invariant), _seed, _locale, _referenceDate);

    private static string NameOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    private static Func<Func<Faker, TState, TArg>> Ignoring<TArg>(Func<Faker, TArg> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        Func<Faker, TState, TArg> make = (faker, _) => argument(faker);
        return () => make;
    }

    private static Func<Func<Faker, TState, TArg>> Always<TArg>(Func<Faker, TState, TArg> argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return () => argument;
    }

    private static Func<Func<Faker, TState, TArg>> FromGenerator<TArg>(ObjectGenerator<TArg> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        NestedGenerators.RejectIfNotNestable(generator, nameof(generator));
        return () =>
        {
            var make = generator.StepArguments();
            return (faker, _) => make(faker);
        };
    }
}
