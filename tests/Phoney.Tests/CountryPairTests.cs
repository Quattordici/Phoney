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

    private static ObjectGenerator<Consignment> Consignments(bool generated) =>
        (generated ? ShipmentFakes.Consignment : new ObjectGenerator<Consignment>(new ReflectionModel<Consignment>())).Seed(1).Locale("en");

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
    public void Generated_and_reflection_models_agree() =>
        Shipments(generated: false).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes))
            .ShouldBe(Shipments(generated: true).Generate(50).Select(s => (s.Country, s.CountryCode, s.City, s.Notes)));

    [Test]
    public void A_country_locale_keeps_its_own_country() =>
        Shipments(generated: true, locale: "de_AT").Generate(20).ShouldAllBe(s => s.Country == "Österreich" && s.CountryCode == "AT");

    [Test]
    public void A_locale_with_other_country_names_still_works() =>
        Shipments(generated: true, locale: "de").Generate(50).ShouldAllBe(s => s.Country.Length > 0 && s.CountryCode.Length == 2);

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
}
