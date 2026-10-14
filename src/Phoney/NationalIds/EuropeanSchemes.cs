using Phoney.Modules;
using static Phoney.Schemes.Checks;

namespace Phoney.Schemes;

/// <summary>United Kingdom, Poland, the Netherlands, Spain and France.</summary>
internal static class EuropeanSchemes
{
    public static IEnumerable<NationalIdScheme> All()
    {
        yield return new Scheme("GB", NationalIdKind.Person, UnitedKingdomPerson, UnitedKingdomPersonValid);
        yield return new Scheme("GB", NationalIdKind.Vat, UnitedKingdomVat, UnitedKingdomVatValid);
        yield return new Scheme("PL", NationalIdKind.Person, PolandPerson, PolandPersonValid);
        yield return new Scheme("PL", NationalIdKind.Company, PolandCompany, PolandCompanyValid);
        yield return new Scheme("PL", NationalIdKind.Vat, PolandVat, PolandVatValid);
        yield return new Scheme("NL", NationalIdKind.Person, NetherlandsPerson, NetherlandsPersonValid);
        yield return new Scheme("ES", NationalIdKind.Person, SpainPerson, SpainPersonValid);
        yield return new Scheme("FR", NationalIdKind.Person, FrancePerson, FrancePersonValid);
        yield return new Scheme("FR", NationalIdKind.Company, FranceCompany, FranceCompanyValid);
        yield return new Scheme("FR", NationalIdKind.Vat, FranceVat, FranceVatValid);
    }

    // ---------------------------------------------------------------- United Kingdom

    private const string NinoFirstLetters = "ABCEGHJKLMNOPRSTWXYZ"; // not D, F, I, Q, U, V
    private const string NinoSecondLetters = "ABCEGHJKLMNPRSTWXYZ"; // additionally not O
    private static readonly string[] NinoForbiddenPrefixes = ["BG", "GB", "KN", "NK", "NT", "TN", "ZZ"];

    /// <summary>National Insurance number <c>AB 12 34 56 C</c>. There is no check digit; the prefix letters and suffix follow HMRC's rules.</summary>
    private static string UnitedKingdomPerson(SeededRandom r, NationalIdRequest q)
    {
        string prefix;
        do
        {
            prefix = r.Element(NinoFirstLetters.AsSpan()).ToString() + r.Element(NinoSecondLetters.AsSpan());
        }
        while (Array.IndexOf(NinoForbiddenPrefixes, prefix) >= 0);

        var digits = RandomDigits(r, 6);
        var suffix = (char)('A' + r.Int(0, 3));
        return Join(q.Formatted, " ", prefix, digits[..2], digits[2..4], digits[4..], suffix.ToString());
    }

    private static bool UnitedKingdomPersonValid(string value)
    {
        var n = Compact(value, " -").ToUpperInvariant();
        return n.Length == 9
            && NinoFirstLetters.Contains(n[0], StringComparison.Ordinal)
            && NinoSecondLetters.Contains(n[1], StringComparison.Ordinal)
            && Array.IndexOf(NinoForbiddenPrefixes, n[..2]) < 0
            && AllDigits(n[2..8])
            && n[8] is >= 'A' and <= 'D';
    }

    private static readonly int[] UnitedKingdomVatWeights = [8, 7, 6, 5, 4, 3, 2];

    /// <summary>Standard UK VAT number <c>GB 123 4567 89</c>: the last two digits make the weighted sum divisible by 97.</summary>
    private static string UnitedKingdomVat(SeededRandom r, NationalIdRequest q)
    {
        var body = Digit(r, 1, 9) + RandomDigits(r, 6);
        var check = (97 - (Weighted(body, UnitedKingdomVatWeights) % 97)) % 97;
        var d = body + Pad(check, 2);
        return q.Formatted ? $"GB {d[..3]} {d[3..7]} {d[7..]}" : "GB" + d;
    }

    private static bool UnitedKingdomVatValid(string value)
    {
        var v = Compact(value, " -");
        if (v.Length != 11 || !v.StartsWith("GB", StringComparison.OrdinalIgnoreCase) || !AllDigits(v[2..]))
            return false;
        var d = v[2..];
        var rest = (Weighted(d, UnitedKingdomVatWeights) + Number(d, 7, 2)) % 97;
        // Numbers issued from 2010 on (first three digits 100 and up) may also use the 42 and 55 variants.
        return rest == 0 || (Number(d, 0, 3) >= 100 && rest is 42 or 55);
    }

    // ---------------------------------------------------------------- Poland

    private static readonly int[] PeselWeights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];

    /// <summary>PESEL: birth date with a century offset on the month, a serial, a sex digit (even = women) and a mod-10 check.</summary>
    private static string PolandPerson(SeededRandom r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        RequireYear(b, 1800, 2299, "Polish PESEL");
        var offset = (b.Year / 100) switch { 18 => 80, 19 => 0, 20 => 20, 21 => 40, _ => 60 };
        var body = Pad(b.Year % 100, 2) + Pad(b.Month + offset, 2) + Pad(b.Day, 2) + RandomDigits(r, 3) + (char)('0' + (r.Int(0, 4) * 2) + (q.Sex == Sex.Male ? 1 : 0));
        return body + (char)('0' + ((10 - (Weighted(body, PeselWeights) % 10)) % 10));
    }

    private static bool PolandPersonValid(string value)
    {
        var d = Compact(value, " ");
        if (!IsDigits(d, 11) || (10 - (Weighted(d, PeselWeights) % 10)) % 10 != d[10] - '0')
            return false;
        // The month carries the century: 81–92 → 1800s, 01–12 → 1900s, 21–32 → 2000s, 41–52 → 2100s, 61–72 → 2200s.
        var month = Number(d, 2, 2);
        var century = month switch { > 80 => 1800, > 60 => 2200, > 40 => 2100, > 20 => 2000, _ => 1900 };
        return IsDate(century + Number(d, 0, 2), month % 20, Number(d, 4, 2));
    }

    private static readonly int[] RegonWeights = [8, 9, 2, 3, 4, 5, 6, 7];

    /// <summary>REGON (9 digits): weighted sum mod 11 (10 becomes 0).</summary>
    private static string PolandCompany(SeededRandom r, NationalIdRequest q)
    {
        var body = RandomDigits(r, 8);
        return body + (char)('0' + (Weighted(body, RegonWeights) % 11 % 10));
    }

    private static bool PolandCompanyValid(string value)
    {
        var d = Compact(value, " -");
        return IsDigits(d, 9) && Weighted(d, RegonWeights) % 11 % 10 == d[8] - '0';
    }

    private static readonly int[] NipWeights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>VAT number <c>PL</c> + NIP (<c>NNN-NNN-NN-NN</c>): weighted sum mod 11 is the last digit.</summary>
    private static string PolandVat(SeededRandom r, NationalIdRequest q)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var body = Digit(r, 1, 9) + RandomDigits(r, 8);
            var check = Weighted(body, NipWeights) % 11;
            if (check < 10)
            {
                var d = body + (char)('0' + check);
                return q.Formatted ? $"PL {d[..3]}-{d[3..6]}-{d[6..8]}-{d[8..]}" : "PL" + d;
            }
        }

        throw new InvalidOperationException("Could not find a valid Polish NIP.");
    }

    private static bool PolandVatValid(string value)
    {
        var v = Compact(value, " -");
        return v.Length == 12 && v.StartsWith("PL", StringComparison.OrdinalIgnoreCase) && IsDigits(v[2..], 10)
            && Weighted(v[2..], NipWeights) % 11 == v[11] - '0';
    }

    // ---------------------------------------------------------------- Netherlands

    private static readonly int[] BsnWeights = [9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>Burgerservicenummer: nine digits that pass the 11-proef (weights 9…2, −1).</summary>
    private static string NetherlandsPerson(SeededRandom r, NationalIdRequest q)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var body = Digit(r, 1, 9) + RandomDigits(r, 7);
            var check = Weighted(body, BsnWeights) % 11;
            if (check < 10)
                return body + (char)('0' + check);
        }

        throw new InvalidOperationException("Could not find a valid BSN.");
    }

    private static bool NetherlandsPersonValid(string value)
    {
        var d = Compact(value);
        if (d.Length == 8)
            d = "0" + d;
        return IsDigits(d, 9) && d != "000000000" && Weighted(d, BsnWeights) % 11 == d[8] - '0';
    }

    // ---------------------------------------------------------------- Spain

    private const string DniLetters = "TRWAGMYFPDXBNJZSQVHLCKE";

    /// <summary>DNI: eight digits and the control letter <c>number mod 23</c> picks from <c>TRWAGMYFPDXBNJZSQVHLCKE</c>.</summary>
    private static string SpainPerson(SeededRandom r, NationalIdRequest q)
    {
        var number = r.Int(1_000_000, 99_999_999);
        return Pad(number, 8) + DniLetters[number % 23];
    }

    private static bool SpainPersonValid(string value)
    {
        var d = Compact(value).ToUpperInvariant();
        return d.Length == 9 && AllDigits(d[..8]) && d[8] == DniLetters[Number(d, 0, 8) % 23];
    }

    // ---------------------------------------------------------------- France

    /// <summary>
    /// NIR (sécurité sociale) <c>S YY MM DD CCC OOO KK</c>: sex digit (1 = men), birth year and month, département and commune,
    /// an order number and the key <c>97 − (first 13 digits mod 97)</c>.
    /// </summary>
    private static string FrancePerson(SeededRandom r, NationalIdRequest q)
    {
        var b = q.BirthDate;
        var department = r.Int(1, 94);
        if (department >= 20)
            department++; // 20 is Corsica (2A/2B); 21–95 and 01–19 remain
        var body = (q.Sex == Sex.Male ? "1" : "2") + Pad(b.Year % 100, 2) + Pad(b.Month, 2) + Pad(department, 2) + Pad(r.Int(1, 990), 3) + Pad(r.Int(1, 999), 3);
        var key = Pad(97 - (long.Parse(body, System.Globalization.CultureInfo.InvariantCulture) % 97), 2);
        return Join(q.Formatted, " ", body[..1], body[1..3], body[3..5], body[5..7], body[7..10], body[10..13], key);
    }

    private static bool FrancePersonValid(string value)
    {
        var d = Compact(value, " ").ToUpperInvariant();
        if (d.Length != 15)
            return false;
        // Corsica: 2A and 2B count as 19 and 18 when computing the key.
        var numeric = d.Replace("2A", "19", StringComparison.Ordinal).Replace("2B", "18", StringComparison.Ordinal);
        if (!AllDigits(numeric) || numeric.Length != 15 || d[0] is not ('1' or '2' or '3' or '4' or '7' or '8'))
            return false;
        var month = Number(numeric, 3, 2);
        return (month is >= 1 and <= 12 or >= 20 and <= 42 or >= 50)
            && 97 - (long.Parse(numeric[..13], System.Globalization.CultureInfo.InvariantCulture) % 97) == Number(numeric, 13, 2);
    }

    /// <summary>SIREN <c>NNN NNN NNN</c>: nine digits with a Luhn check.</summary>
    private static string FranceCompany(SeededRandom r, NationalIdRequest q)
    {
        var body = Digit(r, 1, 9) + RandomDigits(r, 7);
        var d = body + Luhn(body);
        return Join(q.Formatted, " ", d[..3], d[3..6], d[6..]);
    }

    private static bool FranceCompanyValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 9) && LuhnValid(d);
    }

    /// <summary>VAT number <c>FR KK NNN NNN NNN</c>: the key is <c>(12 + 3 × (SIREN mod 97)) mod 97</c>.</summary>
    private static string FranceVat(SeededRandom r, NationalIdRequest q)
    {
        var siren = Compact(FranceCompany(r, q with { Formatted = false }));
        var key = Pad((12 + (3 * (long.Parse(siren, System.Globalization.CultureInfo.InvariantCulture) % 97))) % 97, 2);
        return q.Formatted ? $"FR {key} {siren[..3]} {siren[3..6]} {siren[6..]}" : "FR" + key + siren;
    }

    private static bool FranceVatValid(string value)
    {
        var v = Compact(value, " .");
        if (v.Length != 13 || !v.StartsWith("FR", StringComparison.OrdinalIgnoreCase) || !AllDigits(v[2..]))
            return false;
        var siren = v[4..];
        return LuhnValid(siren) && Number(v, 2, 2) == (12 + (3 * (long.Parse(siren, System.Globalization.CultureInfo.InvariantCulture) % 97))) % 97;
    }
}
