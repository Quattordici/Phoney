using System.Globalization;
using System.Text;
using Phoney.Modules;

namespace Phoney.Schemes;

/// <summary>A scheme assembled from a generator and a validator, used for the built-in countries.</summary>
internal sealed class Scheme(string country, NationalIdKind kind, Func<SeededRandom, NationalIdRequest, string> generate, Func<string, bool> isValid)
    : NationalIdScheme(country, kind)
{
    /// <inheritdoc />
    public override string Generate(SeededRandom random, NationalIdRequest request) => generate(random, request);

    /// <inheritdoc />
    public override bool IsValid(string value) => isValid(value);
}

/// <summary>Digit arithmetic and text helpers shared by the schemes.</summary>
internal static class Checks
{
    /// <summary>Removes the characters in <paramref name="separators"/> and surrounding spaces.</summary>
    public static string Compact(string value, string separators = " -.")
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (separators.IndexOf(c, StringComparison.Ordinal) < 0)
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Whether <paramref name="text"/> is non-empty and consists of ASCII digits only.</summary>
    public static bool AllDigits(string text)
    {
        if (text.Length == 0)
            return false;
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }

    /// <summary>Whether <paramref name="text"/> is a number of exactly <paramref name="length"/> digits.</summary>
    public static bool IsDigits(string text, int length) => text.Length == length && AllDigits(text);

    /// <summary>Σ digit × weight over the first <c>weights.Length</c> digits.</summary>
    public static int Weighted(string digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
            sum += (digits[i] - '0') * weights[i];
        return sum;
    }

    /// <summary><paramref name="count"/> random digits.</summary>
    public static string RandomDigits(SeededRandom random, int count)
    {
        var chars = new char[count];
        for (var i = 0; i < count; i++)
            chars[i] = random.Digit();
        return new string(chars);
    }

    /// <summary>A random digit between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static char Digit(SeededRandom random, int min, int max) => (char)('0' + random.Int(min, max));

    /// <summary><paramref name="value"/> as decimal digits, zero-padded to <paramref name="width"/>.</summary>
    public static string Pad(long value, int width) => value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    /// <summary>The number in the digits <paramref name="start"/>… of <paramref name="digits"/>.</summary>
    public static int Number(string digits, int start, int length) => int.Parse(digits.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Whether the date exists in the proleptic Gregorian calendar.</summary>
    public static bool IsDate(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);

    /// <summary>Throws when the birth date is outside the range the scheme can encode.</summary>
    public static void RequireYear(DateOnly birthDate, int min, int max, string scheme)
    {
        if (birthDate.Year < min || birthDate.Year > max)
            throw new ArgumentOutOfRangeException(nameof(birthDate), birthDate, $"{scheme} identifiers can encode birth years {min}–{max}.");
    }

    /// <summary>The Luhn check digit that completes <paramref name="body"/>.</summary>
    public static char Luhn(string body) => (char)('0' + HelpersModule.LuhnCheckDigit(body));

    /// <summary>Whether <paramref name="digits"/> (digits only) passes the Luhn check.</summary>
    public static bool LuhnValid(string digits) => AllDigits(digits) && HelpersModule.LuhnCheck(digits);

    /// <summary>
    /// Appends a check digit to <paramref name="body"/> from <c>11 - Σ mod 11</c> (11 becomes 0), or returns <see langword="null"/>
    /// when the result would be 10 and the body must be discarded.
    /// </summary>
    public static char? Mod11(string body, ReadOnlySpan<int> weights)
    {
        var check = 11 - (Weighted(body, weights) % 11);
        return check switch
        {
            11 => '0',
            10 => null,
            _ => (char)('0' + check),
        };
    }

    /// <summary>Joins <paramref name="parts"/> with <paramref name="separator"/> when <paramref name="formatted"/>, else concatenates them.</summary>
    public static string Join(bool formatted, string separator, params string[] parts) => string.Join(formatted ? separator : "", parts);
}
