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

    /// <summary>Applies the locale, seed and clock of the options to <paramref name="generator"/>.</summary>
    public Generator<T> Apply<T>(Generator<T> generator)
    {
        if (options.Locale is { } locale)
            generator = generator.Locale(locale);
        if (options.Seed is { } seed)
            generator = generator.Seed(seed);
        if (options.ReferenceDate is { } date)
            generator = generator.ReferenceDate(date);
        else if (Clock is { } clock)
            generator = generator.TimeProvider(clock);
        return generator;
    }
}
