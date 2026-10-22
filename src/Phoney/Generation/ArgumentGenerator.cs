using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Phoney.Data;

namespace Phoney.Generation;

/// <summary>
/// Generates rows of values for a list of arguments, such as the parameters of a test method. Each argument is
/// filled like a member of one object: by name convention and type, honouring data annotations, with complex types
/// populated recursively. All arguments of a row describe the same person, place and timeline, so
/// <c>(string firstName, string email, DateOnly birthDate, int age)</c> agree with each other.
/// </summary>
/// <remarks>
/// This is the building block for test framework integrations such as <c>Phoney.TUnit</c>. Generators are
/// immutable; every configuration method returns a new generator. With a <see cref="Seed"/>, row <c>n</c> is always
/// the same.
/// </remarks>
/// <example>
/// <code>
/// var rows = Fake.Arguments(typeof(OrderTests).GetMethod("Places_order")!).Seed(1).Generate(5);
/// </code>
/// </example>
[RequiresUnreferencedCode("Populates arguments with reflection.")]
[RequiresDynamicCode("Populates arguments with reflection.")]
public sealed class ArgumentGenerator
{
    private readonly ImmutableArray<FakeArgument> _arguments;
    private readonly Func<FakeScope, object?>[] _producers;
    private readonly string? _locale;
    private readonly long? _seed;
    private readonly DateTimeOffset? _referenceDate;
    private readonly TimeProvider? _timeProvider;
    private readonly GenerationSettings _settings;
    private readonly string _replayHint;
#pragma warning disable RS0030 // Base seed of an unseeded generator; a configured seed replaces it.
    private readonly long _randomSeed = System.Random.Shared.NextInt64();
#pragma warning restore RS0030
    private long _next;

    /// <summary>Creates a generator for <paramref name="arguments"/>.</summary>
    public ArgumentGenerator(IEnumerable<FakeArgument> arguments)
        : this([.. arguments ?? throw new ArgumentNullException(nameof(arguments))], null, null, null, null, GenerationSettings.Default with { Subtypes = FakeSubtypes.All, Conventions = NestedConventions.For(FakeConventions.All), TypeGenerators = FakeGenerators.All.IsEmpty ? null : FakeGenerators.All }, CheckSeed.GeneratorPin, null)
    {
    }

#pragma warning disable S107 // the private copy constructor of an immutable generator takes every field
    private ArgumentGenerator(
        ImmutableArray<FakeArgument> arguments,
        string? locale,
        long? seed,
        DateTimeOffset? referenceDate,
        TimeProvider? timeProvider,
        GenerationSettings settings,
        string replayHint,
        Func<FakeScope, object?>[]? producers)
#pragma warning restore S107
    {
        _arguments = arguments;
        _locale = locale;
        _seed = seed;
        _referenceDate = referenceDate;
        _timeProvider = timeProvider;
        _settings = settings;
        _replayHint = replayHint;
        _producers = producers ?? [.. arguments.Select(CreateProducer)];
    }

    /// <summary>The arguments each row contains, in order.</summary>
    public IReadOnlyList<FakeArgument> Arguments => _arguments;

    /// <summary>Uses data from <paramref name="locale"/> (default: <see cref="Fake.Locale"/>).</summary>
    public ArgumentGenerator Locale(string locale) => With(locale: Locales.Get(locale).Code);

    /// <summary>
    /// Makes the rows reproducible: row <c>n</c> depends only on the seed. Unless a <see cref="ReferenceDate"/> is
    /// set, dates are relative to <see cref="Faker.DefaultSeededReferenceDate"/>, so they don't drift with the clock.
    /// </summary>
    public ArgumentGenerator Seed(long seed) => With(seed: seed);

    /// <summary>The point in time relative dates (past, recent, birthdates, ages) are computed from.</summary>
    public ArgumentGenerator ReferenceDate(DateTimeOffset referenceDate) => With(referenceDate: referenceDate);

    /// <summary>Reads "now" for relative dates from <paramref name="timeProvider"/>, also when seeded; an explicit <see cref="ReferenceDate"/> takes precedence.</summary>
    public ArgumentGenerator TimeProvider(TimeProvider timeProvider) =>
        With(timeProvider: timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)));

    /// <summary>Probability (0–1) that nullable arguments (<c>int?</c>, <c>string?</c>…) are <see langword="null"/>. Default 0.</summary>
    public ArgumentGenerator NullProbability(double probability)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(probability);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1);
        return With(settings: _settings with { NullProbability = probability });
    }

    /// <summary>Sets how many elements generated collections get. Default 1–3.</summary>
    public ArgumentGenerator CollectionSize(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return With(settings: _settings with { MinCollectionSize = min, MaxCollectionSize = max });
    }

    /// <summary>
    /// How the report of a failed <see cref="Check(Func{object?[], bool}, int, int)"/> says to pin its seed, as a composite format whose <c>{0}</c> is the
    /// seed. Test framework integrations name their own attribute here (<c>[FakeCheck(Seed = {0})]</c>). Default: <c>Seed({0}) on the generator</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="format"/> is empty or has no <c>{0}</c>.</exception>
    public ArgumentGenerator ReplayHint(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        if (!format.Contains("{0}", StringComparison.Ordinal))
            throw new ArgumentException($"The replay hint '{format}' has no {{0}} for the seed, e.g. \"[FakeCheck(Seed = {{0}})]\".", nameof(format));
        return With(replayHint: format);
    }

    /// <summary>Leaves the way to pin the seed out of failure reports, for callers that have none (<c>Fake.Check</c>); <c>PHONEY_SEED</c> still replays.</summary>
    internal ArgumentGenerator WithoutReplayHint() => With(replayHint: "");

    /// <summary>
    /// Checks that <paramref name="property"/> holds for <paramref name="count"/> rows, property-based testing style, like
    /// <see cref="ObjectGenerator{T}.Check(Func{T, bool}, int, int)"/>: a failing row is <em>shrunk</em> to the simplest row the generator can make that still
    /// fails, and a <see cref="FakeCheckException{T}"/> of <c>object?[]</c> reports it with the arguments by name (<c>order = Order { … }, discount = 11</c>).
    /// </summary>
    /// <remarks>
    /// The seed is this generator's <see cref="Seed"/>, else <c>PHONEY_SEED</c>, else a new random one that the failure reports; the rows are those
    /// <see cref="Row(long)"/> makes for that seed from 0. The values of a shrunk row still agree with each other and honour their annotations.
    /// </remarks>
    /// <param name="property">What must hold for every row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    public void Check(Func<object?[], bool> property, int count = 100, int maxShrinks = 1000)
    {
        ArgumentNullException.ThrowIfNull(property);
        StartCheck(count, maxShrinks).Run(row => PropertyCheck<object?[]>.Outcome.Of(() => property(row)));
    }

    /// <summary>
    /// Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> rows, so assertions of any test framework can be the property;
    /// a failing row is shrunk as for <see cref="Check(Func{object?[], bool}, int, int)"/>, and what the property threw is the
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <param name="property">The assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    public void Check(Action<object?[]> property, int count = 100, int maxShrinks = 1000)
    {
        ArgumentNullException.ThrowIfNull(property);
        StartCheck(count, maxShrinks).Run(row => PropertyCheck<object?[]>.Outcome.Of(() => property(row)));
    }

    /// <summary>The asynchronous form of <see cref="Check(Action{object?[]}, int, int)"/>: a row fails when the property's task faults.</summary>
    /// <param name="property">The asynchronous assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <param name="cancellationToken">Stops the check between rows.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task CheckAsync(Func<object?[], Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(property);
        return StartCheck(count, maxShrinks).RunAsync(property, cancellationToken);
    }

    /// <summary>
    /// Prepares a check on a copy of this generator with the check's seed (so dates are relative to the seeded reference date and the rows are those of
    /// 0, 1, 2…); a row is made from a tape by one faker, as <see cref="Row(Faker, long)"/> makes it.
    /// </summary>
    private PropertyCheck<object?[]> StartCheck(int count, int maxShrinks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(maxShrinks);
        var seed = CheckSeed.Choose(_seed, _replayHint);
        var checking = With(seed: seed.Value);
        Faker? faker = null;
        object?[] OnTape(long index, ChoiceTape tape)
        {
            faker ??= checking.CreateFaker();
            faker.Random.Tape = tape;
            try
            {
                return checking.Row(faker, index);
            }
            finally
            {
                faker.Random.Tape = null;
            }
        }

        return new PropertyCheck<object?[]>(OnTape, OnTape, count, maxShrinks, seed, "row", Describe);
    }

    /// <summary>A row on one line, each value after its argument's name: <c>order = Order { … }, discount = 11</c>.</summary>
    private string Describe(object?[] row) =>
        string.Join(", ", row.Select((value, i) => $"{_arguments[i].Name} = {ObjectDescription.Of(value)}"));

    /// <summary>Generates one row: a value per argument.</summary>
    public object?[] Generate()
    {
        var faker = CreateFaker();
        return Row(faker, Interlocked.Increment(ref _next) - 1);
    }

    /// <summary>Generates <paramref name="count"/> rows.</summary>
    public List<object?[]> Generate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var faker = CreateFaker();
        var first = Interlocked.Add(ref _next, count) - count;
        var rows = new List<object?[]>(count);
        for (var i = 0; i < count; i++)
            rows.Add(Row(faker, first + i));
        return rows;
    }

    /// <summary>
    /// Generates row <paramref name="index"/> without advancing the sequence. With a <see cref="Seed"/> it is always
    /// the same row, which lets callers recreate a row on demand (e.g. a test framework retrying a test).
    /// </summary>
    public object?[] Row(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Row(CreateFaker(), index);
    }

    /// <summary>Row <paramref name="index"/>: one scope for all arguments, so they share identity and timeline.</summary>
    private object?[] Row(Faker faker, long index)
    {
        faker.Random.Reseed(SeededRandom.DeriveSeed(_seed ?? _randomSeed, index));
        var scope = new FakeScope(faker, _settings, null, index, typeof(ArgumentList));
        var row = new object?[_producers.Length];
        for (var i = 0; i < row.Length; i++)
            row[i] = _producers[i](scope);
        return row;
    }

    private Faker CreateFaker()
    {
        var faker = new Faker(LocaleStore.Get(_locale ?? Fake.Locale), new SeededRandom(0)) { TimeProvider = _referenceDate is null ? _timeProvider : null };
        if (faker.TimeProvider is null && (_referenceDate ?? (_seed is not null ? Faker.DefaultSeededReferenceDate : null)) is { } date)
            faker.ReferenceDate = date;
        return faker;
    }

    private ArgumentGenerator With(string? locale = null, long? seed = null, DateTimeOffset? referenceDate = null, TimeProvider? timeProvider = null, GenerationSettings? settings = null, string? replayHint = null) =>
        new(_arguments, locale ?? _locale, seed ?? _seed, referenceDate ?? _referenceDate, timeProvider ?? _timeProvider, settings ?? _settings, replayHint ?? _replayHint, _producers);

    /// <summary>
    /// The value producer for one argument, chosen exactly like for an object member: a custom convention
    /// (<see cref="Fake.Conventions"/>), else a format annotation or the name convention, with the annotations applied.
    /// </summary>
    private static Func<FakeScope, object?> CreateProducer(FakeArgument argument)
    {
        var constraints = argument.Attributes is { } attributes ? MemberConstraints.Read(attributes) : null;
        var name = ReflectionTypeModel.Capitalize(argument.Name);
        var category = FakeMember.ConventionCategory(argument.Type);
        var convention = constraints?.Format ?? ConventionRules.Match(name, category, "");

        var member = new FakeMember(name, argument.Type, convention) { Constraints = constraints };
        if (FakeConventions.All.FirstOrDefault(c => c.Matches(member)) is { } custom)
            return custom.Factory;

        return ReflectionTypeModel.Producer(argument.Type, name, convention, argument.Nullable && constraints?.Required != true, constraints);
    }

#pragma warning disable S2094 // a marker type for the scope of a row; its name shows in messages
    /// <summary>Scope type of a row; never equal to a real type, so arguments of any type are not mistaken for cycles.</summary>
    private sealed class ArgumentList;
#pragma warning restore S2094
}
