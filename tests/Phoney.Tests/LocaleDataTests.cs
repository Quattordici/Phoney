using Phoney.Data;
using Phoney.Templates;

namespace Phoney.Tests;

/// <summary>Validates the imported faker.js data itself: loading, fallback and every template.</summary>
public sealed class LocaleDataTests
{
    [Test]
    public void All_faker_locales_are_embedded()
    {
        Locales.All.Count.ShouldBeGreaterThanOrEqualTo(77);
        Locales.FakerVersion.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Every_method_referenced_by_locale_templates_is_registered()
    {
        var missing = LocaleCatalog.TemplateMethodReferences.Where(m => !TemplateFunctions.Contains(m)).ToList();
        missing.ShouldBeEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
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

    [Test]
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

    [Test]
    public void Explicitly_unavailable_data_is_not_filled_from_fallback_locales()
    {
        // faker.js marks az person.prefix as null: "not applicable", not "missing".
        var data = Locales.Data("az");
        data.Get("person.prefix").ShouldBe(UnavailableEntry.Instance);
        Should.Throw<PhoneyDataException>(() => new Faker("az").Person.Prefix()).Message.ShouldContain("isn't applicable");
    }

    [Test]
    [Arguments("de-AT", "de_AT")]
    [Arguments("DE_at", "de_AT")]
    [Arguments("sv-SE", "sv")]
    [Arguments("en_US", "en_US")]
    [Arguments("pt-BR", "pt_BR")]
    [Arguments("en_AU_ocker", "en_AU_ocker")]
    [Arguments("de-AT-u-ca-gregory", "de_AT")]
    // Script-tagged and alias culture names (as .NET's CultureInfo.Name produces them)
    [Arguments("sr-Latn-RS", "sr_RS_latin")]
    [Arguments("sr-Latn", "sr_RS_latin")]
    [Arguments("uz-Latn-UZ", "uz_UZ_latin")]
    [Arguments("mn-MN", "mn_MN_cyrl")]
    [Arguments("mn-Cyrl-MN", "mn_MN_cyrl")]
    [Arguments("zh-Hans-CN", "zh_CN")]
    [Arguments("zh-Hant-TW", "zh_TW")]
    [Arguments("zh-TW", "zh_TW")]
    [Arguments("zh", "zh_CN")]
    [Arguments("en-Latn-US", "en_US")]
    [Arguments("ckb-IQ", "ku_ckb")]
    [Arguments("ku-Arab-IQ", "ku_ckb")]
    [Arguments("ku", "ku_kmr_latin")]
    [Arguments("kmr", "ku_kmr_latin")]
    // Closest locale of the language
    [Arguments("en-AU", "en_AU")]
    [Arguments("en-NZ", "en")]
    [Arguments("de-LU", "de")]
    [Arguments("pt-AO", "pt_BR")]
    public void Locale_codes_and_culture_names_resolve(string input, string expected) =>
        Locales.Get(input).Code.ShouldBe(expected);

    [Test]
    [Arguments("sr-Cyrl-RS")] // only Latin Serbian exists; never substitute another script
    [Arguments("uz-Cyrl")]
    [Arguments("mn-Mong-CN")]
    [Arguments("tlh")]
    public void Culture_names_without_a_matching_locale_are_rejected(string input) =>
        Locales.Exists(input).ShouldBeFalse();

    [Test]
    public void Faker_accepts_script_tagged_cultures() =>
        new Faker(new System.Globalization.CultureInfo("sr-Latn-RS")).Locale.ShouldBe("sr_RS_latin");

    [Test]
    public void Unknown_locale_lists_the_available_ones()
    {
        var ex = Should.Throw<ArgumentException>(() => new Faker("xx"));
        ex.Message.ShouldContain("Available locales");
        ex.Message.ShouldContain("sv");
    }
}
