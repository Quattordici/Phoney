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

    /// <summary>Returns a date within one year before or after the reference date (stopping at the ends of the calendar).</summary>
    public DateTimeOffset Anytime(DateTimeOffset? referenceDate = null)
    {
        var reference = Reference(referenceDate);
        return Between(AddDays(reference, -365) ?? DateTimeOffset.MinValue, AddDays(reference, 365) ?? DateTimeOffset.MaxValue);
    }

    /// <summary>Returns a date in the past, up to <paramref name="years"/> years before the reference date (no earlier than year 1).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="years"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The reference date is the first moment of the calendar, so nothing lies before it.</exception>
    public DateTimeOffset Past(int years = 1, DateTimeOffset? referenceDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(years);
        return Past(0, years, referenceDate);
    }

    /// <summary>Returns a date between <paramref name="minYears"/> and <paramref name="maxYears"/> years before the reference date (no earlier than year 1).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minYears"/> is negative or <paramref name="maxYears"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxYears"/> is not greater than <paramref name="minYears"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="minYears"/> years before the reference date is before year 1.</exception>
    public DateTimeOffset Past(int minYears, int maxYears, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(minYears, maxYears);
        var reference = Reference(referenceDate);
        var bounds = Volatile.Read(ref s_past);
        if (bounds is null || bounds.Reference != reference || bounds.Min != minYears || bounds.Max != maxYears)
        {
            // The calendar arithmetic is done once per reference date and range (objects ask for the same past year again and again); the draw is Between's.
            var latest = AddYears(reference, -minYears) is { } end ? AddSeconds(end, -1) : null;
            var from = (AddYears(reference, -maxYears) ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds();
            var to = (latest ?? throw NoDate(minYears, "years", "before", reference)).ToUnixTimeMilliseconds();
            // ValidateRange keeps minYears below maxYears, so from lies at least a year before to.
            bounds = new PastBounds(reference, minYears, maxYears, from, to);
            Volatile.Write(ref s_past, bounds);
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(Random.Long(bounds.FromMs, bounds.ToMs));
    }

    // Shared by every faker (the bounds depend on their inputs only, and the record is immutable), so a new faker with the usual reference date doesn't redo them.
    private static PastBounds? s_past;

    /// <summary>The range <see cref="Past(int, int, DateTimeOffset?)"/> last drew from, in Unix milliseconds.</summary>
    private sealed record PastBounds(DateTimeOffset Reference, int Min, int Max, long FromMs, long ToMs);

    /// <summary>Returns a date in the future, up to <paramref name="years"/> years after the reference date (no later than year 9999).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="years"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The reference date is the last moment of the calendar, so nothing lies after it.</exception>
    public DateTimeOffset Future(int years = 1, DateTimeOffset? referenceDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(years);
        return Future(0, years, referenceDate);
    }

    /// <summary>Returns a date between <paramref name="minYears"/> and <paramref name="maxYears"/> years after the reference date (no later than year 9999).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minYears"/> is negative or <paramref name="maxYears"/> is not positive.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxYears"/> is not greater than <paramref name="minYears"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="minYears"/> years after the reference date is after year 9999.</exception>
    public DateTimeOffset Future(int minYears, int maxYears, DateTimeOffset? referenceDate = null)
    {
        ValidateRange(minYears, maxYears);
        var reference = Reference(referenceDate);
        var earliest = AddYears(reference, minYears) is { } start ? AddSeconds(start, 1) : null;
        return Between(earliest ?? throw NoDate(minYears, "years", "after", reference), AddYears(reference, maxYears) ?? DateTimeOffset.MaxValue);
    }

    /// <summary>Returns a date within the last <paramref name="days"/> days (no earlier than year 1).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The reference date is the first moment of the calendar, so nothing lies before it.</exception>
    public DateTimeOffset Recent(int days = 1, DateTimeOffset? referenceDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var reference = Reference(referenceDate);
        return Between(AddDays(reference, -days) ?? DateTimeOffset.MinValue, AddSeconds(reference, -1) ?? throw NoDate(0, "days", "before", reference));
    }

    /// <summary>Returns a date within the next <paramref name="days"/> days (no later than year 9999).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The reference date is the last moment of the calendar, so nothing lies after it.</exception>
    public DateTimeOffset Soon(int days = 1, DateTimeOffset? referenceDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var reference = Reference(referenceDate);
        return Between(AddSeconds(reference, 1) ?? throw NoDate(0, "days", "after", reference), AddDays(reference, days) ?? DateTimeOffset.MaxValue);
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
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var dates = new DateTimeOffset[count];
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
    /// <remarks>Ages reaching back before year 1 start at <see cref="DateOnly.MinValue"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minAge"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxAge"/> is less than <paramref name="minAge"/>.</exception>
    /// <exception cref="InvalidOperationException">Nobody born in year 1 or later is <paramref name="minAge"/> at the reference date.</exception>
    public DateOnly Birthdate(int minAge = 18, int maxAge = 80, DateTimeOffset? referenceDate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minAge);
        if (maxAge < minAge)
            throw new ArgumentException($"Max age {maxAge} must be greater than or equal to min age {minAge}.", nameof(maxAge));
        var today = DateOnly.FromDateTime(Reference(referenceDate).UtcDateTime);
        // Oldest: turns maxAge+1 tomorrow; youngest: turned minAge today.
        var oldest = today.Year - (long)maxAge - 1 >= 1 ? today.AddYears(-maxAge - 1).AddDays(1) : DateOnly.MinValue;
        var youngest = today.Year - (long)minAge >= 1
            ? today.AddYears(-minAge)
            : throw new InvalidOperationException($"Nobody born in year 1 or later is {minAge} years old on {today:O}: pass a smaller minAge or a later reference date.");
        return Between(oldest, youngest);
    }

    /// <summary>Returns a birthdate in a year between <paramref name="minYear"/> and <paramref name="maxYear"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minYear"/> is less than 1 or <paramref name="maxYear"/> more than 9999.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxYear"/> is less than <paramref name="minYear"/>.</exception>
    public DateOnly BirthdateInYears(int minYear, int maxYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minYear, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxYear, 9999);
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

    /// <summary>Validates a relative range like faker.js: max must be positive and greater than min, which can't be negative.</summary>
    private static void ValidateRange(int minYears, int maxYears)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minYears);
        if (maxYears <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxYears), maxYears, "The number of years must be greater than 0.");
        if (minYears >= maxYears)
            throw new ArgumentException("The maximum number of years must be greater than the minimum.", nameof(maxYears));
    }

    /// <summary><paramref name="date"/> moved by whole years, or <see langword="null"/> when that leaves the calendar (years 1–9999).</summary>
    private static DateTimeOffset? AddYears(DateTimeOffset date, int years) =>
        date.Year + (long)years is >= 1 and <= 9999 ? date.AddYears(years) : null;

    /// <summary><paramref name="date"/> moved by whole days, or <see langword="null"/> when that leaves the calendar.</summary>
    private static DateTimeOffset? AddDays(DateTimeOffset date, int days) =>
        Math.Abs((long)days) <= Room(date, days) / TimeSpan.TicksPerDay ? date.AddDays(days) : null;

    /// <summary><paramref name="date"/> moved by whole seconds, or <see langword="null"/> when that leaves the calendar.</summary>
    private static DateTimeOffset? AddSeconds(DateTimeOffset date, int seconds) =>
        Math.Abs((long)seconds) <= Room(date, seconds) / TimeSpan.TicksPerSecond ? date.AddSeconds(seconds) : null;

    /// <summary>The ticks between <paramref name="date"/> and the end of the calendar in the direction of <paramref name="sign"/>.</summary>
    private static long Room(DateTimeOffset date, int sign) =>
        sign < 0 ? date.UtcTicks - DateTimeOffset.MinValue.UtcTicks : DateTimeOffset.MaxValue.UtcTicks - date.UtcTicks;

    private static InvalidOperationException NoDate(int distance, string unit, string direction, DateTimeOffset reference) => new(
        distance == 0
            ? $"No date lies {direction} the reference date {reference:O}: it is at the end of the calendar. Pass another reference date."
            : $"No date lies {distance} {unit} or more {direction} the reference date {reference:O}: that is outside the calendar (years 1–9999).");
}
