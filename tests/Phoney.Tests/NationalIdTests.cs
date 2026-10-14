using System.Globalization;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>An object whose identity members must describe one person.</summary>
public sealed class Citizen
{
    public string FirstName { get; set; } = "";
    public Sex Sex { get; set; }
    public DateOnly BirthDate { get; set; }
    public string Personnummer { get; set; } = "";
    public string CompanyNumber { get; set; } = "";
    public string VatNumber { get; set; } = "";
}

/// <summary>
/// National identifiers: generated values pass their own validator and independent implementations
/// (the vectors below were cross-checked against python-stdnum), and encode birth date and sex.
/// </summary>
public sealed class NationalIdTests
{
    private static readonly DateOnly Reference = new(2025, 1, 1);

    private static readonly (NationalIdKind Kind, string[] Countries)[] WaveOne =
    [
        (NationalIdKind.Person, ["BR", "CA", "DK", "ES", "FI", "FR", "GB", "NL", "NO", "PL", "SE", "US"]),
        (NationalIdKind.Company, ["BR", "DK", "FI", "FR", "NO", "PL", "SE", "US"]),
        (NationalIdKind.Vat, ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]),
    ];

    [Test]
    public void Wave_one_countries_are_supported()
    {
        foreach (var (kind, countries) in WaveOne)
            countries.ShouldBeSubsetOf(NationalIds.Supported(kind));
    }

    [Test]
    public void Generated_values_pass_their_scheme_in_both_formats()
    {
        var faker = new Faker("en", seed: 1);
        foreach (var (kind, countries) in WaveOne)
        {
            foreach (var country in countries)
            {
                for (var i = 0; i < 300; i++)
                {
                    var formatted = i % 2 == 0;
                    var value = kind switch
                    {
                        NationalIdKind.Person => faker.NationalId.Person(country, formatted: formatted),
                        NationalIdKind.Company => faker.NationalId.Company(country, formatted),
                        _ => faker.NationalId.Vat(country, formatted),
                    };
                    NationalIds.IsValid(kind, country, value).ShouldBeTrue($"{kind} {country} '{value}'");
                }
            }
        }
    }

    // Values confirmed valid by python-stdnum (the GB national insurance number has no stdnum module: it follows HMRC's published rules).
    [Test]
    [Arguments(NationalIdKind.Person, "SE", "19560703-6190")]
    [Arguments(NationalIdKind.Person, "SE", "196612056264")]
    [Arguments(NationalIdKind.Person, "SE", "560703-6190")]
    [Arguments(NationalIdKind.Person, "NO", "050479 05593")]
    [Arguments(NationalIdKind.Person, "NO", "14017724593")]
    [Arguments(NationalIdKind.Person, "DK", "080100-9638")]
    [Arguments(NationalIdKind.Person, "DK", "1407592472")]
    [Arguments(NationalIdKind.Person, "FI", "231178-7246")]
    [Arguments(NationalIdKind.Person, "FI", "100270-602J")]
    [Arguments(NationalIdKind.Person, "US", "464-32-0435")]
    [Arguments(NationalIdKind.Person, "US", "150347586")]
    [Arguments(NationalIdKind.Person, "GB", "RY 56 85 00 A")]
    [Arguments(NationalIdKind.Person, "GB", "PT526430A")]
    [Arguments(NationalIdKind.Person, "BR", "017.232.507-24")]
    [Arguments(NationalIdKind.Person, "BR", "37106846333")]
    [Arguments(NationalIdKind.Person, "PL", "63051433634")]
    [Arguments(NationalIdKind.Person, "PL", "55013193104")]
    [Arguments(NationalIdKind.Person, "NL", "760141186")]
    [Arguments(NationalIdKind.Person, "NL", "368108119")]
    [Arguments(NationalIdKind.Person, "ES", "34127600Q")]
    [Arguments(NationalIdKind.Person, "FR", "1 77 07 83 837 567 90")]
    [Arguments(NationalIdKind.Person, "FR", "299010177650364")]
    [Arguments(NationalIdKind.Person, "CA", "176-993-533")]
    [Arguments(NationalIdKind.Person, "CA", "745719369")]
    [Arguments(NationalIdKind.Company, "SE", "214200-3165")]
    [Arguments(NationalIdKind.Company, "NO", "938 836 671")]
    [Arguments(NationalIdKind.Company, "DK", "11 12 78 78")]
    [Arguments(NationalIdKind.Company, "FI", "0866400-4")]
    [Arguments(NationalIdKind.Company, "US", "87-1389293")]
    [Arguments(NationalIdKind.Company, "BR", "28.290.875/0001-94")]
    [Arguments(NationalIdKind.Company, "PL", "787364706")]
    [Arguments(NationalIdKind.Company, "FR", "609 819 693")]
    [Arguments(NationalIdKind.Vat, "SE", "SE806320405401")]
    [Arguments(NationalIdKind.Vat, "NO", "NO 969 020 734 MVA")]
    [Arguments(NationalIdKind.Vat, "NO", "NO919168021MVA")]
    [Arguments(NationalIdKind.Vat, "DK", "DK 23 62 90 89")]
    [Arguments(NationalIdKind.Vat, "FI", "FI10081641")]
    [Arguments(NationalIdKind.Vat, "GB", "GB 154 4706 67")]
    [Arguments(NationalIdKind.Vat, "GB", "GB861967677")]
    [Arguments(NationalIdKind.Vat, "PL", "PL 348-643-78-85")]
    [Arguments(NationalIdKind.Vat, "FR", "FR 82 581 066 560")]
    public void Known_valid_values_are_accepted(NationalIdKind kind, string country, string value) =>
        NationalIds.IsValid(kind, country, value).ShouldBeTrue();

    // One digit changed from a valid value: rejected by Phoney and by python-stdnum.
    [Test]
    [Arguments(NationalIdKind.Person, "SE", "19560703-6193")]
    [Arguments(NationalIdKind.Person, "SE", "196612056234")]
    [Arguments(NationalIdKind.Person, "NO", "050879 05593")]
    [Arguments(NationalIdKind.Person, "NO", "14014724593")]
    [Arguments(NationalIdKind.Person, "DK", "116254-1108")]
    [Arguments(NationalIdKind.Person, "FI", "236178-7246")]
    [Arguments(NationalIdKind.Person, "FI", "110270-602J")]
    [Arguments(NationalIdKind.Person, "US", "998-53-8618")]
    [Arguments(NationalIdKind.Person, "US", "953-46-3712")]
    [Arguments(NationalIdKind.Person, "GB", "DA 56 85 00 A")]
    [Arguments(NationalIdKind.Person, "GB", "BG 56 85 00 A")]
    [Arguments(NationalIdKind.Person, "GB", "RO 56 85 00 A")]
    [Arguments(NationalIdKind.Person, "GB", "RY 56 85 00 E")]
    [Arguments(NationalIdKind.Person, "BR", "017.212.507-24")]
    [Arguments(NationalIdKind.Person, "PL", "63051433637")]
    [Arguments(NationalIdKind.Person, "NL", "060141186")]
    [Arguments(NationalIdKind.Person, "ES", "34027600Q")]
    [Arguments(NationalIdKind.Person, "FR", "1 77 07 83 834 567 90")]
    [Arguments(NationalIdKind.Person, "CA", "176-913-533")]
    [Arguments(NationalIdKind.Person, "CA", "046-454-286")]
    [Arguments(NationalIdKind.Company, "SE", "294200-3165")]
    [Arguments(NationalIdKind.Company, "NO", "438 836 671")]
    [Arguments(NationalIdKind.Company, "DK", "11 82 78 78")]
    [Arguments(NationalIdKind.Company, "FI", "0266400-4")]
    [Arguments(NationalIdKind.Company, "US", "074248204")]
    [Arguments(NationalIdKind.Company, "BR", "18.290.875/0001-94")]
    [Arguments(NationalIdKind.Company, "PL", "787364006")]
    [Arguments(NationalIdKind.Company, "FR", "609 819 691")]
    [Arguments(NationalIdKind.Vat, "SE", "SE806330405401")]
    [Arguments(NationalIdKind.Vat, "NO", "NO 969 030 734 MVA")]
    [Arguments(NationalIdKind.Vat, "DK", "DK 23 62 90 87")]
    [Arguments(NationalIdKind.Vat, "FI", "FI10081841")]
    [Arguments(NationalIdKind.Vat, "GB", "GB 154 4786 67")]
    [Arguments(NationalIdKind.Vat, "PL", "PL 548-643-78-85")]
    [Arguments(NationalIdKind.Vat, "FR", "FR 82 581 066 568")]
    [Arguments(NationalIdKind.Person, "SE", "")]
    [Arguments(NationalIdKind.Person, "SE", "not a number")]
    public void Known_invalid_values_are_rejected(NationalIdKind kind, string country, string value) =>
        NationalIds.IsValid(kind, country, value).ShouldBeFalse();

    [Test]
    public void Swedish_coordination_numbers_use_day_plus_sixty()
    {
        // Same person as 19560703-6190, but with the day written as 63: a samordningsnummer.
        var body = "560763619";
        var number = body + HelpersModule.LuhnCheckDigit(body);
        NationalIds.IsValid(NationalIdKind.Person, "SE", number).ShouldBeTrue();
        NationalIds.IsValid(NationalIdKind.Person, "SE", "560793619" + HelpersModule.LuhnCheckDigit("560793619")).ShouldBeFalse();
    }

    [Test]
    public void Brazilian_numbers_made_of_one_repeated_digit_are_rejected()
    {
        NationalIds.IsValid(NationalIdKind.Person, "BR", "111.111.111-11").ShouldBeFalse();
        NationalIds.IsValid(NationalIdKind.Company, "BR", "00.000.000/0000-00").ShouldBeFalse();
    }

    [Test]
    public void Person_ids_encode_birth_date_and_sex_across_the_supported_years()
    {
        var faker = new Faker("en", seed: 3);
        var random = new System.Random(11);
        foreach (var (country, from, to) in new[] { ("SE", 1800, 2099), ("NO", 1854, 2039), ("DK", 1858, 2057), ("FI", 1800, 2099), ("PL", 1800, 2299), ("FR", 1900, 2025) })
        {
            for (var i = 0; i < 400; i++)
            {
                var date = new DateOnly(random.Next(from, to + 1), random.Next(1, 13), 1).AddDays(random.Next(0, 28));
                var sex = i % 2 == 0 ? Sex.Male : Sex.Female;
                var value = faker.NationalId.Person(country, date, sex, formatted: i % 3 != 0);

                var (decoded, decodedSex) = Decode(country, country == "FI" ? value : Compact(value)); // the sign of a henkilötunnus is part of it
                if (country != "FR")
                    decoded.ShouldBe(date, $"{country} {value}");
                else
                    (decoded.Month, decoded.Year % 100).ShouldBe((date.Month, date.Year % 100), $"{country} {value}");
                decodedSex.ShouldBe(sex, $"{country} {value}");
            }
        }
    }

    [Test]
    public void Birth_years_a_scheme_cannot_encode_are_rejected()
    {
        var faker = new Faker("en", seed: 1);
        Should.Throw<ArgumentOutOfRangeException>(() => faker.NationalId.Person("SE", new DateOnly(1799, 12, 31)));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.NationalId.Person("NO", new DateOnly(2040, 1, 1)));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.NationalId.Person("DK", new DateOnly(1857, 1, 1)));
    }

    [Test]
    [Arguments("sv", "SE")]
    [Arguments("da", "DK")]
    [Arguments("fi", "FI")]
    [Arguments("nb_NO", "NO")]
    [Arguments("pl", "PL")]
    [Arguments("pt_BR", "BR")]
    [Arguments("fr_CA", "CA")]
    [Arguments("en_GB", "GB")]
    [Arguments("en", "US")]
    [Arguments("sv-SE", "SE")]
    public void The_locale_decides_the_country(string locale, string country)
    {
        var faker = new Faker(locale, seed: 5);
        NationalIds.IsValid(NationalIdKind.Person, country, faker.NationalId.Person()).ShouldBeTrue();
    }

    [Test]
    public void A_locale_whose_country_has_no_scheme_reports_it_and_an_explicit_country_still_works()
    {
        var faker = new Faker("ja", seed: 5);
        Should.Throw<PhoneyDataUnavailableException>(() => faker.NationalId.Person());
        Should.Throw<PhoneyDataUnavailableException>(() => faker.NationalId.Vat());
        NationalIds.IsValid(NationalIdKind.Vat, "SE", faker.NationalId.Vat("se")).ShouldBeTrue();
    }

    [Test]
    public void An_unsupported_explicit_country_lists_the_supported_ones()
    {
        var faker = new Faker("en", seed: 5);
        var error = Should.Throw<ArgumentException>(() => faker.NationalId.Person("ZZ"));
        error.Message.ShouldContain("SE");
        Should.Throw<ArgumentException>(() => NationalIds.IsValid(NationalIdKind.Vat, "US", "x"));
        NationalIds.Find(NationalIdKind.Vat, "US").ShouldBeNull();
    }

    [Test]
    public void Seeded_values_are_reproducible_and_do_not_depend_on_the_reference_date()
    {
        new Faker("sv", seed: 9).NationalId.Person().ShouldBe(new Faker("sv", seed: 9).NationalId.Person());
        new Faker("sv", seed: 9).NationalId.Vat().ShouldBe(new Faker("sv", seed: 9).NationalId.Vat());
        new Faker("sv", seed: 9).NationalId.Person().ShouldNotBe(new Faker("sv", seed: 10).NationalId.Person());
    }

    [Test]
    public void Profile_has_an_identity_number_that_encodes_its_birth_date_and_sex()
    {
        var faker = new Faker("sv", seed: 21);
        for (var i = 0; i < 100; i++)
        {
            var p = faker.Person.Profile();
            p.NationalId.ShouldNotBeNull();
            var (date, sex) = Decode("SE", Compact(p.NationalId));
            date.ShouldBe(p.BirthDate);
            sex.ShouldBe(p.Sex);
        }

        new Faker("de", seed: 21).Person.Profile().NationalId.ShouldBeNull();
    }

    [Test]
    public void Members_of_one_object_agree_on_the_person_and_company_ids()
    {
        var citizens = Fake.For<Citizen>().Locale("sv").Seed(4).Generate(100);
        foreach (var c in citizens)
        {
            var (date, sex) = Decode("SE", Compact(c.Personnummer));
            date.ShouldBe(c.BirthDate);
            sex.ShouldBe(c.Sex);
            NationalIds.IsValid(NationalIdKind.Company, "SE", c.CompanyNumber).ShouldBeTrue();
            NationalIds.IsValid(NationalIdKind.Vat, "SE", c.VatNumber).ShouldBeTrue();
        }
    }

    [Test]
    public void Id_members_are_empty_in_a_locale_without_a_scheme()
    {
        var citizen = Fake.For<Citizen>().Locale("ja").Seed(4).Generate();
        (citizen.Personnummer, citizen.CompanyNumber, citizen.VatNumber).ShouldBe(("", "", ""));
    }

    [Test]
    public void Schemes_can_be_registered_and_replaced()
    {
        NationalIds.Register(new EmployeeNumber());
        try
        {
            var faker = new Faker("en", seed: 1);
            var value = faker.NationalId.Company("XE");
            value.ShouldMatch("^E-[0-9]{5}$");
            NationalIds.IsValid(NationalIdKind.Company, "xe", value).ShouldBeTrue();
            NationalIds.Supported(NationalIdKind.Company).ShouldContain("XE");
        }
        finally
        {
            NationalIds.Remove(NationalIdKind.Company, "XE");
        }
        NationalIds.Find(NationalIdKind.Company, "XE").ShouldBeNull();
    }

    [Test]
    public void Templates_can_generate_ids()
    {
        var faker = new Faker("sv", seed: 2);
        NationalIds.IsValid(NationalIdKind.Person, "SE", faker.Parse("{{NationalId.Person}}")).ShouldBeTrue();
        NationalIds.IsValid(NationalIdKind.Vat, "GB", faker.Parse("{{nationalId.vat(GB)}}")).ShouldBeTrue();
    }

    private sealed class EmployeeNumber : NationalIdScheme
    {
        public EmployeeNumber() : base("XE", NationalIdKind.Company)
        {
        }

        public override string Generate(SeededRandom random, NationalIdRequest request) => "E-" + random.Replace("#####");

        public override bool IsValid(string value) => value.Length == 7 && value.StartsWith("E-", StringComparison.Ordinal);
    }

    private static string Compact(string value) => value.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Replace("+", "", StringComparison.Ordinal);

    /// <summary>Reads birth date and sex back out of an identifier, independently of the generator.</summary>
    private static (DateOnly Date, Sex Sex) Decode(string country, string d)
    {
        int N(int start, int length) => int.Parse(d.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
        switch (country)
        {
            case "SE": // YYYYMMDDNNNC
                return (new DateOnly(N(0, 4), N(4, 2), N(6, 2)), N(10, 1) % 2 == 1 ? Sex.Male : Sex.Female);
            case "NO": // DDMMYYIIIKK
            {
                int yy = N(4, 2), serial = N(6, 3);
                var year = serial < 500 ? 1900 + yy : serial < 750 && yy >= 54 ? 1800 + yy : 2000 + yy;
                return (new DateOnly(year, N(2, 2), N(0, 2)), N(8, 1) % 2 == 1 ? Sex.Male : Sex.Female);
            }

            case "DK": // DDMMYYSSSS
            {
                int yy = N(4, 2), century = d[6] - '0';
                var year = century <= 3 ? 1900 + yy : century is 4 or 9 ? (yy <= 36 ? 2000 : 1900) + yy : (yy <= 57 ? 2000 : 1800) + yy;
                return (new DateOnly(year, N(2, 2), N(0, 2)), N(9, 1) % 2 == 1 ? Sex.Male : Sex.Female);
            }

            case "FI": // DDMMYYCZZZQ
            {
                var year = (d[6] switch { '+' => 1800, '-' => 1900, _ => 2000 }) + N(4, 2);
                return (new DateOnly(year, N(2, 2), N(0, 2)), N(7, 3) % 2 == 1 ? Sex.Male : Sex.Female);
            }

            case "PL": // YYMMDDZZZXQ
            {
                int month = N(2, 2);
                var century = month > 80 ? 1800 : month > 60 ? 2200 : month > 40 ? 2100 : month > 20 ? 2000 : 1900;
                return (new DateOnly(century + N(0, 2), month % 20, N(4, 2)), N(9, 1) % 2 == 1 ? Sex.Male : Sex.Female);
            }

            case "FR": // SYYMMDDDCCCOOOKK: only the year and month are encoded
                return (new DateOnly(1900 + N(1, 2), N(3, 2), 1), d[0] == '1' ? Sex.Male : Sex.Female);
            default:
                throw new ArgumentOutOfRangeException(nameof(country));
        }
    }
}
