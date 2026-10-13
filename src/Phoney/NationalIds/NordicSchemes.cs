using Phoney.Modules;
using static Phoney.Schemes.Checks;

namespace Phoney.Schemes;

/// <summary>Sweden, Norway, Denmark and Finland.</summary>
internal static class NordicSchemes
{
    public static IEnumerable<NationalIdScheme> All()
    {
        yield return new Scheme("SE", NationalIdKind.Person, SwedenPerson, SwedenPersonValid);
        yield return new Scheme("SE", NationalIdKind.Company, SwedenCompany, SwedenCompanyValid);
        yield return new Scheme("SE", NationalIdKind.Vat, SwedenVat, SwedenVatValid);
        yield return new Scheme("NO", NationalIdKind.Person, NorwayPerson, NorwayPersonValid);
        yield return new Scheme("NO", NationalIdKind.Company, NorwayCompany, NorwayCompanyValid);
        yield return new Scheme("NO", NationalIdKind.Vat, NorwayVat, NorwayVatValid);
        yield return new Scheme("DK", NationalIdKind.Person, DenmarkPerson, DenmarkPersonValid);
        yield return new Scheme("DK", NationalIdKind.Company, DenmarkCompany, DenmarkCompanyValid);
        yield return new Scheme("DK", NationalIdKind.Vat, DenmarkVat, DenmarkVatValid);
        yield return new Scheme("FI", NationalIdKind.Person, FinlandPerson, FinlandPersonValid);
        yield return new Scheme("FI", NationalIdKind.Company, FinlandCompany, FinlandCompanyValid);
        yield return new Scheme("FI", NationalIdKind.Vat, FinlandVat, FinlandVatValid);
    }

    // ---------------------------------------------------------------- Sweden

    /// <summary>Personnummer <c>YYYYMMDD-NNNC</c>: the 9th digit is odd for men, the last digit is a Luhn check over <c>YYMMDDNNNC</c>.</summary>
    private static string SwedenPerson(Randomizer r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        RequireYear(b, 1800, 2099, "Swedish personnummer");
        var serial = (r.Int(0, 99) * 10) + (r.Int(0, 4) * 2) + (q.Sex == Sex.Male ? 1 : 0);
        if (serial == 0)
            serial = 2; // 000 is never issued; 002 keeps the woman's even third digit
        var date = Pad(b.Year % 100, 2) + Pad(b.Month, 2) + Pad(b.Day, 2);
        var check = Luhn(date + Pad(serial, 3));
        return Join(q.Formatted, "-", Pad(b.Year, 4) + Pad(b.Month, 2) + Pad(b.Day, 2), Pad(serial, 3) + check);
    }

    /// <summary>Accepts 10 or 12 digits with <c>-</c>/<c>+</c>; samordningsnummer (day + 60) are valid too.</summary>
    private static bool SwedenPersonValid(string value)
    {
        var s = value.Trim();
        var sign = s.IndexOfAny(['-', '+']);
        var digits = sign < 0 ? s : s.Remove(sign, 1);
        if (!AllDigits(digits) || digits.Length is not (10 or 12) || (sign >= 0 && sign != digits.Length - 4))
            return false;
        if (!LuhnValid(digits[^10..]))
            return false;
        var month = Number(digits, digits.Length - 8, 2);
        var day = Number(digits, digits.Length - 6, 2);
        day = day > 60 ? day - 60 : day;
        var yy = Number(digits, digits.Length - 10, 2);
        return digits.Length == 12
            ? IsDate(Number(digits, 0, 4), month, day)
            : SwedishCenturies.Any(century => IsDate(century + yy, month, day));
    }

    /// <summary>Centuries a 10-digit personnummer can belong to, most likely first.</summary>
    private static readonly int[] SwedishCenturies = [1900, 2000, 1800];

    private static readonly char[] SwedishOrganisationGroups = ['5', '5', '5', '5', '6', '7', '8', '9', '2'];

    /// <summary>Organisationsnummer <c>NNNNNN-NNNN</c>: Luhn check, third digit at least 2 (so it is never a date).</summary>
    private static string SwedenCompany(Randomizer r, NationalIdRequest q)
    {
        var body = r.Element(SwedishOrganisationGroups).ToString() + r.Digit() + Digit(r, 2, 9) + RandomDigits(r, 6);
        return Join(q.Formatted, "-", body[..6], body[6..] + Luhn(body));
    }

    private static bool SwedenCompanyValid(string value)
    {
        var digits = Compact(value);
        return IsDigits(digits, 10) && digits[2] >= '2' && LuhnValid(digits);
    }

    /// <summary>VAT number <c>SE</c> + organisationsnummer + <c>01</c>.</summary>
    private static string SwedenVat(Randomizer r, NationalIdRequest q) =>
        "SE" + Compact(SwedenCompany(r, q with { Formatted = false })) + "01";

    private static bool SwedenVatValid(string value)
    {
        var v = Compact(value, " -.");
        return v.Length == 14 && v.StartsWith("SE", StringComparison.OrdinalIgnoreCase) && v.EndsWith("01", StringComparison.Ordinal) && SwedenCompanyValid(v[2..12]);
    }

    // ---------------------------------------------------------------- Norway

    private static readonly int[] NorwayWeights1 = [3, 7, 6, 1, 8, 9, 4, 5, 2];
    private static readonly int[] NorwayWeights2 = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>Fødselsnummer <c>DDMMYY IIIKK</c>: the century follows from the individual number, its third digit is odd for men, K are two mod-11 checks.</summary>
    private static string NorwayPerson(Randomizer r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        RequireYear(b, 1854, 2039, "Norwegian fødselsnummer");
        var (low, high) = b.Year switch { < 1900 => (500, 749), < 2000 => (0, 499), _ => (500, 999) };
        var date = Pad(b.Day, 2) + Pad(b.Month, 2) + Pad(b.Year % 100, 2);
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var serial = (r.Int(low, high) / 2 * 2) + (q.Sex == Sex.Male ? 1 : 0);
            if (serial > high)
                serial -= 2;
            var body = date + Pad(serial, 3);
            if (Mod11(body, NorwayWeights1) is not { } k1 || Mod11(body + k1, NorwayWeights2) is not { } k2)
                continue;
            return Join(q.Formatted, " ", date, Pad(serial, 3) + k1 + k2);
        }

        throw new InvalidOperationException("Could not find a valid Norwegian fødselsnummer.");
    }

    /// <summary>D-numbers (day + 40) and H-numbers (month + 40) are valid too.</summary>
    private static bool NorwayPersonValid(string value)
    {
        var d = Compact(value, " ");
        if (!IsDigits(d, 11) || Mod11(d[..9], NorwayWeights1) != d[9] || Mod11(d[..10], NorwayWeights2) != d[10])
            return false;
        var day = Number(d, 0, 2);
        var month = Number(d, 2, 2);
        var yy = Number(d, 4, 2);
        var serial = Number(d, 6, 3);
        day = day > 40 ? day - 40 : day;
        month = month > 40 ? month - 40 : month;
        int? year = serial switch
        {
            < 500 => 1900 + yy,
            < 750 when yy >= 54 => 1800 + yy,
            >= 900 when yy >= 40 => 1900 + yy,
            >= 500 when yy < 40 => 2000 + yy,
            _ => null,
        };
        return year is { } y && IsDate(y, month, day);
    }

    private static readonly int[] NorwayOrganisationWeights = [3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>Organisasjonsnummer <c>NNN NNN NNN</c>: mod-11 check, new numbers start with 8 or 9.</summary>
    private static string NorwayCompany(Randomizer r, NationalIdRequest q)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var body = (r.Bool(0.7) ? '9' : '8') + RandomDigits(r, 7);
            if (Mod11(body, NorwayOrganisationWeights) is { } check)
            {
                var d = body + check;
                return Join(q.Formatted, " ", d[..3], d[3..6], d[6..]);
            }
        }

        throw new InvalidOperationException("Could not find a valid Norwegian organisasjonsnummer.");
    }

    private static bool NorwayCompanyValid(string value)
    {
        var d = Compact(value, " ");
        return IsDigits(d, 9) && Mod11(d[..8], NorwayOrganisationWeights) == d[8];
    }

    /// <summary>VAT number <c>NO</c> + organisasjonsnummer + <c>MVA</c>.</summary>
    private static string NorwayVat(Randomizer r, NationalIdRequest q)
    {
        var d = NorwayCompany(r, q with { Formatted = false });
        return q.Formatted ? $"NO {d[..3]} {d[3..6]} {d[6..]} MVA" : $"NO{d}MVA";
    }

    private static bool NorwayVatValid(string value)
    {
        var v = Compact(value, " ");
        return v.Length == 14 && v.StartsWith("NO", StringComparison.OrdinalIgnoreCase) && v.EndsWith("MVA", StringComparison.OrdinalIgnoreCase) && NorwayCompanyValid(v[2..11]);
    }

    // ---------------------------------------------------------------- Denmark

    private static readonly int[] DenmarkPersonWeights = [4, 3, 2, 7, 6, 5, 4, 3, 2, 1];

    /// <summary>
    /// CPR <c>DDMMYY-SSSS</c>: the first serial digit selects the century, the last digit is odd for men. Generated numbers
    /// also satisfy the classic mod-11 rule (dropped for new numbers in 2007), so old and new validators accept them.
    /// </summary>
    private static string DenmarkPerson(Randomizer r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        RequireYear(b, 1858, 2057, "Danish CPR");
        var yy = b.Year % 100;
        var date = Pad(b.Day, 2) + Pad(b.Month, 2) + Pad(yy, 2);
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var first = b.Year switch
            {
                < 1900 => Digit(r, 5, 8),
                < 2000 => Digit(r, 0, 3),
                < 2037 => r.Bool() ? '4' : '9',
                _ => Digit(r, 5, 8),
            };
            var serial = first + RandomDigits(r, 2) + (char)('0' + (r.Int(0, 4) * 2) + (q.Sex == Sex.Male ? 1 : 0));
            if (Weighted(date + serial, DenmarkPersonWeights) % 11 == 0)
                return Join(q.Formatted, "-", date, serial);
        }

        throw new InvalidOperationException("Could not find a valid Danish CPR number.");
    }

    /// <summary>Checks the date and its century digit; the mod-11 rule is not required because new CPR numbers don't follow it.</summary>
    private static bool DenmarkPersonValid(string value)
    {
        var d = Compact(value);
        if (!IsDigits(d, 10))
            return false;
        var yy = Number(d, 4, 2);
        int? year = d[6] switch
        {
            <= '3' => 1900 + yy,
            '4' or '9' => yy <= 36 ? 2000 + yy : 1900 + yy,
            _ => yy <= 57 ? 2000 + yy : 1800 + yy,
        };
        return IsDate(year.Value, Number(d, 2, 2), Number(d, 0, 2));
    }

    private static readonly int[] DenmarkCompanyWeights = [2, 7, 6, 5, 4, 3, 2, 1];

    /// <summary>CVR <c>NN NN NN NN</c>: weighted mod-11 sum of 0.</summary>
    private static string DenmarkCompany(Randomizer r, NationalIdRequest q)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var body = Digit(r, 1, 9) + RandomDigits(r, 6);
            var check = (11 - (Weighted(body, DenmarkCompanyWeights.AsSpan(0, 7)) % 11)) % 11;
            if (check < 10)
            {
                var d = body + (char)('0' + check);
                return Join(q.Formatted, " ", d[..2], d[2..4], d[4..6], d[6..]);
            }
        }

        throw new InvalidOperationException("Could not find a valid Danish CVR number.");
    }

    private static bool DenmarkCompanyValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 8) && d[0] != '0' && Weighted(d, DenmarkCompanyWeights) % 11 == 0;
    }

    /// <summary>VAT number <c>DK</c> + CVR.</summary>
    private static string DenmarkVat(Randomizer r, NationalIdRequest q)
    {
        var d = DenmarkCompany(r, q with { Formatted = false });
        return q.Formatted ? $"DK {d[..2]} {d[2..4]} {d[4..6]} {d[6..]}" : "DK" + d;
    }

    private static bool DenmarkVatValid(string value)
    {
        var v = Compact(value, " ");
        return v.Length == 10 && v.StartsWith("DK", StringComparison.OrdinalIgnoreCase) && DenmarkCompanyValid(v[2..]);
    }

    // ---------------------------------------------------------------- Finland

    private const string FinnishCheckCharacters = "0123456789ABCDEFHJKLMNPRSTUVWXY";

    /// <summary>
    /// Henkilötunnus <c>DDMMYYCZZZQ</c>: C is the century sign (<c>+</c> 1800s, <c>-</c> 1900s, <c>A</c> 2000s), ZZZ is even for women,
    /// Q is the number DDMMYYZZZ mod 31 as a character.
    /// </summary>
    private static string FinlandPerson(Randomizer r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        RequireYear(b, 1800, 2099, "Finnish henkilötunnus");
        var sign = b.Year < 1900 ? '+' : b.Year < 2000 ? '-' : 'A';
        var serial = (r.Int(1, 449) * 2) + (q.Sex == Sex.Male ? 1 : 0);
        var digits = Pad(b.Day, 2) + Pad(b.Month, 2) + Pad(b.Year % 100, 2) + Pad(serial, 3);
        return digits[..6] + sign + digits[6..] + FinnishCheckCharacters[int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) % 31];
    }

    private static bool FinlandPersonValid(string value)
    {
        var s = value.Trim().ToUpperInvariant();
        if (s.Length != 11 || !AllDigits(s[..6]) || !AllDigits(s[7..10]))
            return false;
        var yy = Number(s, 4, 2);
        int? century = s[6] switch
        {
            '+' => 1800,
            '-' or 'Y' or 'X' or 'W' or 'V' or 'U' => 1900,
            'A' or 'B' or 'C' or 'D' or 'E' or 'F' => 2000,
            _ => null,
        };
        var serial = Number(s, 7, 3);
        return century is { } c
            && serial >= 2
            && IsDate(c + yy, Number(s, 2, 2), Number(s, 0, 2))
            && s[10] == FinnishCheckCharacters[int.Parse(s[..6] + s[7..10], System.Globalization.CultureInfo.InvariantCulture) % 31];
    }

    private static readonly int[] FinlandCompanyWeights = [7, 9, 10, 5, 8, 4, 2];

    /// <summary>Y-tunnus <c>NNNNNNN-C</c>: mod-11 check digit.</summary>
    private static string FinlandCompany(Randomizer r, NationalIdRequest q)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var body = Digit(r, 0, 2) + RandomDigits(r, 6);
            if (Mod11(body, FinlandCompanyWeights) is { } check)
                return Join(q.Formatted, "-", body, check.ToString());
        }

        throw new InvalidOperationException("Could not find a valid Finnish Y-tunnus.");
    }

    private static bool FinlandCompanyValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 8) && Mod11(d[..7], FinlandCompanyWeights) == d[7];
    }

    /// <summary>VAT number <c>FI</c> + Y-tunnus without the hyphen.</summary>
    private static string FinlandVat(Randomizer r, NationalIdRequest q) => "FI" + FinlandCompany(r, q with { Formatted = false });

    private static bool FinlandVatValid(string value)
    {
        var v = Compact(value, " ");
        return v.Length == 10 && v.StartsWith("FI", StringComparison.OrdinalIgnoreCase) && FinlandCompanyValid(v[2..]);
    }
}
