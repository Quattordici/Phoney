using Phonery.Templates;

namespace Phonery.Tests;

public sealed class TemplateAndCustomizationTests
{
    private readonly Faker _faker = new("en", seed: 99);

    [Fact]
    public void Parse_resolves_methods_data_paths_and_record_fields()
    {
        _faker.Parse("Hi {{person.firstName}}!").ShouldMatch("^Hi .+!$");
        _faker.Parse("{{person.last_name.generic}}").ShouldNotBeNullOrWhiteSpace();
        _faker.Parse("{{airline.airport.iataCode}}").ShouldMatch("^[A-Z]{3}$");
        _faker.Parse("no templates here").ShouldBe("no templates here");
        _faker.Parse("{{ not a template").ShouldBe("{{ not a template");
    }

    [Fact]
    public void Parse_passes_arguments_like_faker_js()
    {
        _faker.Parse("{{string.numeric(6)}}").ShouldMatch("^\\d{6}$");
        _faker.Parse("{{number.int({\"min\": 5, \"max\": 7})}}").ShouldBeOneOf("5", "6", "7");
        _faker.Parse("{{helpers.arrayElement([\"a\",\"b\"])}}").ShouldBeOneOf("a", "b");
        _faker.Parse("{{helpers.fromRegExp([A-Z]{2}[0-9]{2})}}").ShouldMatch("^[A-Z]{2}\\d{2}$");
    }

    [Fact]
    public void Unknown_expressions_fail_with_a_clear_message() =>
        Should.Throw<PhoneryDataException>(() => _faker.Parse("{{nothing.here}}")).Message.ShouldContain("nothing.here");

    [Fact]
    public void Custom_template_functions_can_be_registered()
    {
        TemplateFunctions.Register("test.sku", (f, _) => f.Random.Replace("SKU-####"));
        _faker.Parse("{{test.sku}}").ShouldMatch("^SKU-\\d{4}$");
    }

    [Fact]
    public void Custom_locales_fall_back_to_their_parent()
    {
        Locales.Register("sv_test", l => l
            .FallbackTo("sv")
            .Title("Swedish (test)")
            .Set("person.first_name.generic", "Kalle")
            .Set("person.first_name.female", "Kalle")
            .Set("person.first_name.male", "Kalle")
            .Set("person.nickname", "Kalle Anka"));

        var faker = new Faker("sv_test");
        faker.Person.FirstName().ShouldBe("Kalle");
        faker.Pick("person.nickname").ShouldBe("Kalle Anka");
        faker.Parse("{{person.nickname}}").ShouldBe("Kalle Anka");
        Locales.Data("sv").Get("location.city_name").ShouldBeSameAs(faker.Data.Get("location.city_name"));
    }

    [Fact]
    public void Custom_locales_can_be_imported_from_faker_js_json()
    {
        const string json = """
            {
              "metadata": { "title": "Pirate" },
              "person": { "first_name": { "generic": ["Jack", "Anne"] } },
              "company": { "name_pattern": ["{{person.firstName}}'s Crew"] },
              "system": { "mime_type": { "x/treasure": { "extensions": ["gold", "map"] } } }
            }
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        Locales.Register("en_PIRATE", stream);

        var faker = new Faker("en_PIRATE", seed: 1);
        faker.Person.FirstName().ShouldBeOneOf("Jack", "Anne");
        faker.Company.Name().ShouldMatch("^(Jack|Anne)'s Crew$");
        faker.System.FileExt("x/treasure").ShouldBeOneOf("gold", "map");
        Locales.Get("en_PIRATE").Title.ShouldBe("Pirate");
    }

    [Fact]
    public void Seeded_fakers_are_reproducible()
    {
        static string Sample(Faker f) =>
            $"{f.Person.FullName()}|{f.Internet.Email()}|{f.Location.StreetAddress()}|{f.Finance.Iban()}|{f.Lorem.Sentence()}";

        var reference = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var a = new Faker("de", seed: 2024) { ReferenceDate = reference };
        var b = new Faker("de", seed: 2024) { ReferenceDate = reference };
        for (var i = 0; i < 20; i++)
            Sample(a).ShouldBe(Sample(b));
    }

    [Fact]
    public void Static_facade_is_thread_safe()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 1000, _ => names.Add(Fake.Person.FullName()));
        names.Count.ShouldBe(1000);
        names.ShouldAllBe(n => !string.IsNullOrWhiteSpace(n));
    }
}
