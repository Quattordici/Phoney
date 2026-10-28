namespace Phoney.Generation;

/// <summary>
/// The "now" a generator gives the Fakers of its runs: an explicit reference date, else a clock. What a Faker does without either (the default seeded
/// date, the system clock) is its own <see cref="Faker.ReferenceDate"/>; this only decides what to hand it, in one place for every generator.
/// </summary>
/// <param name="Date">The explicit reference date, which wins over everything else.</param>
/// <param name="Clock">The clock "now" is read from when there is no explicit date.</param>
internal readonly record struct RunClock(DateTimeOffset? Date, TimeProvider? Clock)
{
    /// <summary>
    /// Sets <paramref name="faker"/> up: the explicit date; else the clock; else the date of <paramref name="parent"/> (a nested generator);
    /// else, when <paramref name="seeded"/>, <see cref="Faker.DefaultSeededReferenceDate"/> so relative dates don't drift with the clock; else nothing,
    /// and the faker reads the system clock.
    /// </summary>
    /// <param name="faker">The faker of a run.</param>
    /// <param name="parent">The faker of the object this run is nested in, or <see langword="null"/>.</param>
    /// <param name="seeded">Whether the generator is seeded (or its dates must be stable anyway).</param>
    public void Apply(Faker faker, Faker? parent, bool seeded)
    {
        if (Date is null && Clock is { } clock)
            faker.TimeProvider = clock;
        else if ((Date ?? parent?.ReferenceDate ?? (seeded ? Faker.DefaultSeededReferenceDate : null)) is { } date)
            faker.ReferenceDate = date;
    }
}
