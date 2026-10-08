using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>The .NET-first APIs: profiles, typed custom data, .NET-style template names and image sources.</summary>
public sealed class DotNetApiTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en")]
    [InlineData("sv")]
    [InlineData("de_AT")]
    [InlineData("ru")]
    [InlineData("en_HK")] // no postal codes in this locale
    public void Profiles_are_coherent(string locale)
    {
        var faker = new Faker(locale, seed: 3) { ReferenceDate = Reference };
        for (var i = 0; i < 25; i++)
        {
            var p = faker.Person.Profile();
            p.FullName.ShouldContain(p.FirstName);
            p.FullName.ShouldContain(p.LastName);
            p.Email.ShouldContain("@");
            p.Age.ShouldBeInRange(18, 80);
            var expectedAge = Reference.Year - p.BirthDate.Year - (DateOnly.FromDateTime(Reference.UtcDateTime) < p.BirthDate.AddYears(Reference.Year - p.BirthDate.Year) ? 1 : 0);
            p.Age.ShouldBe(expectedAge);
            p.Address.Street.ShouldNotBeNullOrWhiteSpace();
            p.Address.City.ShouldNotBeNullOrWhiteSpace();
            p.Address.CountryCode.ShouldBe(Locales.Get(locale).Country);
        }
    }

    [Fact]
    public void Profile_names_follow_the_requested_sex()
    {
        var faker = new Faker("sv", seed: 1);
        var female = Locales.Data("sv").Get("person.first_name.female").ShouldBeOfType<Phoney.Data.StringsEntry>().Values;
        for (var i = 0; i < 25; i++)
        {
            var p = faker.Person.Profile(Sex.Female, minAge: 30, maxAge: 40);
            p.Sex.ShouldBe(Sex.Female);
            female.ShouldContain(p.FirstName);
            p.Age.ShouldBeInRange(30, 40);
        }
    }

    [Fact]
    public void Templates_accept_dotnet_style_names()
    {
        var faker = new Faker(seed: 2);
        faker.Parse("{{Person.FirstName}} {{Person.LastName}}").ShouldMatch(@"^\S.* \S.*$");
        faker.Parse("{{String.Numeric(4)}}").ShouldMatch(@"^\d{4}$");
        faker.Parse("{{Color.Hex}}").ShouldMatch("^#[0-9a-f]{6}$");
        faker.Parse("{{Location.Address}}").ShouldContain(",");
        faker.Parse("{{Helpers.FromRegExp([A-Z]{2})}}").ShouldMatch("^[A-Z]{2}$");
        // faker.js names (used by the locale data) keep working.
        faker.Parse("{{person.firstName}}").ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Typed_locale_data_replaces_only_what_is_set()
    {
        Locales.Register("sv_typed", l => l
            .FallbackTo("sv")
            .FirstNames(Sex.Female, "Astrid", "Greta")
            .Cities("Mora", "Falun")
            .EmailDomains("example.se")
            .ZipCodeFormats("### ##"));

        var faker = new Faker("sv_typed", seed: 4);
        var svMale = Locales.Data("sv").Get("person.first_name.male").ShouldBeOfType<Phoney.Data.StringsEntry>().Values;
        for (var i = 0; i < 25; i++)
        {
            faker.Person.FirstName(Sex.Female).ShouldBeOneOf("Astrid", "Greta");
            svMale.ShouldContain(faker.Person.FirstName(Sex.Male)); // inherited, not wiped out by the female list
            faker.Location.City().ShouldBeOneOf("Mora", "Falun");
            faker.Internet.Email().ShouldEndWith("@example.se");
            faker.Location.ZipCode().ShouldMatch(@"^\d{3} \d{2}$");
        }
    }

    [Fact]
    public void First_names_without_sex_replace_all_first_names()
    {
        Locales.Register("sv_only", l => l.FallbackTo("sv").FirstNames("Kim"));
        var faker = new Faker("sv_only");
        faker.Person.FirstName().ShouldBe("Kim");
        faker.Person.FirstName(Sex.Female).ShouldBe("Kim");
        faker.Person.FirstName(Sex.Male).ShouldBe("Kim");
    }

    [Fact]
    public void Extending_a_locale_keeps_the_rest_of_the_group()
    {
        Locales.Register("en_extend", l => l.FallbackTo("en"));
        Locales.Extend("en_extend", l => l.FirstNames(Sex.Female, "Ada"));
        var faker = new Faker("en_extend", seed: 5);
        var female = Enumerable.Range(0, 200).Select(_ => faker.Person.FirstName(Sex.Female)).ToList();
        var male = Enumerable.Range(0, 200).Select(_ => faker.Person.FirstName(Sex.Male)).ToList();

        female.ShouldContain("Ada");        // the female list was replaced…
        male.ShouldNotContain("Ada");       // …the male list was not…
        female.Distinct().Count().ShouldBeGreaterThan(1); // …and English sex-neutral names still mix in
    }

    [Fact]
    public void Image_urls_use_replaceable_templates()
    {
        var faker = new Faker(seed: 6);
        try
        {
            faker.Image.Url(640, 480).ShouldStartWith("https://picsum.photos/seed/");
            ImageSources.UseHost("https://images.test/");
            faker.Image.Url(640, 480).ShouldMatch(@"^https://images\.test/photos/\w+/640x480$");
            faker.Image.Avatar(Sex.Female, 128).ShouldMatch(@"^https://images\.test/avatars/female/128/\d+\.jpg$");
        }
        finally
        {
            ImageSources.Reset();
        }

        faker.Image.DataUri(10, 10, base64: true).ShouldStartWith("data:image/svg+xml;base64,");
    }

    [Fact]
    public void Card_numbers_are_valid_for_every_issuer_in_every_locale()
    {
        foreach (var locale in new[] { "en", "sv", "zh_CN", "el" })
        {
            var faker = new Faker(locale, seed: 7);
            foreach (var issuer in Enum.GetValues<CardIssuer>())
                HelpersModule.LuhnCheck(faker.Finance.CreditCardNumber(issuer)).ShouldBeTrue($"{locale} {issuer}");
        }
    }

    [Fact]
    public void Weighted_picks_follow_the_weights()
    {
        var random = new Randomizer(8);
        var gold = Enumerable.Range(0, 4000).Count(_ => random.Weighted(("gold", 1.0), ("silver", 3.0)) == "gold");
        gold.ShouldBeInRange(800, 1200);
        Should.Throw<ArgumentException>(() => random.Weighted(("x", 0.0)));
    }
}
