using NodaTime;

namespace Phoney.NodaTime;

/// <summary>Adds NodaTime values to <see cref="Faker"/> (and <see cref="IFaker"/>).</summary>
public static class NodaTimeFakerExtensions
{
    /// <summary>NodaTime values relative to the faker's <see cref="Faker.ReferenceDate"/>: <c>faker.Noda().Instant.Past()</c>.</summary>
    /// <example><code>LocalDate birthday = faker.Noda().LocalDate.Birthdate();</code></example>
    public static NodaTimeModule Noda(this IFaker faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        return new NodaTimeModule(faker);
    }
}

/// <summary>
/// NodaTime values (dates are UTC unless a zone is given). Each value is drawn like its built-in counterpart in
/// <see cref="Faker.Date"/>, so it follows <see cref="Faker.ReferenceDate"/>, <see cref="Faker.TimeProvider"/> and the seed.
/// A struct: getting it allocates nothing.
/// </summary>
public readonly struct NodaTimeModule
{
    private static readonly string[] ZoneIds = [.. DateTimeZoneProviders.Tzdb.Ids.Order(StringComparer.Ordinal)];
    private readonly IFaker _faker;

    internal NodaTimeModule(IFaker faker) => _faker = faker;

    /// <summary><see cref="global::NodaTime.Instant"/> values.</summary>
    public InstantValues Instant => new(_faker);

    /// <summary><see cref="global::NodaTime.LocalDate"/> values.</summary>
    public LocalDateValues LocalDate => new(_faker);

    /// <summary><see cref="global::NodaTime.LocalDateTime"/> values (UTC).</summary>
    public LocalDateTimeValues LocalDateTime => new(_faker);

    /// <summary><see cref="global::NodaTime.ZonedDateTime"/> values.</summary>
    public ZonedDateTimeValues ZonedDateTime => new(_faker);

    /// <summary>A random time of day, to the second.</summary>
    public LocalTime TimeOfDay() => global::NodaTime.LocalTime.FromTimeOnly(_faker.Date.TimeOfDay());

    /// <summary>A random time zone from the tz database.</summary>
    public DateTimeZone TimeZone() => DateTimeZoneProviders.Tzdb[ZoneIds[_faker.Random.Index(ZoneIds.Length)]];

    /// <summary>A duration between <paramref name="min"/> and <paramref name="max"/> (inclusive), to the tick.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public Duration Duration(Duration min, Duration max)
    {
        if (max < min)
            throw new ArgumentException($"max ({max}) must not be less than min ({min}).", nameof(max));
        return global::NodaTime.Duration.FromTicks(_faker.Random.Long(min.BclCompatibleTicks, max.BclCompatibleTicks));
    }

    /// <summary>A UTC offset of a real-world kind: between -12:00 and +14:00, in steps of 15 minutes.</summary>
    public Offset Offset() => global::NodaTime.Offset.FromSeconds(_faker.Random.Int(-48, 56) * 900);

    /// <summary>An interval inside the last <paramref name="years"/> years before the reference date.</summary>
    public Interval Interval(int years = 1) => Ordered(_faker.Date.Past(years), _faker.Date.Past(years));

    /// <summary>An interval inside <paramref name="from"/> and <paramref name="to"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public Interval Interval(Instant from, Instant to)
    {
        var (start, end) = (from.ToDateTimeOffset(), to.ToDateTimeOffset());
        return Ordered(_faker.Date.Between(start, end), _faker.Date.Between(start, end));
    }

    /// <summary>A date interval inside the last <paramref name="years"/> years before the reference date.</summary>
    public DateInterval DateInterval(int years = 1) => OrderedDates(Day(_faker.Date.Past(years)), Day(_faker.Date.Past(years)));

    /// <summary>A date interval inside <paramref name="from"/> and <paramref name="to"/> (both included).</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public DateInterval DateInterval(LocalDate from, LocalDate to)
    {
        var (start, end) = (from.ToDateOnly(), to.ToDateOnly());
        return OrderedDates(global::NodaTime.LocalDate.FromDateOnly(_faker.Date.Between(start, end)), global::NodaTime.LocalDate.FromDateOnly(_faker.Date.Between(start, end)));
    }

    /// <summary>A month inside the last <paramref name="years"/> years before the reference date.</summary>
    public YearMonth YearMonth(int years = 1)
    {
        var date = _faker.Date.Past(years).UtcDateTime;
        return new YearMonth(date.Year, date.Month);
    }

    /// <summary>A month from <paramref name="from"/> to <paramref name="to"/> (both included).</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public YearMonth YearMonth(YearMonth from, YearMonth to)
    {
        var date = _faker.Date.Between(from.ToDateInterval().Start.ToDateOnly(), to.ToDateInterval().End.ToDateOnly());
        return new YearMonth(date.Year, date.Month);
    }

    /// <summary>A day of a year that has no year, such as a birthday or a holiday; <c>February 29</c> is possible.</summary>
    public AnnualDate AnnualDate()
    {
        var month = _faker.Random.Int(1, 12);
        return new AnnualDate(month, _faker.Random.Int(1, DaysIn(month)));
    }

    private static int DaysIn(int month) => month switch
    {
        2 => 29,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    private static LocalDate Day(DateTimeOffset value) => global::NodaTime.LocalDate.FromDateTime(value.UtcDateTime);

    private static Interval Ordered(DateTimeOffset a, DateTimeOffset b) =>
        a <= b ? new Interval(global::NodaTime.Instant.FromDateTimeOffset(a), global::NodaTime.Instant.FromDateTimeOffset(b))
               : new Interval(global::NodaTime.Instant.FromDateTimeOffset(b), global::NodaTime.Instant.FromDateTimeOffset(a));

    private static DateInterval OrderedDates(LocalDate a, LocalDate b) => a <= b ? new DateInterval(a, b) : new DateInterval(b, a);

    /// <summary>A period of <paramref name="minDays"/> to <paramref name="maxDays"/> days.</summary>
    /// <exception cref="ArgumentException">The range is invalid.</exception>
    public Period Period(int minDays = 0, int maxDays = 30)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minDays);
        if (maxDays < minDays)
            throw new ArgumentException($"maxDays ({maxDays}) must not be less than minDays ({minDays}).", nameof(maxDays));
        return global::NodaTime.Period.FromDays(_faker.Random.Int(minDays, maxDays));
    }
}

/// <summary><see cref="Instant"/> values relative to the reference date.</summary>
public readonly struct InstantValues
{
    private readonly IFaker _faker;

    internal InstantValues(IFaker faker) => _faker = faker;

    /// <summary>Within one year before or after the reference date.</summary>
    public Instant Anytime() => Instant.FromDateTimeOffset(_faker.Date.Anytime());

    /// <summary>Up to <paramref name="years"/> years before the reference date.</summary>
    public Instant Past(int years = 1) => Instant.FromDateTimeOffset(_faker.Date.Past(years));

    /// <summary>Up to <paramref name="years"/> years after the reference date.</summary>
    public Instant Future(int years = 1) => Instant.FromDateTimeOffset(_faker.Date.Future(years));

    /// <summary>Within the last <paramref name="days"/> days.</summary>
    public Instant Recent(int days = 1) => Instant.FromDateTimeOffset(_faker.Date.Recent(days));

    /// <summary>Within the next <paramref name="days"/> days.</summary>
    public Instant Soon(int days = 1) => Instant.FromDateTimeOffset(_faker.Date.Soon(days));

    /// <summary>Between <paramref name="from"/> and <paramref name="to"/> (inclusive).</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public Instant Between(Instant from, Instant to) => Instant.FromDateTimeOffset(_faker.Date.Between(from.ToDateTimeOffset(), to.ToDateTimeOffset()));
}

/// <summary><see cref="LocalDate"/> values relative to the reference date (UTC).</summary>
public readonly struct LocalDateValues
{
    private readonly IFaker _faker;

    internal LocalDateValues(IFaker faker) => _faker = faker;

    /// <summary>Up to <paramref name="years"/> years before the reference date.</summary>
    public LocalDate Past(int years = 1) => Date(_faker.Date.Past(years));

    /// <summary>Up to <paramref name="years"/> years after the reference date.</summary>
    public LocalDate Future(int years = 1) => Date(_faker.Date.Future(years));

    /// <summary>Within the last <paramref name="days"/> days.</summary>
    public LocalDate Recent(int days = 1) => Date(_faker.Date.Recent(days));

    /// <summary>Within the next <paramref name="days"/> days.</summary>
    public LocalDate Soon(int days = 1) => Date(_faker.Date.Soon(days));

    /// <summary>Between <paramref name="from"/> and <paramref name="to"/> (inclusive).</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public LocalDate Between(LocalDate from, LocalDate to) => LocalDate.FromDateOnly(_faker.Date.Between(from.ToDateOnly(), to.ToDateOnly()));

    /// <summary>A date of birth for an age between <paramref name="minAge"/> and <paramref name="maxAge"/> at the reference date.</summary>
    public LocalDate Birthdate(int minAge = 18, int maxAge = 80) => LocalDate.FromDateOnly(_faker.Date.Birthdate(minAge, maxAge));

    private static LocalDate Date(DateTimeOffset value) => LocalDate.FromDateTime(value.UtcDateTime);
}

/// <summary><see cref="LocalDateTime"/> values relative to the reference date (UTC).</summary>
public readonly struct LocalDateTimeValues
{
    private readonly IFaker _faker;

    internal LocalDateTimeValues(IFaker faker) => _faker = faker;

    /// <summary>Up to <paramref name="years"/> years before the reference date.</summary>
    public LocalDateTime Past(int years = 1) => Local(_faker.Date.Past(years));

    /// <summary>Up to <paramref name="years"/> years after the reference date.</summary>
    public LocalDateTime Future(int years = 1) => Local(_faker.Date.Future(years));

    /// <summary>Within the last <paramref name="days"/> days.</summary>
    public LocalDateTime Recent(int days = 1) => Local(_faker.Date.Recent(days));

    /// <summary>Within the next <paramref name="days"/> days.</summary>
    public LocalDateTime Soon(int days = 1) => Local(_faker.Date.Soon(days));

    /// <summary>Between <paramref name="from"/> and <paramref name="to"/> (inclusive), both read as UTC.</summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public LocalDateTime Between(LocalDateTime from, LocalDateTime to) =>
        Local(_faker.Date.Between(from.InUtc().ToDateTimeOffset(), to.InUtc().ToDateTimeOffset()));

    private static LocalDateTime Local(DateTimeOffset value) => LocalDateTime.FromDateTime(value.UtcDateTime);
}

/// <summary><see cref="ZonedDateTime"/> values relative to the reference date, in a given zone (default UTC).</summary>
public readonly struct ZonedDateTimeValues
{
    private readonly IFaker _faker;

    internal ZonedDateTimeValues(IFaker faker) => _faker = faker;

    /// <summary>Up to <paramref name="years"/> years before the reference date.</summary>
    public ZonedDateTime Past(int years = 1, DateTimeZone? zone = null) => In(_faker.Date.Past(years), zone);

    /// <summary>Up to <paramref name="years"/> years after the reference date.</summary>
    public ZonedDateTime Future(int years = 1, DateTimeZone? zone = null) => In(_faker.Date.Future(years), zone);

    /// <summary>Within the last <paramref name="days"/> days.</summary>
    public ZonedDateTime Recent(int days = 1, DateTimeZone? zone = null) => In(_faker.Date.Recent(days), zone);

    /// <summary>Within the next <paramref name="days"/> days.</summary>
    public ZonedDateTime Soon(int days = 1, DateTimeZone? zone = null) => In(_faker.Date.Soon(days), zone);

    private static ZonedDateTime In(DateTimeOffset value, DateTimeZone? zone) => Instant.FromDateTimeOffset(value).InZone(zone ?? DateTimeZone.Utc);
}
