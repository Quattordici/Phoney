using Phoney.Data;
using Phoney.Templates;

namespace Phoney.Tests;

/// <summary>Validates the imported faker.js data itself: loading, fallback and every template.</summary>
public sealed class LocaleDataTests
{
    [Fact]
    public void All_faker_locales_are_embedded()
    {
        Locales.All.Count.ShouldBeGreaterThanOrEqualTo(77);
        Locales.FakerVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Every_method_referenced_by_locale_templates_is_registered()
    {
        var missing = LocaleCatalog.TemplateMethodReferences.Where(m => !TemplateFunctions.Contains(m)).ToList();
        missing.ShouldBeEmpty();
    }

    [Theory]
    [ClassData(typeof(AllLocales))]
    public void Every_template_in_the_locale_data_evaluates(string locale)
    {
        var faker = new Faker(locale, seed: 7);
        var data = faker.Data;
        for (var key = 0; key < DataKeys.Count; key++)
        {
            var path = DataKeys.Paths[key];
            switch (data.Get(path))
            {
                case StringsEntry { HasTemplates: true } strings:
                    foreach (var template in strings.Values)
                    {
                        var result = faker.Parse(template);
                        result.ShouldNotContain("{{", customMessage: $"{locale}: '{path}' template '{template}'");
                    }

                    break;
                case RecordsEntry records:
                    for (var row = 0; row < records.Count; row++)
                    {
                        for (var field = 0; field < records.Fields.Count; field++)
                        {
                            if (records.Get(row, field) is { } value)
                                faker.Parse(value).ShouldNotContain("{{");
                        }
                    }

                    break;
            }
        }
    }

    [Fact]
    public void Fallback_is_resolved_per_entry_group_like_faker_js()
    {
        // de_AT defines its own person.first_name group, so none of it may come from de or en.
        var deAt = Locales.Data("de_AT");
        var en = Locales.Data("en");
        deAt.Get("person.first_name.female").ShouldNotBeSameAs(en.Get("person.first_name.female"));

        // A group de_AT lacks (person.sex) comes from the next locale in the chain that has it.
        deAt.Get("person.sex").ShouldBeSameAs(Locales.Data("de").Get("person.sex"));
        deAt.Get("person.sex").ShouldNotBeSameAs(en.Get("person.sex"));
    }

    [Fact]
    public void Explicitly_unavailable_data_is_not_filled_from_fallback_locales()
    {
        // faker.js marks az person.prefix as null: "not applicable", not "missing".
        var data = Locales.Data("az");
        data.Get("person.prefix").ShouldBe(UnavailableEntry.Instance);
        Should.Throw<PhoneyDataException>(() => new Faker("az").Person.Prefix()).Message.ShouldContain("isn't applicable");
    }

    [Theory]
    [InlineData("de-AT", "de_AT")]
    [InlineData("DE_at", "de_AT")]
    [InlineData("sv-SE", "sv")]
    [InlineData("en_US", "en_US")]
    [InlineData("pt-BR", "pt_BR")]
    public void Locale_codes_and_culture_names_resolve(string input, string expected) =>
        Locales.Get(input).Code.ShouldBe(expected);

    [Fact]
    public void Unknown_locale_lists_the_available_ones()
    {
        var ex = Should.Throw<ArgumentException>(() => new Faker("xx"));
        ex.Message.ShouldContain("Available locales");
        ex.Message.ShouldContain("sv");
    }
}
