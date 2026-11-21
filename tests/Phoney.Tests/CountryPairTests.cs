using Phoney.Data;
using Phoney.Generation;
using Phoney.Modules;

namespace Phoney.Tests;

public sealed class Shipment
{
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string City { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>Declares the code before the country.</summary>
public sealed class Consignment
{
    public string CountryCode { get; set; } = "";
    public string Country { get; set; } = "";
}

[FakeFor<Shipment>]
[FakeFor<Consignment>]
public static partial class ShipmentFakes;

/// <summary>In a language-wide locale (no country of its own) an object's country and country code still name the same country.</summary>
public sealed class CountryPairTests
{
    private static ObjectGenerator<Shipment> Shipments(bool generated, string locale = "en") =>
        (generated ? ShipmentFakes.Shipment : new ObjectGenerator<Shipment>(new ReflectionModel<Shipment>())).Seed(1).Locale(locale);

    private static ObjectGenerator<Consignment> Consignments(bool generated, string locale = "en") =>
        (generated ? ShipmentFakes.Consignment : new ObjectGenerator<Consignment>(new ReflectionModel<Consignment>())).Seed(1).Locale(locale);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Country_and_code_name_the_same_country(bool generated)
    {
        Shipments(generated).Generate(300).ShouldAllBe(s => CountryNames.CodeOf(s.Country) == s.CountryCode);
        Consignments(generated).Generate(300).ShouldAllBe(p => CountryNames.CodeOf(p.Country) == p.CountryCode);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populate_derives_the_missing_one_from_a_kept_one(bool generated)
    {
        Shipments(generated).Populate(new Shipment { Country = "Norway" }).CountryCode.ShouldBe("NO");
        Shipments(generated).Populate(new Shipment { CountryCode = "JP" }).Country.ShouldBe("Japan");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_country_from_a_rule_decides_the_code(bool generated)
    {
        Shipments(generated).With(s => s.Country, "Norway").Generate(20).ShouldAllBe(s => s.CountryCode == "NO");
        Shipments(generated).With(s => s.CountryCode, "JP").Generate(20).ShouldAllBe(s => s.Country == "Japan"); // a constant counts in any member order
        Consignments(generated).With(c => c.Country, "Norway").Generate(20).ShouldAllBe(c => c.CountryCode == "NO");
        Shipments(generated).With(s => s.Country, f => f.Random.Element<string>(["Norway", "Japan"])).Generate(50)
            .ShouldAllBe(s => CountryNames.CodeOf(s.Country) == s.CountryCode); // a factory when the country comes first
    }

    [Test]
    public void Generated_and_reflection_models_agree() =>
        Shipments(generated: false).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes))
            .ShouldBe(Shipments(generated: true).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes)));

    [Test]
    public void A_country_locale_keeps_its_own_country() =>
        Shipments(generated: true, locale: "de_AT").Generate(20).ShouldAllBe(s => s.Country == "Österreich" && s.CountryCode == "AT");

    [Test]
    public void A_locale_with_other_country_names_still_works() =>
        Shipments(generated: true, locale: "de").Generate(50).ShouldAllBe(s => s.Country.Length > 0 && s.CountryCode.Length == 2);

    [Test]
    [Arguments("de_AT", "Schweden", true)]
    [Arguments("de_AT", "Schweden", false)]
    [Arguments("en_US", "Sweden", true)]
    [Arguments("en_US", "Sweden", false)]
    public void In_a_country_locale_a_given_other_country_decides_the_other_member(string locale, string sweden, bool generated)
    {
        Shipments(generated, locale).With(s => s.Country, "Sweden").Generate(20).ShouldAllBe(s => s.CountryCode == "SE");
        Shipments(generated, locale).With(s => s.Country, sweden).Generate(20).ShouldAllBe(s => s.CountryCode == "SE");
        Shipments(generated, locale).With(s => s.CountryCode, "SE").Generate(20).ShouldAllBe(s => s.Country == sweden);
        Shipments(generated, locale).Populate(new Shipment { Country = sweden }).CountryCode.ShouldBe("SE");
        Shipments(generated, locale).Populate(new Shipment { CountryCode = "SE" }).Country.ShouldBe(sweden);
    }

    [Test]
    public void In_a_country_locale_its_own_country_in_its_own_language_keeps_its_code()
    {
        Shipments(generated: true, locale: "de_AT").With(s => s.Country, "Österreich").Generate(5).ShouldAllBe(s => s.CountryCode == "AT");
        Shipments(generated: true, locale: "de_AT").With(s => s.CountryCode, "AT").Generate(5).ShouldAllBe(s => s.Country == "Österreich");
    }

    [Test]
    public void A_given_country_moves_no_other_member()
    {
        var plain = Shipments(generated: true, locale: "de_AT").Generate(20);
        var given = Shipments(generated: true, locale: "de_AT").With(s => s.Country, "Sweden").Generate(20);

        given.Select(s => (s.City, s.Notes)).ShouldBe(plain.Select(s => (s.City, s.Notes)));
    }

    [Test]
    [Arguments("de", "Schweden", "Deutschland", true)]
    [Arguments("de", "Schweden", "Deutschland", false)]
    [Arguments("de_CH", "Schweden", "Deutschland", true)]
    [Arguments("sv", "Sverige", "Tyskland", true)]
    [Arguments("sv", "Sverige", "Tyskland", false)]
    public void In_another_language_a_given_code_gets_its_country_in_that_language(string locale, string sweden, string germany, bool generated)
    {
        Shipments(generated, locale).With(s => s.CountryCode, "SE").Generate(20).ShouldAllBe(s => s.Country == sweden);
        Shipments(generated, locale).With(s => s.CountryCode, "DE").Generate(20).ShouldAllBe(s => s.Country == germany);
        Shipments(generated, locale).Populate(new Shipment { CountryCode = "SE" }).Country.ShouldBe(sweden);
    }

    [Test]
    [Arguments("de", "Schweden", true)]
    [Arguments("de", "Schweden", false)]
    [Arguments("sv", "Sverige", true)]
    [Arguments("sv", "Sverige", false)]
    public void In_another_language_a_given_country_in_that_language_or_english_gets_its_code(string locale, string sweden, bool generated)
    {
        Shipments(generated, locale).With(s => s.Country, sweden).Generate(20).ShouldAllBe(s => s.CountryCode == "SE");
        Shipments(generated, locale).With(s => s.Country, "Sweden").Generate(20).ShouldAllBe(s => s.CountryCode == "SE");
        Shipments(generated, locale).Populate(new Shipment { Country = sweden }).CountryCode.ShouldBe("SE");
    }

    [Test]
    [Arguments("de", true)]
    [Arguments("de", false)]
    [Arguments("sv", true)]
    [Arguments("sv", false)]
    public void In_another_language_drawn_countries_and_codes_agree(string locale, bool generated)
    {
        // faker.js' lists name a few former countries without a code (Jugoslawien, Niederländische Antillen, Kosovo); those keep a drawn code.
        var names = CountryNames.For(locale);
        Shipments(generated, locale).Generate(300).ShouldAllBe(s => names.CodeOf(s.Country) == null || names.CodeOf(s.Country) == s.CountryCode);
        Consignments(generated, locale).Generate(300).ShouldAllBe(c => names.CodeOf(c.Country) == c.CountryCode);
    }

    [Test]
    [Arguments("de")]
    [Arguments("sv")]
    public void Generated_and_reflection_models_agree_in_another_language(string locale) =>
        Shipments(generated: false, locale).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes))
            .ShouldBe(Shipments(generated: true, locale).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes)));

    // The table was built from faker.js' English country list and checked against pycountry (ISO 3166-1); every English name and every code has exactly one partner.
    [Test]
    public void The_table_pairs_every_english_country_name_with_one_code()
    {
        var names = (StringsEntry)Locales.Data("en").Get("location.country")!;
        var codes = (RecordsEntry)Locales.Data("en").Get("location.country_code")!;
        var alpha2 = Enumerable.Range(0, codes.Count).Select(row => codes.Get(row, "alpha2")!).ToList();

        names.Values.Select(CountryNames.CodeOf).ShouldBe(names.Values.Select(CountryNames.CodeOf).Distinct());
        names.Values.Select(CountryNames.CodeOf).Order(StringComparer.Ordinal).ShouldBe(alpha2.Order(StringComparer.Ordinal));
        alpha2.ShouldAllBe(code => names.Values.Contains(CountryNames.NameOf(code)));
        CountryNames.CodeOf("Republic of Korea").ShouldBe("KR");
        CountryNames.CodeOf("Democratic People's Republic of Korea").ShouldBe("KP");
        CountryNames.NameOf("TR").ShouldBe("Turkey");
    }

    // Built from faker.js' German and Swedish lists, matched to codes with CLDR names (Babel) and cross-checked with the iso-codes translations
    // pycountry ships: every name with a code maps to it, and every code has one name, faker.js' own when its list has exactly one for it.
    [Test]
    [Arguments("de", "Jugoslawien|Niederländische Antillen")]
    [Arguments("sv", "Kosovo")]
    public void The_localized_table_pairs_the_locale_list_with_codes(string locale, string withoutCode)
    {
        var names = (StringsEntry)Locales.Data(locale).Get("location.country")!;
        var codes = (RecordsEntry)Locales.Data("en").Get("location.country_code")!;
        var alpha2 = Enumerable.Range(0, codes.Count).Select(row => codes.Get(row, "alpha2")!).ToList();
        var table = CountryNames.For(locale);
        var former = withoutCode.Split('|');

        names.Values.Where(name => !former.Contains(name)).ShouldAllBe(name => table.CodeOf(name) != null);
        former.ShouldAllBe(name => table.CodeOf(name) == null);
        alpha2.ShouldAllBe(code => table.CodeOf(table.NameOf(code)!) == code);
        alpha2.Where(code => names.Values.Count(name => table.CodeOf(name) == code) == 1)
            .ShouldAllBe(code => names.Values.Contains(table.NameOf(code)));
    }

    [Test]
    public void The_localized_tables_know_names_from_cldr_and_fall_back_to_english()
    {
        var german = CountryNames.For("de");
        var swedish = CountryNames.For("sv");

        german.CodeOf("Tschechien").ShouldBe("CZ"); // CLDR's name, next to faker.js' "Tschechische Republik"
        german.NameOf("CZ").ShouldBe("Tschechische Republik");
        german.NameOf("ME").ShouldBe("Montenegro"); // not in faker.js' German list
        german.CodeOf("Sweden").ShouldBe("SE");
        swedish.CodeOf("Svalbard").ShouldBe("SJ");
        swedish.CodeOf("Jan Mayen").ShouldBe("SJ");
        swedish.NameOf("SJ").ShouldBe("Svalbard och Jan Mayen"); // faker.js names both islands
        swedish.NameOf("BY").ShouldBe("Vitryssland");
        CountryNames.For("en").ShouldBeSameAs(CountryNames.For(null));
        CountryNames.For("fr").NameOf("SE").ShouldBe("Sweden"); // a language without a table
    }
}
