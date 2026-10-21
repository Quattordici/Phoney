using Phoney.Modules;
using Phoney.Templates;

namespace Phoney.Tests;

public sealed class TemplateAndCustomizationTests
{
    private readonly Faker _faker = new("en", seed: 99);

    [Test]
    public void Parse_resolves_methods_data_paths_and_record_fields()
    {
        _faker.Parse("Hi {{person.firstName}}!").ShouldMatch("^Hi .+!$");
        _faker.Parse("{{person.last_name.generic}}").ShouldNotBeNullOrWhiteSpace();
        _faker.Parse("{{airline.airport.iataCode}}").ShouldMatch("^[A-Z]{3}$");
        _faker.Parse("no templates here").ShouldBe("no templates here");
        _faker.Parse("{{ not a template").ShouldBe("{{ not a template");
    }

    [Test]
    public void Parse_passes_arguments_like_faker_js()
    {
        _faker.Parse("{{string.numeric(6)}}").ShouldMatch("^\\d{6}$");
        _faker.Parse("{{number.int({\"min\": 5, \"max\": 7})}}").ShouldBeOneOf("5", "6", "7");
        _faker.Parse("{{helpers.arrayElement([\"a\",\"b\"])}}").ShouldBeOneOf("a", "b");
        _faker.Parse("{{helpers.fromRegExp([A-Z]{2}[0-9]{2})}}").ShouldMatch("^[A-Z]{2}\\d{2}$");
    }

    // The expression used to end at the first "}}", so an object inside the options (or "}}" in quoted text) broke it with "Missing closing parenthesis".
    [Test]
    public void Braces_inside_the_arguments_do_not_end_the_expression()
    {
        _faker.Parse("<{{helpers.arrayElement([\"}}\", \"{{x}}\"])}}>").ShouldBeOneOf("<}}>", "<{{x}}>");
        _faker.Parse("{{string.alpha({length: {min: 3, max: 3}})}}!").ShouldMatch("^[A-Za-z]{3}!$");
        _faker.Parse("{{person.firstName}}}").ShouldEndWith("}");
    }

    // An apostrophe in a plain-text argument is not a quote: two of them used to join the expressions between them into one.
    [Test]
    public void Apostrophes_in_plain_arguments_do_not_join_expressions() =>
        _faker.Parse("{{helpers.slugify(don't)}}-{{helpers.slugify(can't)}}").ShouldBe(HelpersModule.Slugify("don't") + "-" + HelpersModule.Slugify("can't"));

    [Test]
    public void Unknown_expressions_fail_with_a_clear_message() =>
        Should.Throw<PhoneyDataException>(() => _faker.Parse("{{nothing.here}}")).Message.ShouldContain("nothing.here");

    [Test]
    [Arguments("{{person.firstNme}}", "person.firstName")]
    [Arguments("{{persn.lastName}}", "person.lastName")]
    [Arguments("{{person.last_name.generc}}", "person.last_name.generic")]
    [Arguments("{{Person.FirstNme}}", "person.firstName")]
    public void Unresolved_expressions_suggest_the_closest_name(string template, string suggestion) =>
        Should.Throw<PhoneyDataException>(() => _faker.Parse(template)).Message.ShouldEndWith($"Did you mean '{suggestion}'?");

    [Test]
    public void Unknown_functions_suggest_the_closest_name() =>
        Should.Throw<FormatException>(() => _faker.Parse("{{string.numerc(6)}}")).Message.ShouldEndWith("Did you mean 'string.numeric'?");

    [Test]
    public void Unknown_data_paths_suggest_the_closest_path() =>
        Should.Throw<PhoneyDataException>(() => _faker.Pick("person.last_name.generc")).Message.ShouldBe(
            "Unknown data path 'person.last_name.generc'. Did you mean 'person.last_name.generic'?");

    [Test]
    public void Nothing_is_suggested_when_no_name_is_close() =>
        Should.Throw<PhoneyDataException>(() => _faker.Parse("{{nothing.here}}")).Message.ShouldNotContain("Did you mean");

    [Test]
    public void Self_referencing_data_fails_and_leaves_the_depth_limit_intact()
    {
        // A lone reference nests through Evaluate, one with text around it through AppendTo.
        Locales.Register("en_LOOP", l => l.FallbackTo("en")
            .Set("person.nickname", "{{person.nickname}}")
            .Set("person.bio_part", "a {{person.bio_part}} b"));
        var faker = new Faker("en_LOOP");

        foreach (var key in new[] { "person.nickname", "person.bio_part" })
        {
            Should.Throw<PhoneyDataException>(() => faker.Parse($"{{{{{key}}}}}")).Message.ShouldContain("too deep");
            Should.Throw<PhoneyDataException>(() => faker.Pick(key)).Message.ShouldContain("too deep");
            Template.Depth.ShouldBe(0);
        }
    }

    [Test]
    public void Custom_template_functions_can_be_registered()
    {
        TemplateFunctions.Register("test.sku", (f, _) => f.Random.ReplaceSymbols("SKU-####"));
        _faker.Parse("{{test.sku}}").ShouldMatch("^SKU-\\d{4}$");
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public void Static_facade_is_thread_safe()
    {
        var names = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 1000, _ => names.Add(Fake.Person.FullName()));
        names.Count.ShouldBe(1000);
        names.ShouldAllBe(n => !string.IsNullOrWhiteSpace(n));
    }
}
