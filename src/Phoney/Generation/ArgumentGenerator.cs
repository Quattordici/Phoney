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
    private readonly GenerationSettings _settings;
    private readonly long _randomSeed = System.Random.Shared.NextInt64();
    private long _next;

    /// <summary>Creates a generator for <paramref name="arguments"/>.</summary>
    public ArgumentGenerator(IEnumerable<FakeArgument> arguments)
        : this([.. arguments ?? throw new ArgumentNullException(nameof(arguments))], null, null, null, GenerationSettings.Default, null)
    {
    }

    private ArgumentGenerator(
        ImmutableArray<FakeArgument> arguments,
        string? locale,
        long? seed,
        DateTimeOffset? referenceDate,
        GenerationSettings settings,
        Func<FakeScope, object?>[]? producers)
    {
        _arguments = arguments;
        _locale = locale;
        _seed = seed;
        _referenceDate = referenceDate;
        _settings = settings;
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
        faker.Random.Reseed(Randomizer.DeriveSeed(_seed ?? _randomSeed, index));
        var scope = new FakeScope(faker, _settings, null, index, typeof(ArgumentList));
        var row = new object?[_producers.Length];
        for (var i = 0; i < row.Length; i++)
            row[i] = _producers[i](scope);
        return row;
    }

    private Faker CreateFaker()
    {
        var faker = new Faker(LocaleStore.Get(_locale ?? Fake.Locale), new Randomizer(0));
        if ((_referenceDate ?? (_seed is not null ? Faker.DefaultSeededReferenceDate : null)) is { } date)
            faker.ReferenceDate = date;
        return faker;
    }

    private ArgumentGenerator With(string? locale = null, long? seed = null, DateTimeOffset? referenceDate = null, GenerationSettings? settings = null) =>
        new(_arguments, locale ?? _locale, seed ?? _seed, referenceDate ?? _referenceDate, settings ?? _settings, _producers);

    /// <summary>
    /// The value producer for one argument, chosen exactly like for an object member: a custom convention
    /// (<see cref="Fake.Conventions"/>), else a format annotation or the name convention, with the annotations applied.
    /// </summary>
    private static Func<FakeScope, object?> CreateProducer(FakeArgument argument)
    {
        var constraints = argument.Attributes is { } attributes ? MemberConstraints.Read(attributes) : null;
        var name = ReflectionTypeModel.Capitalize(argument.Name);
        var category = FakeMember.ScalarCategory(argument.Type);
        var convention = constraints?.Format ?? ConventionRules.Match(name, category, "");

        var member = new FakeMember(name, argument.Type, convention, constraints: constraints);
        if (FakeConventions.All.FirstOrDefault(c => c.Matches(member)) is { } custom)
            return custom.Factory;

        return ReflectionTypeModel.Producer(argument.Type, name, convention, argument.Nullable && constraints?.Required != true, constraints);
    }

    /// <summary>Scope type of a row; never equal to a real type, so arguments of any type are not mistaken for cycles.</summary>
    private sealed class ArgumentList;
}
