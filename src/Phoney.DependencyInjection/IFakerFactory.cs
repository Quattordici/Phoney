namespace Phoney.DependencyInjection;

/// <summary>Creates <see cref="Faker"/> instances configured by <see cref="PhoneyOptions"/>; thread-safe.</summary>
public interface IFakerFactory
{
    /// <summary>Creates a faker; <paramref name="locale"/> and <paramref name="seed"/> override the options.</summary>
    /// <param name="locale">Locale code; default <see cref="PhoneyOptions.Locale"/>, then <see cref="Fake.Locale"/>.</param>
    /// <param name="seed">Seed; default <see cref="PhoneyOptions.Seed"/>.</param>
    Faker Create(string? locale = null, long? seed = null);
}

/// <summary>Applies <see cref="PhoneyOptions"/> to fakers and generators.</summary>
internal sealed class FakerFactory(PhoneyOptions options, TimeProvider? containerTime) : IFakerFactory
{
    /// <summary>The clock to use: the configured one, else a non-system one from the container.</summary>
    private TimeProvider? Clock => options.TimeProvider ?? (ReferenceEquals(containerTime, TimeProvider.System) ? null : containerTime);

    /// <inheritdoc />
    public Faker Create(string? locale = null, long? seed = null)
    {
        var faker = new Faker(locale ?? options.Locale ?? Fake.Locale, seed ?? options.Seed);
        if (options.ReferenceDate is { } date)
            faker.ReferenceDate = date;
        else
            faker.TimeProvider = Clock;
        return faker;
    }

    /// <summary>Returns a generator with the configured locale, seed, clock and generation settings applied.</summary>
    /// <remarks>Unset generation options preserve the generator's settings. A single collection-size bound keeps the other one the generator has, or else <see cref="Fake.Defaults"/>, moved only when the two would cross.</remarks>
    public ObjectGenerator<T> Apply<T>(ObjectGenerator<T> generator)
    {
        if (options.Locale is { } locale)
            generator = generator.Locale(locale);
        if (options.Seed is { } seed)
            generator = generator.Seed(seed);
        if (options.ReferenceDate is { } date)
            generator = generator.ReferenceDate(date);
        else if (Clock is { } clock)
            generator = generator.TimeProvider(clock);
        if (options.NullProbability is { } nullProbability)
            generator = generator.NullProbability(nullProbability);
        if (options.MinCollectionSize is { } min)
            generator = generator.MinCollectionSize(min);
        if (options.MaxCollectionSize is { } max)
            generator = generator.MaxCollectionSize(max);

        if (options.MaxDepth is { } depth)
            generator = generator.MaxDepth(depth);
        if (options.Strict)
            generator = generator.Strict();
        return generator;
    }
}
