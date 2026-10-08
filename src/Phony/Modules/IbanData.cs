namespace Phony.Modules;

/// <summary>
/// IBAN structures per country and ISO 3166 codes used for BICs. Ported from faker.js
/// <c>src/modules/finance/iban.ts</c> (v10.6.0).
/// </summary>
internal static class IbanData
{
    /// <summary>A BBAN segment: <c>a</c> = upper-case letters, <c>c</c> = alphanumeric, <c>n</c> = digits.</summary>
    public readonly record struct Segment(char Type, int Count);

    /// <summary>The structure of one country's IBAN.</summary>
    public sealed record Format(string Country, int Total, Segment[] Bban, string Example);

    public static readonly Format[] Formats =
    [
        new("AL", 28, [new('n', 8), new('c', 16)], "ALkk bbbs sssx cccc cccc cccc cccc"),
        new("AD", 24, [new('n', 8), new('c', 12)], "ADkk bbbb ssss cccc cccc cccc"),
        new("AT", 20, [new('n', 5), new('n', 11)], "ATkk bbbb bccc cccc cccc"),
        new("AZ", 28, [new('a', 4), new('n', 20)], "AZkk bbbb cccc cccc cccc cccc cccc"),
        new("BH", 22, [new('a', 4), new('c', 14)], "BHkk bbbb cccc cccc cccc cc"),
        new("BE", 16, [new('n', 3), new('n', 9)], "BEkk bbbc cccc ccxx"),
        new("BA", 20, [new('n', 6), new('n', 10)], "BAkk bbbs sscc cccc ccxx"),
        new("BR", 29, [new('n', 13), new('n', 10), new('a', 1), new('c', 1)], "BRkk bbbb bbbb ssss sccc cccc ccct n"),
        new("BG", 22, [new('a', 4), new('n', 6), new('c', 8)], "BGkk bbbb ssss ddcc cccc cc"),
        new("CR", 22, [new('n', 1), new('n', 3), new('n', 14)], "CRkk xbbb cccc cccc cccc cc"),
        new("HR", 21, [new('n', 7), new('n', 10)], "HRkk bbbb bbbc cccc cccc c"),
        new("CY", 28, [new('n', 8), new('c', 16)], "CYkk bbbs ssss cccc cccc cccc cccc"),
        new("CZ", 24, [new('n', 10), new('n', 10)], "CZkk bbbb ssss sscc cccc cccc"),
        new("DK", 18, [new('n', 4), new('n', 10)], "DKkk bbbb cccc cccc cc"),
        new("DO", 28, [new('a', 4), new('n', 20)], "DOkk bbbb cccc cccc cccc cccc cccc"),
        new("TL", 23, [new('n', 3), new('n', 16)], "TLkk bbbc cccc cccc cccc cxx"),
        new("EE", 20, [new('n', 4), new('n', 12)], "EEkk bbss cccc cccc cccx"),
        new("FO", 18, [new('n', 4), new('n', 10)], "FOkk bbbb cccc cccc cx"),
        new("FI", 18, [new('n', 6), new('n', 8)], "FIkk bbbb bbcc cccc cx"),
        new("FR", 27, [new('n', 10), new('c', 11), new('n', 2)], "FRkk bbbb bggg ggcc cccc cccc cxx"),
        new("GE", 22, [new('a', 2), new('n', 16)], "GEkk bbcc cccc cccc cccc cc"),
        new("DE", 22, [new('n', 8), new('n', 10)], "DEkk bbbb bbbb cccc cccc cc"),
        new("GI", 23, [new('a', 4), new('c', 15)], "GIkk bbbb cccc cccc cccc ccc"),
        new("GR", 27, [new('n', 7), new('c', 16)], "GRkk bbbs sssc cccc cccc cccc ccc"),
        new("GL", 18, [new('n', 4), new('n', 10)], "GLkk bbbb cccc cccc cc"),
        new("GT", 28, [new('c', 4), new('c', 4), new('c', 16)], "GTkk bbbb mmtt cccc cccc cccc cccc"),
        new("HU", 28, [new('n', 8), new('n', 16)], "HUkk bbbs sssk cccc cccc cccc cccx"),
        new("IS", 26, [new('n', 6), new('n', 16)], "ISkk bbbb sscc cccc iiii iiii ii"),
        new("IE", 22, [new('a', 4), new('n', 6), new('n', 8)], "IEkk aaaa bbbb bbcc cccc cc"),
        new("IL", 23, [new('n', 6), new('n', 13)], "ILkk bbbn nncc cccc cccc ccc"),
        new("IR", 26, [new('n', 22)], "IRkk bbbb cccc cccc cccc cccc cc"),
        new("IT", 27, [new('a', 1), new('n', 10), new('c', 12)], "ITkk xaaa aabb bbbc cccc cccc ccc"),
        new("JO", 30, [new('a', 4), new('n', 4), new('n', 18)], "JOkk bbbb nnnn cccc cccc cccc cccc cc"),
        new("KZ", 20, [new('n', 3), new('c', 13)], "KZkk bbbc cccc cccc cccc"),
        new("XK", 20, [new('n', 4), new('n', 12)], "XKkk bbbb cccc cccc cccc"),
        new("KW", 30, [new('a', 4), new('c', 22)], "KWkk bbbb cccc cccc cccc cccc cccc cc"),
        new("LV", 21, [new('a', 4), new('c', 13)], "LVkk bbbb cccc cccc cccc c"),
        new("LB", 28, [new('n', 4), new('c', 20)], "LBkk bbbb cccc cccc cccc cccc cccc"),
        new("LI", 21, [new('n', 5), new('c', 12)], "LIkk bbbb bccc cccc cccc c"),
        new("LT", 20, [new('n', 5), new('n', 11)], "LTkk bbbb bccc cccc cccc"),
        new("LU", 20, [new('n', 3), new('c', 13)], "LUkk bbbc cccc cccc cccc"),
        new("MK", 19, [new('n', 3), new('c', 10), new('n', 2)], "MKkk bbbc cccc cccc cxx"),
        new("MT", 31, [new('a', 4), new('n', 5), new('c', 18)], "MTkk bbbb ssss sccc cccc cccc cccc ccc"),
        new("MR", 27, [new('n', 10), new('n', 13)], "MRkk bbbb bsss sscc cccc cccc cxx"),
        new("MU", 30, [new('a', 4), new('n', 4), new('n', 15), new('a', 3)], "MUkk bbbb bbss cccc cccc cccc 000d dd"),
        new("MC", 27, [new('n', 10), new('c', 11), new('n', 2)], "MCkk bbbb bsss sscc cccc cccc cxx"),
        new("MD", 24, [new('c', 2), new('c', 18)], "MDkk bbcc cccc cccc cccc cccc"),
        new("ME", 22, [new('n', 3), new('n', 15)], "MEkk bbbc cccc cccc cccc xx"),
        new("NL", 18, [new('a', 4), new('n', 10)], "NLkk bbbb cccc cccc cc"),
        new("NO", 15, [new('n', 4), new('n', 7)], "NOkk bbbb cccc ccx"),
        new("PK", 24, [new('a', 4), new('n', 16)], "PKkk bbbb cccc cccc cccc cccc"),
        new("PS", 29, [new('a', 4), new('n', 9), new('n', 12)], "PSkk bbbb xxxx xxxx xccc cccc cccc c"),
        new("PL", 28, [new('n', 8), new('n', 16)], "PLkk bbbs sssx cccc cccc cccc cccc"),
        new("PT", 25, [new('n', 8), new('n', 13)], "PTkk bbbb ssss cccc cccc cccx x"),
        new("QA", 29, [new('a', 4), new('c', 21)], "QAkk bbbb cccc cccc cccc cccc cccc c"),
        new("RO", 24, [new('a', 4), new('c', 16)], "ROkk bbbb cccc cccc cccc cccc"),
        new("SM", 27, [new('a', 1), new('n', 10), new('c', 12)], "SMkk xaaa aabb bbbc cccc cccc ccc"),
        new("SA", 24, [new('n', 2), new('c', 18)], "SAkk bbcc cccc cccc cccc cccc"),
        new("RS", 22, [new('n', 3), new('n', 15)], "RSkk bbbc cccc cccc cccc xx"),
        new("SK", 24, [new('n', 10), new('n', 10)], "SKkk bbbb ssss sscc cccc cccc"),
        new("SI", 19, [new('n', 5), new('n', 10)], "SIkk bbss sccc cccc cxx"),
        new("ES", 24, [new('n', 10), new('n', 10)], "ESkk bbbb gggg xxcc cccc cccc"),
        new("SE", 24, [new('n', 3), new('n', 17)], "SEkk bbbc cccc cccc cccc cccc"),
        new("CH", 21, [new('n', 5), new('c', 12)], "CHkk bbbb bccc cccc cccc c"),
        new("TN", 24, [new('n', 5), new('n', 15)], "TNkk bbss sccc cccc cccc cccc"),
        new("TR", 26, [new('n', 5), new('n', 1), new('n', 16)], "TRkk bbbb bxcc cccc cccc cccc cc"),
        new("AE", 23, [new('n', 3), new('n', 16)], "AEkk bbbc cccc cccc cccc ccc"),
        new("GB", 22, [new('a', 4), new('n', 6), new('n', 8)], "GBkk bbbb ssss sscc cccc cc"),
        new("VG", 24, [new('a', 4), new('n', 16)], "VGkk bbbb cccc cccc cccc cccc"),
    ];

    public static readonly string[] Iso3166 =
    [
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS",
        "BT", "BV", "BW", "BY", "BZ", "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN",
        "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE",
        "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF",
        "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM",
        "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM",
        "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC",
        "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK",
        "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ", "NA",
        "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG",
        "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS",
        "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO",
        "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "US", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
        "VN", "VU", "WF", "WS", "XK", "YE", "YT", "ZA", "ZM", "ZW",
    ];
}
