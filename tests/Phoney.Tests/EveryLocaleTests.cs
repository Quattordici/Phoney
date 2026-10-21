using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

// A typical entity, generated in every locale through the source-generated and the reflection model.
public sealed class LocaleProfile
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public int Age { get; set; }
    public string NationalId { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Company { get; set; } = "";
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public LocaleAddress Address { get; set; } = null!;
    public List<string> Tags { get; set; } = [];
}

public sealed class LocaleAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
}

[FakeFor<LocaleProfile>]
public static partial class LocaleProfileFakes;

/// <summary>Basic functionality, run in every shipped locale: objects in both models, a person that agrees with itself, populating and reproducible seeds.</summary>
public sealed class EveryLocaleTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<LocaleProfile> Generated(string locale) => LocaleProfileFakes.LocaleProfile.Locale(locale).Seed(11).ReferenceDate(Reference);

    private static ObjectGenerator<LocaleProfile> Reflected(string locale) =>
        new ObjectGenerator<LocaleProfile>(new ReflectionModel<LocaleProfile>()).Locale(locale).Seed(11).ReferenceDate(Reference);

    /// <summary>Every member, also the nested ones, as text that is the same for equal data on every machine.</summary>
    internal static string Describe(object value) => JsonSerializer.Serialize(value);

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
    public void Objects_are_complete_and_the_same_in_both_models(string locale)
    {
        var generated = Generated(locale).Generate(10);
        var reflected = Reflected(locale).Generate(10);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe), locale);
        var country = NationalIds.CountryOf(Locales.Get(locale));
        var hasNationalIds = country is not null && NationalIds.Supported(NationalIdKind.Person).Contains(country);
        var hasZipCodes = Locales.Data(locale).Has("location.postcode"); // en_HK has none
        foreach (var profile in generated)
        {
            // A national id where the country has a scheme, valid for it; empty where it has none.
            if (hasNationalIds)
                NationalIds.IsValid(NationalIdKind.Person, country!, profile.NationalId).ShouldBeTrue($"{locale}: {profile.NationalId}");
            else
                profile.NationalId.ShouldBeEmpty(locale);
            new[] { profile.FirstName, profile.LastName, profile.Email, profile.Username, profile.Phone, profile.JobTitle, profile.Company }
                .ShouldAllBe(text => !string.IsNullOrWhiteSpace(text) && !text.Contains("{{"), $"{locale}: {Describe(profile)}");
            (profile.Address.ZipCode.Length > 0).ShouldBe(hasZipCodes, locale);
            new[] { profile.Address.Street, profile.Address.City, profile.Address.Country }
                .ShouldAllBe(text => !string.IsNullOrWhiteSpace(text) && !text.Contains("{{"), $"{locale}: {Describe(profile.Address)}");
            profile.Id.ShouldNotBe(Guid.Empty);
            profile.Tags.ShouldNotBeEmpty();
        }
    }

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
    public void A_generated_person_agrees_with_itself(string locale)
    {
        var today = DateOnly.FromDateTime(Reference.UtcDateTime);
        foreach (var profile in Generated(locale).Generate(25))
        {
            // Emails are ASCII addresses whatever the script of the names, and the age is the age at the reference date.
            MailAddress.TryCreate(profile.Email, out _).ShouldBeTrue($"{locale}: {profile.Email}");
            profile.Email.ShouldAllBe(c => char.IsAscii(c), $"{locale}: {profile.Email}");
            profile.Username.ShouldAllBe(c => char.IsAscii(c), $"{locale}: {profile.Username}");
            profile.Age.ShouldBe(today.Year - profile.BirthDate.Year - (today < profile.BirthDate.AddYears(today.Year - profile.BirthDate.Year) ? 1 : 0), $"{locale}: {profile.BirthDate}");
            profile.CreatedAt.ShouldBeLessThanOrEqualTo(Reference);
            profile.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
    public void Populate_keeps_what_is_set_and_fills_the_rest(string locale)
    {
        var profile = new LocaleProfile { FirstName = "Kept", Address = new LocaleAddress { City = "Kept City" } };

        var filled = Generated(locale).Populate(profile);

        filled.FirstName.ShouldBe("Kept");
        filled.Address.City.ShouldBe("Kept City");
        filled.LastName.ShouldNotBeNullOrWhiteSpace(locale);
        filled.Address.Street.ShouldNotBeNullOrWhiteSpace(locale);
        Describe(Reflected(locale).Populate(new LocaleProfile { FirstName = "Kept", Address = new LocaleAddress { City = "Kept City" } })).ShouldBe(Describe(filled), locale);
    }

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
    public void Seeds_reproduce_the_data_and_other_seeds_change_it(string locale)
    {
        Fingerprint(new Faker(locale, seed: 4)).ShouldBe(Fingerprint(new Faker(locale, seed: 4)), locale);
        Fingerprint(new Faker(locale, seed: 5)).ShouldNotBe(Fingerprint(new Faker(locale, seed: 4)), locale);
        Generated(locale).Generate(3).Select(Describe).ShouldBe(Generated(locale).Generate(3).Select(Describe), locale);
    }

    /// <summary>
    /// The first person seed 11 gives in a few scripts, as recorded: the comparisons above would still pass if a change moved every seed's data alike.
    /// <see cref="CurrentCultureTests"/> checks the same values under other cultures.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> RecordedPeople = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["en"] = "Jose Torphy <Jose_Torphy@yahoo.com> Reaganworth",
        ["de"] = "Levent Szendrei <Levent_Szendrei@yahoo.com> Nikitaland",
        ["ja"] = "正雄 谷口 <l7nts4_rp3gkj@softbank.ne.jp> 港泉村",
        ["ar"] = "إياس جاوحدو <a191yas_jawhdw69@yahoo.com> بنغازي",
    };

    public static IEnumerable<string> RecordedLocales() => RecordedPeople.Keys.Order(StringComparer.Ordinal);

    /// <summary>The first person seed 11 gives in <paramref name="locale"/>, in one line.</summary>
    internal static string FirstPerson(string locale)
    {
        var profile = Generated(locale).Generate();
        return $"{profile.FirstName} {profile.LastName} <{profile.Email}> {profile.Address.City}";
    }

    [Test]
    [MethodDataSource(nameof(RecordedLocales))]
    public void A_seed_gives_the_data_it_always_gave(string locale) =>
        FirstPerson(locale).ShouldBe(RecordedPeople[locale]);

    [Test]
    [MethodDataSource(typeof(TestLocales), nameof(TestLocales.All))]
    public void The_facade_and_a_seeded_scope_use_the_locale(string locale)
    {
        string expected;
        using (Fake.Seeded(6, locale))
        {
            Fake.Faker.Locale.ShouldBe(locale);
            expected = Describe(Fake.One<LocaleProfile>());
        }

        using (Fake.Seeded(6, locale))
            Describe(Fake.One<LocaleProfile>()).ShouldBe(expected, locale);
    }

    /// <summary>
    /// The values of every locale-dependent generator (<see cref="SmokeTests"/>) for one faker, in order; data the locale marks as not applicable
    /// is a fixed placeholder, so it still draws nothing and compares equal.
    /// </summary>
    internal static string Fingerprint(Faker faker) =>
        string.Join('\n', SmokeTests.Generators.Select(g => $"{g.Name}={Value(() => g.Generate(faker))}"));

    private static string Value(Func<object> generate)
    {
        try
        {
            return Convert.ToString(generate(), CultureInfo.InvariantCulture) ?? "null";
        }
        catch (PhoneyDataException e)
        {
            return "unavailable: " + e.GetType().Name;
        }
    }
}
