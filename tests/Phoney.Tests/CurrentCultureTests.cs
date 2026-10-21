using System.Globalization;

namespace Phoney.Tests;

/// <summary>
/// Seeded data is the same whatever the current .NET culture of the process: cultures with other casing rules (Turkish dotless i),
/// calendars (Thai Buddhist, Persian, Umm al-Qura), digits and number formats (decimal commas, the Swedish minus sign) must not change a value.
/// </summary>
/// <remarks>Under invariant globalization (a CI run) every culture behaves like the invariant one, so these tests then only check that nothing depends on it either.</remarks>
public sealed class CurrentCultureTests
{
    private static readonly string[] Cultures = ["tr-TR", "az-Latn-AZ", "th-TH", "fa-IR", "ar-SA", "sv-SE", "de-DE", "uk-UA", "ja-JP"];

    public static IEnumerable<string> CultureNames() => Cultures;

    /// <summary>Runs <paramref name="produce"/> with <paramref name="culture"/> as the current culture and UI culture.</summary>
    private static T In<T>(string culture, Func<T> produce)
    {
        var (previous, previousUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return produce();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (previous, previousUi);
        }
    }

    private static T Invariant<T>(Func<T> produce) => In(CultureInfo.InvariantCulture.Name, produce);

    [Test]
    [MethodDataSource(nameof(CultureNames))]
    public void Values_of_every_locale_do_not_depend_on_the_current_culture(string culture)
    {
        foreach (var locale in TestLocales.All())
        {
            var expected = Invariant(() => EveryLocaleTests.Fingerprint(new Faker(locale, seed: 21)));

            In(culture, () => EveryLocaleTests.Fingerprint(new Faker(locale, seed: 21))).ShouldBe(expected, $"{locale} under {culture}");
        }
    }

    [Test]
    [MethodDataSource(nameof(CultureNames))]
    public void Objects_of_every_locale_do_not_depend_on_the_current_culture(string culture)
    {
        var seeded = LocaleProfileFakes.LocaleProfile.Seed(21); // the same seed in every locale on purpose
        foreach (var locale in TestLocales.All())
        {
            string Generate() => string.Join('\n', seeded.Locale(locale).Generate(3).Select(EveryLocaleTests.Describe));

            In(culture, Generate).ShouldBe(Invariant(Generate), $"{locale} under {culture}");
        }
    }

    [Test]
    [MethodDataSource(nameof(CultureNames))]
    public void Formatted_numbers_dates_and_templates_do_not_depend_on_the_current_culture(string culture)
    {
        static string Produce()
        {
            var faker = new Faker("en", seed: 3) { ReferenceDate = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero) };
            return string.Join('|',
                faker.Commerce.PriceText("$", 1, 50),
                faker.Finance.AmountText(-1000, 1000, 2, "€"),
                faker.Number.Double(-10, 10, 3).ToString(CultureInfo.InvariantCulture),
                faker.Parse("{{number.float({\"min\": -5, \"max\": 5, \"fractionDigits\": 2})}} {{date.past}} {{date.month}}"),
                faker.Internet.Email("İsmail", "IŞIK"),
                faker.Internet.Username("Irmak", "Işıl"),
                Modules.HelpersModule.Slugify("Idaho Iİıi"),
                faker.String.Alpha(10, Modules.Casing.Lower),
                faker.Color.Rgb(includeAlpha: true).ToCss(),
                faker.System.Semver(),
                faker.Location.Latitude().ToString(CultureInfo.InvariantCulture),
                faker.Date.Past().ToString("O", CultureInfo.InvariantCulture));
        }

        In(culture, Produce).ShouldBe(Invariant(Produce), culture);
    }

    [Test]
    [MethodDataSource(nameof(CultureNames))]
    public void A_culture_resolves_to_a_locale_of_its_language(string culture) =>
        new Faker(new CultureInfo(culture)).Locale.ShouldStartWith(culture[..2]);
}
