using System.Globalization;

namespace Phoney.Templates;

/// <summary>Reads dates given as text (template options, <c>[FakeData(ReferenceDate = …)]</c>) the same way everywhere.</summary>
internal static class DateText
{
    /// <summary>
    /// Reads an ISO 8601 date, UTC unless it has an offset. Text without a day (<c>12:00</c>) is refused: parsing would take today's date,
    /// and data seeded from it would change every day.
    /// </summary>
    public static bool TryParse(string? text, out DateTimeOffset date)
    {
        date = default;
        // DateTimeOffset doesn't accept NoCurrentDateDefault; with it a time alone is 0001-01-01.
        return text is not null
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault | DateTimeStyles.AdjustToUniversal, out var day) && day.Year > 1
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }
}
