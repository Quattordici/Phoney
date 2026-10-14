using static Phoney.Schemes.Checks;

namespace Phoney.Schemes;

/// <summary>United States, Brazil and Canada.</summary>
internal static class AmericanSchemes
{
    public static IEnumerable<NationalIdScheme> All()
    {
        yield return new Scheme("US", NationalIdKind.Person, UnitedStatesPerson, UnitedStatesPersonValid);
        yield return new Scheme("US", NationalIdKind.Company, UnitedStatesCompany, UnitedStatesCompanyValid);
        yield return new Scheme("BR", NationalIdKind.Person, BrazilPerson, BrazilPersonValid);
        yield return new Scheme("BR", NationalIdKind.Company, BrazilCompany, BrazilCompanyValid);
        yield return new Scheme("CA", NationalIdKind.Person, CanadaPerson, CanadaPersonValid);
    }

    // ---------------------------------------------------------------- United States

    /// <summary>Social Security number <c>AAA-GG-SSSS</c>. There is no check digit; the area is never 000, 666 or 9xx, group and serial are never zero.</summary>
    private static string UnitedStatesPerson(SeededRandom r, NationalIdRequest q)
    {
        var area = r.Int(1, 898);
        if (area >= 666)
            area++;
        return Join(q.Formatted, "-", Pad(area, 3), Pad(r.Int(1, 99), 2), Pad(r.Int(1, 9999), 4));
    }

    private static bool UnitedStatesPersonValid(string value)
    {
        var d = Compact(value);
        if (!IsDigits(d, 9))
            return false;
        var area = Number(d, 0, 3);
        return area is not (0 or 666) && area < 900 && Number(d, 3, 2) != 0 && Number(d, 5, 4) != 0;
    }

    /// <summary>The two-digit prefixes the IRS assigns to employer identification numbers.</summary>
    private static readonly string[] EinPrefixes =
    [
        "01", "02", "03", "04", "05", "06", "10", "11", "12", "13", "14", "15", "16",
        "20", "21", "22", "23", "24", "25", "26", "27", "30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
        "40", "41", "42", "43", "44", "45", "46", "47", "48", "50", "51", "52", "53", "54", "55", "56", "57", "58", "59",
        "60", "61", "62", "63", "64", "65", "66", "67", "68", "71", "72", "73", "74", "75", "76", "77",
        "80", "81", "82", "83", "84", "85", "86", "87", "88", "90", "91", "92", "93", "94", "95", "98", "99",
    ];

    /// <summary>Employer identification number <c>NN-NNNNNNN</c> with an assigned prefix; there is no check digit.</summary>
    private static string UnitedStatesCompany(SeededRandom r, NationalIdRequest q) =>
        Join(q.Formatted, "-", r.Element(EinPrefixes), RandomDigits(r, 7));

    private static bool UnitedStatesCompanyValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 9) && Array.IndexOf(EinPrefixes, d[..2]) >= 0;
    }

    // ---------------------------------------------------------------- Brazil

    private static readonly int[] CpfWeights1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CpfWeights2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjWeights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjWeights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>The Brazilian check digit: <c>11 - Σ mod 11</c>, with 10 and 11 becoming 0.</summary>
    private static char BrazilCheck(string digits, ReadOnlySpan<int> weights)
    {
        var rest = Weighted(digits, weights) % 11;
        return rest < 2 ? '0' : (char)('0' + (11 - rest));
    }

    /// <summary>CPF <c>NNN.NNN.NNN-NN</c>: two mod-11 check digits.</summary>
    private static string BrazilPerson(SeededRandom r, NationalIdRequest q)
    {
        string body;
        do
        {
            body = RandomDigits(r, 9);
        }
        while (body.Distinct().Count() == 1);

        var first = BrazilCheck(body, CpfWeights1);
        var second = BrazilCheck(body + first, CpfWeights2);
        var d = body + first + second;
        return q.Formatted ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : d;
    }

    private static bool BrazilPersonValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 11) && d.Distinct().Count() > 1 && BrazilCheck(d[..9], CpfWeights1) == d[9] && BrazilCheck(d[..10], CpfWeights2) == d[10];
    }

    /// <summary>CNPJ <c>NN.NNN.NNN/NNNN-NN</c>: a head office (branch 0001) with two mod-11 check digits.</summary>
    private static string BrazilCompany(SeededRandom r, NationalIdRequest q)
    {
        var body = Digit(r, 1, 9) + RandomDigits(r, 7) + "0001";
        var first = BrazilCheck(body, CnpjWeights1);
        var second = BrazilCheck(body + first, CnpjWeights2);
        var d = body + first + second;
        return q.Formatted ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}" : d;
    }

    private static bool BrazilCompanyValid(string value)
    {
        var d = Compact(value, " -./");
        return IsDigits(d, 14) && d.Distinct().Count() > 1 && BrazilCheck(d[..12], CnpjWeights1) == d[12] && BrazilCheck(d[..13], CnpjWeights2) == d[13];
    }

    // ---------------------------------------------------------------- Canada

    /// <summary>Social insurance number <c>NNN-NNN-NNN</c>: Luhn check, never starting with 0 or 8 (9 marks temporary residents).</summary>
    private static string CanadaPerson(SeededRandom r, NationalIdRequest q)
    {
        var body = r.Element(['1', '2', '3', '4', '5', '6', '7', '9']) + RandomDigits(r, 7);
        var d = body + Luhn(body);
        return Join(q.Formatted, "-", d[..3], d[3..6], d[6..]);
    }

    private static bool CanadaPersonValid(string value)
    {
        var d = Compact(value);
        return IsDigits(d, 9) && d[0] is not ('0' or '8') && LuhnValid(d);
    }
}
