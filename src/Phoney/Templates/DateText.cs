using System.Globalization;

namespace Phoney.Templates;

/// <summary>Reads dates given as text (template options, <c>[FakeData(ReferenceDate = …)]</c>) the same way everywhere.</summary>
internal static class DateText
{
    /// <summary>
    /// Reads an ISO 8601 date (<c>2030-01-01</c>, optionally followed by a time and an offset), UTC unless it has an offset. Other text is
    /// refused, because a parsed date that leaves out the day or year (<c>12:00</c>, <c>June 5</c>) takes them from today, and data seeded from it
    /// would change over time.
    /// </summary>
    public static bool TryParse(string? text, out DateTimeOffset date)
    {
        date = default;
        return text is not null && StartsWithFullDate(text.AsSpan().Trim())
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    /// <summary>Whether the text is <c>yyyy-MM-dd</c>, alone or followed by <c>T</c> or a space and the time.</summary>
    private static bool StartsWithFullDate(ReadOnlySpan<char> text) =>
        text.Length >= 10
        && char.IsAsciiDigit(text[0]) && char.IsAsciiDigit(text[1]) && char.IsAsciiDigit(text[2]) && char.IsAsciiDigit(text[3])
        && text[4] == '-' && char.IsAsciiDigit(text[5]) && char.IsAsciiDigit(text[6])
        && text[7] == '-' && char.IsAsciiDigit(text[8]) && char.IsAsciiDigit(text[9])
        && (text.Length == 10 || text[10] is 'T' or 't' or ' ');
}
