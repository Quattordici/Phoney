using Phonery.Data;

namespace Phonery.Modules;

/// <summary>Names, job titles and other personal details (faker.js <c>person</c>).</summary>
public sealed class PersonModule : FakerModule
{
    internal PersonModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a first name; gendered when <paramref name="sex"/> is given and the locale has gendered names.</summary>
    public string FirstName(Sex? sex = null) =>
        Faker.Pick(Select(sex, DataKeys.PersonFirstNameGeneric, DataKeys.PersonFirstNameFemale, DataKeys.PersonFirstNameMale));

    /// <summary>Returns a last name; some locales (e.g. Slavic languages) have gendered last names.</summary>
    public string LastName(Sex? sex = null)
    {
        // Locales with last-name patterns (e.g. double-barrelled names) build the name from a weighted template.
        if (Faker.Data.Has(DataKeys.PersonLastNamePatternGeneric) || Faker.Data.Has(DataKeys.PersonLastNamePatternFemale) || Faker.Data.Has(DataKeys.PersonLastNamePatternMale))
            return Faker.Pick(Select(sex, DataKeys.PersonLastNamePatternGeneric, DataKeys.PersonLastNamePatternFemale, DataKeys.PersonLastNamePatternMale));
        return Faker.Pick(Select(sex, DataKeys.PersonLastNameGeneric, DataKeys.PersonLastNameFemale, DataKeys.PersonLastNameMale));
    }

    /// <summary>Returns a middle name.</summary>
    public string MiddleName(Sex? sex = null) =>
        Faker.Pick(Select(sex, DataKeys.PersonMiddleNameGeneric, DataKeys.PersonMiddleNameFemale, DataKeys.PersonMiddleNameMale));

    /// <summary>
    /// Returns a full name formatted by the locale's name patterns (which may include prefixes, middle names and suffixes).
    /// Pass <paramref name="firstName"/>/<paramref name="lastName"/> to keep parts consistent with other generated values.
    /// </summary>
    public string FullName(string? firstName = null, string? lastName = null, Sex? sex = null)
    {
        var s = sex ?? SexType();
        // Substitute the name parts ourselves (as faker.js does with mustache) so they share one sex and the
        // caller's first/last name are honoured; anything else in the pattern is evaluated afterwards.
        var result = RawPattern(DataKeys.PersonName);
        result = Substitute(result, "{{person.prefix}}", () => Prefix(s));
        result = Substitute(result, "{{person.firstName}}", () => firstName ?? FirstName(s));
        result = Substitute(result, "{{person.middleName}}", () => MiddleName(s));
        result = Substitute(result, "{{person.lastName}}", () => lastName ?? LastName(s));
        result = Substitute(result, "{{person.suffix}}", Suffix);
        return Faker.Parse(result);
    }

    /// <summary>Returns a gender identity, e.g. <c>Trans*Man</c>.</summary>
    public string Gender() => Faker.Pick(DataKeys.PersonGender);

    /// <summary>Returns the locale's word for a biological sex, e.g. <c>female</c>.</summary>
    public string Sex() => Faker.Pick(DataKeys.PersonSex);

    /// <summary>Returns <see cref="Modules.Sex.Female"/> or <see cref="Modules.Sex.Male"/>.</summary>
    public Sex SexType() => Random.Bool() ? Modules.Sex.Female : Modules.Sex.Male;

    /// <summary>Returns a short social media style bio, e.g. <c>coffee lover, developer 🙌</c>.</summary>
    public string Bio() => Faker.Pick(DataKeys.PersonBioPattern);

    /// <summary>Returns a name prefix such as <c>Mrs.</c> or <c>Dr.</c>.</summary>
    public string Prefix(Sex? sex = null) =>
        Faker.Pick(Select(sex, DataKeys.PersonPrefixGeneric, DataKeys.PersonPrefixFemale, DataKeys.PersonPrefixMale, DataKeys.PersonPrefix));

    /// <summary>Returns a name suffix such as <c>Jr.</c> or <c>PhD</c>.</summary>
    public string Suffix() => Faker.Pick(DataKeys.PersonSuffix);

    /// <summary>Returns a job title, e.g. <c>Global Accounts Engineer</c>.</summary>
    public string JobTitle() => Faker.Pick(DataKeys.PersonJobTitlePattern);

    /// <summary>Returns a job descriptor, e.g. <c>Senior</c>.</summary>
    public string JobDescriptor() => Faker.Pick(DataKeys.PersonJobDescriptor);

    /// <summary>Returns a job area, e.g. <c>Marketing</c>.</summary>
    public string JobArea() => Faker.Pick(DataKeys.PersonJobArea);

    /// <summary>Returns a job type, e.g. <c>Engineer</c>.</summary>
    public string JobType() => Faker.Pick(DataKeys.PersonJobType);

    /// <summary>Returns a western zodiac sign.</summary>
    public string ZodiacSign() => Faker.Pick(DataKeys.PersonWesternZodiacSign);

    /// <summary>
    /// Chooses between generic and gendered data the way faker.js does: with a sex, the gendered list is
    /// preferred (weighted 3·√n against √n of the generic list); without one, the generic list is used.
    /// </summary>
    /// <param name="sex">Requested sex; <see langword="null"/> picks one at random when only gendered data exists.</param>
    /// <param name="generic">Key of the sex-neutral list.</param>
    /// <param name="female">Key of the female list.</param>
    /// <param name="male">Key of the male list.</param>
    /// <param name="plain">Optional key used when the entry is a plain list rather than generic/female/male.</param>
    private StringsEntry Select(Sex? sex, int generic, int female, int male, int plain = -1)
    {
        var data = Faker.Data;
        var genericEntry = data.TryStrings(generic);
        if (sex is null)
        {
            if (genericEntry is not null)
                return genericEntry;
            sex = SexType();
        }

        var binaryEntry = data.TryStrings(sex == Modules.Sex.Female ? female : male);
        if (binaryEntry is not null)
        {
            if (genericEntry is null)
                return binaryEntry;
            var binaryWeight = 3 * Math.Sqrt(binaryEntry.Count);
            var genericWeight = Math.Sqrt(genericEntry.Count);
            return Random.Double() * (binaryWeight + genericWeight) < binaryWeight ? binaryEntry : genericEntry;
        }

        if (genericEntry is not null)
            return genericEntry;
        if (plain >= 0 && data.TryStrings(plain) is { } plainEntry)
            return plainEntry;
        throw data.Missing(plain >= 0 ? plain : generic, "strings");
    }

    /// <summary>Replaces <paramref name="placeholder"/> only when present, so unused parts consume no randomness and need no data.</summary>
    private static string Substitute(string pattern, string placeholder, Func<string> value) =>
        pattern.Contains(placeholder, StringComparison.Ordinal) ? pattern.Replace(placeholder, value(), StringComparison.Ordinal) : pattern;

    /// <summary>Picks a raw (unevaluated) pattern from a weighted or plain list.</summary>
    private string RawPattern(int key)
    {
        var entry = Faker.Data.Strings(key);
        return entry[entry.PickIndex(Random)];
    }
}
