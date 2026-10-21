using System.Text;

namespace Phoney.Tests.Adversarial;

/// <summary>Registering and resolving locales in ways that used to destroy data or fail with a null reference: overwriting a built-in locale, cycles, nulls and data that refers to itself.</summary>
public sealed class LocaleAbuseTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Test]
    [Arguments("en")]
    [Arguments("EN")]
    [Arguments("de-AT")]
    public void Registering_over_a_built_in_locale_is_refused_instead_of_wiping_its_data(string code)
    {
        var error = Should.Throw<ArgumentException>(() => Locales.Register(code, l => l.Set("person.first_name.generic", "Hijack")));

        error.Message.ShouldContain("built-in");
        error.Message.ShouldContain("Locales.Extend");
        new Faker("en", seed: 1).Person.LastName().ShouldNotBeNullOrWhiteSpace(); // en and everything that falls back to it keep their data
        new Faker("en_US", seed: 1).Commerce.ProductName().ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void A_user_locale_can_still_be_registered_again()
    {
        Locales.Register("abuse_twice", l => l.FallbackTo("en").Set("person.first_name.generic", "First"));
        Locales.Register("abuse_twice", l => l.FallbackTo("en").Set("person.first_name.generic", "Second"));

        new Faker("abuse_twice", seed: 1).Person.FirstName().ShouldBe("Second");
    }

    [Test]
    public void A_locale_that_would_fall_back_to_itself_is_refused()
    {
        Locales.Register("abuse_cycle_a", _ => { });
        Locales.Register("abuse_cycle_b", l => l.FallbackTo("abuse_cycle_a"));

        Should.Throw<ArgumentException>(() => Locales.Register("abuse_cycle_a", l => l.FallbackTo("abuse_cycle_b")))
            .Message.ShouldContain("falls back to itself");
        Locales.Get("abuse_cycle_b").Fallback.ShouldBe(["abuse_cycle_a", "en", "base"]);
    }

    [Test]
    public void A_null_fallback_list_or_stream_is_an_argument_null_error()
    {
        Should.Throw<ArgumentNullException>(() => Locales.Register("abuse_null", Json("{}"), null!)).ParamName.ShouldBe("fallback");
        Should.Throw<ArgumentNullException>(() => Locales.Register("abuse_null", null!, "en"));
        Locales.Exists("abuse_null").ShouldBeFalse();
    }

    [Test]
    public void Broken_locale_json_is_a_json_error_and_registers_nothing()
    {
        Should.Throw<System.Text.Json.JsonException>(() => Locales.Register("abuse_bad_json", Json("{\"person\":")));
        Should.Throw<System.Text.Json.JsonException>(() => Locales.Register("abuse_deep_json", Json(new string('[', 10_000) + new string(']', 10_000))));

        Locales.Exists("abuse_bad_json").ShouldBeFalse();
        Locales.Exists("abuse_deep_json").ShouldBeFalse();
    }

    [Test]
    public void A_null_locale_is_an_argument_null_error_and_an_empty_one_names_the_choices()
    {
        Should.Throw<ArgumentNullException>(() => new Faker((string)null!, seed: 1));
        Should.Throw<ArgumentException>(() => new Faker("", seed: 1)).Message.ShouldContain("Available locales");
        Should.Throw<ArgumentException>(() => new Faker("xx_YY", seed: 1)).Message.ShouldContain("Available locales");
    }

    [Test]
    [Arguments("EN_us", "en_US")]
    [Arguments("en-US", "en_US")]
    [Arguments(" en ", "en")]
    [Arguments("sv-SE", "sv")]
    public void Locale_codes_in_other_spellings_resolve(string code, string expected) =>
        new Faker(code, seed: 1).Locale.ShouldBe(expected);

    [Test]
    public void Data_that_refers_to_itself_is_reported_instead_of_overflowing_the_stack()
    {
        Locales.Register("abuse_self_ref", l => l.FallbackTo("en").Set("person.first_name.generic", "{{person.firstName}}"));
        Locales.Register("abuse_mutual_ref", l => l.FallbackTo("en")
            .Set("person.last_name.generic", "{{person.last_name_pattern}}")
            .Set("person.last_name_pattern", "{{person.last_name.generic}}"));

        Should.Throw<PhoneyDataException>(() => new Faker("abuse_self_ref", seed: 1).Person.FirstName()).Message.ShouldContain("refer");
        Should.Throw<PhoneyDataException>(() => new Faker("abuse_mutual_ref", seed: 1).Person.LastName()).Message.ShouldContain("refer");
    }

    [Test]
    public void Templates_that_are_broken_are_left_as_text_or_refused_with_the_template_named()
    {
        var faker = new Faker("en", seed: 1);

        faker.Parse("{{").ShouldBe("{{");
        faker.Parse("}}").ShouldBe("}}");
        faker.Parse("{{}}").ShouldBe("{{}}");
        faker.Parse("{{ }}").ShouldBe("{{ }}");
        faker.Parse("{{person.firstName}}😀{{").ShouldEndWith("😀{{");
        Should.Throw<FormatException>(() => faker.Parse("{{person.firstName(}}")).Message.ShouldContain("{{person.firstName(}}");
        Should.Throw<PhoneyDataException>(() => faker.Parse("{{nope.nope}}")).Message.ShouldContain("nope.nope");
        Should.Throw<ArgumentNullException>(() => faker.Parse(null!));
    }

    [Test]
    public void A_template_with_a_hundred_thousand_placeholders_is_filled()
    {
        var text = new Faker("en", seed: 1).Parse(string.Concat(Enumerable.Repeat("{{person.firstName}} ", 100_000)));

        text.ShouldNotContain("{{");
        text.Count(c => c == ' ').ShouldBeGreaterThanOrEqualTo(100_000);
    }
}
