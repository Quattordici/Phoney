using Phoney.Data;

namespace Phoney.Modules;

/// <summary>
/// Dates and times (faker.js <c>date</c>). Relative methods are computed from <see cref="Phoney.Faker.ReferenceDate"/>
/// (or an explicit <c>referenceDate</c>) and return UTC values with millisecond precision.
/// </summary>
public sealed class DateModule : FakerModule
{
    internal DateModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a date within one year before or after the reference date.</summary>
    public DateTimeOffset Anytime(DateTimeOffset? referenceDate = null)
    {
        var reference = Reference(referenceDate);
        return Between(reference.AddDays(-365), reference.AddDays(365));
    }

    /// <summary>Returns a date in the past, up to <paramref name="years"/> years before the reference date.</summary>
    public DateTimeOffset Past(int years = 1, DateTimeOffset? referenceDate = null) => Past(0, years, referenceDate);

    /// <summary>Returns a date between <paramref name="minYears"/> and <paramref name="maxYears"/> years before the reference date.</summary>
    public DateTimeOffset Past(int minYears, int maxYears, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(minYears, maxYears, "years");
        var reference = Reference(referenceDate);
        return Between(reference.AddYears(-maxYears), reference.AddYears(-minYears).AddSeconds(-1));
    }

    /// <summary>Returns a date in the future, up to <paramref name="years"/> years after the reference date.</summary>
    public DateTimeOffset Future(int years = 1, DateTimeOffset? referenceDate = null) => Future(0, years, referenceDate);

    /// <summary>Returns a date between <paramref name="minYears"/> and <paramref name="maxYears"/> years after the reference date.</summary>
    public DateTimeOffset Future(int minYears, int maxYears, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(minYears, maxYears, "years");
        var reference = Reference(referenceDate);
        return Between(reference.AddYears(minYears).AddSeconds(1), reference.AddYears(maxYears));
    }

    /// <summary>Returns a date within the last <paramref name="days"/> days.</summary>
    public DateTimeOffset Recent(int days = 1, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(0, days, "days");
        var reference = Reference(referenceDate);
        return Between(reference.AddDays(-days), reference.AddSeconds(-1));
    }

    /// <summary>Returns a date within the next <paramref name="days"/> days.</summary>
    public DateTimeOffset Soon(int days = 1, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(0, days, "days");
        var reference = Reference(referenceDate);
        return Between(reference.AddSeconds(1), reference.AddDays(days));
    }

    /// <summary>Returns a date between <paramref name="from"/> and <paramref name="to"/> (inclusive).</summary>
    public DateTimeOffset Between(DateTimeOffset from, DateTimeOffset to)
    {
        var fromMs = from.ToUnixTimeMilliseconds();
        var toMs = to.ToUnixTimeMilliseconds();
        if (fromMs > toMs)
            throw new ArgumentException("'from' must be before 'to'.", nameof(from));
        return DateTimeOffset.FromUnixTimeMilliseconds(Random.Long(fromMs, toMs));
    }

    /// <summary>Returns <paramref name="count"/> sorted dates between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public DateTimeOffset[] Betweens(DateTimeOffset from, DateTimeOffset to, int count = 3)
    {
        var dates = new DateTimeOffset[Math.Max(0, count)];
        for (var i = 0; i < dates.Length; i++)
            dates[i] = Between(from, to);
        Array.Sort(dates);
        return dates;
    }

    /// <summary>Returns a date between <paramref name="from"/> and <paramref name="to"/> (inclusive), without time of day.</summary>
    public DateOnly Between(DateOnly from, DateOnly to)
    {
        if (from > to)
            throw new ArgumentException("'from' must be before 'to'.", nameof(from));
        return DateOnly.FromDayNumber(Random.Int(from.DayNumber, to.DayNumber));
    }

    /// <summary>Returns a random time of day with second precision.</summary>
    public TimeOnly TimeOfDay() => new(Random.Long(0, (TimeSpan.TicksPerDay / TimeSpan.TicksPerSecond) - 1) * TimeSpan.TicksPerSecond);

    /// <summary>
    /// Returns the birthdate of someone between <paramref name="minAge"/> and <paramref name="maxAge"/> years old
    /// at the reference date (default 18–80).
    /// </summary>
    public DateOnly Birthdate(int minAge = 18, int maxAge = 80, DateTimeOffset? referenceDate = null)
    {
        if (maxAge < minAge)
            throw new ArgumentException($"Max age {maxAge} must be greater than or equal to min age {minAge}.", nameof(maxAge));
        var today = DateOnly.FromDateTime(Reference(referenceDate).UtcDateTime);
        // Oldest: turns maxAge+1 tomorrow; youngest: turned minAge today.
        return Between(today.AddYears(-maxAge - 1).AddDays(1), today.AddYears(-minAge));
    }

    /// <summary>Returns a birthdate in a year between <paramref name="minYear"/> and <paramref name="maxYear"/>.</summary>
    public DateOnly BirthdateInYears(int minYear, int maxYear)
    {
        if (maxYear < minYear)
            throw new ArgumentException($"Max year {maxYear} must be greater than or equal to min year {minYear}.", nameof(maxYear));
        return Between(new DateOnly(minYear, 1, 2), new DateOnly(maxYear, 12, 30));
    }

    /// <summary>Returns a month name in the locale's language, e.g. <c>January</c>.</summary>
    /// <param name="abbreviated">Return the short form, e.g. <c>Jan</c>.</param>
    /// <param name="context">Use the grammatical form for use inside a date, where the language has one.</param>
    public string Month(bool abbreviated = false, bool context = false) => Faker.Pick(DateNameKey(
        abbreviated, context, DataKeys.DateMonthWide, DataKeys.DateMonthWideContext, DataKeys.DateMonthAbbr, DataKeys.DateMonthAbbrContext));

    /// <summary>Returns a weekday name in the locale's language, e.g. <c>Monday</c>.</summary>
    /// <inheritdoc cref="Month" path="/param"/>
    public string Weekday(bool abbreviated = false, bool context = false) => Faker.Pick(DateNameKey(
        abbreviated, context, DataKeys.DateWeekdayWide, DataKeys.DateWeekdayWideContext, DataKeys.DateWeekdayAbbr, DataKeys.DateWeekdayAbbrContext));

    /// <summary>Returns an IANA time zone, e.g. <c>America/New_York</c>.</summary>
    public string TimeZone() => Faker.Pick(DataKeys.DateTimeZone);

    /// <summary>Uses the context form when requested and available, like faker.js.</summary>
    private int DateNameKey(bool abbreviated, bool context, int wide, int wideContext, int abbr, int abbrContext)
    {
        if (abbreviated)
            return context && Faker.Data.Has(abbrContext) ? abbrContext : abbr;
        return context && Faker.Data.Has(wideContext) ? wideContext : wide;
    }

    /// <summary>The explicit reference date or the faker's, in UTC.</summary>
    private DateTimeOffset Reference(DateTimeOffset? referenceDate) => (referenceDate ?? Faker.ReferenceDate).ToUniversalTime();

    /// <summary>Validates a relative range like faker.js: max must be positive and greater than min.</summary>
    private static void ValidateRange(int min, int max, string unit)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), max, $"The number of {unit} must be greater than 0.");
        if (min >= max)
            throw new ArgumentException($"The maximum number of {unit} must be greater than the minimum.", nameof(max));
    }
}
