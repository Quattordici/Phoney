using System.Globalization;
using System.Runtime.CompilerServices;
using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Names, job titles and other personal details (faker.js <c>person</c>).</summary>
public sealed class PersonModule : FakerModule
{
    internal PersonModule(Faker faker) : base(faker)
    {
    }

    /// <summary>
    /// Returns a whole, coherent person: the names match the sex, the email and username are built from the names,
    /// and the age matches the birthdate at <see cref="Phoney.Faker.ReferenceDate"/>.
    /// </summary>
    /// <param name="sex">The person's sex; random when not given.</param>
    /// <param name="minAge">Minimum age.</param>
    /// <param name="maxAge">Maximum age.</param>
    public PersonProfile Profile(Sex? sex = null, int minAge = 18, int maxAge = 80)
    {
        var s = sex ?? Sex();
        var firstName = FirstName(s);
        var lastName = LastName(s);
        var birthDate = Faker.Date.Birthdate(minAge, maxAge);
        var age = Generation.Identity.AgeAt(birthDate, DateOnly.FromDateTime(Faker.ReferenceDate.UtcDateTime));
        var fullName = FullName(firstName, lastName, s);
        var email = Faker.Internet.Email(firstName, lastName);
        var username = Faker.Internet.Username(firstName, lastName);
        var phone = Faker.Phone.Number();
        var jobTitle = JobTitle();
        var address = Faker.Location.Address();
        // Last, so the values above are the same as before the identity number existed.
        var nationalId = Faker.NationalId.TryPerson(birthDate, s);
        return new PersonProfile(s, firstName, lastName, fullName, email, username, phone, birthDate, age, jobTitle, address, nationalId);
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
        var s = sex ?? Sex();
        // Substitute the name parts ourselves (as faker.js does with mustache) so they share one sex and the
        // caller's first/last name are honoured; anything else in the pattern is evaluated afterwards.
        // Each part is drawn only when the pattern has it, always in this order, so unused parts consume no randomness and need no data.
        var entry = Faker.Data.Strings(DataKeys.PersonName);
        var pattern = entry.Parsed(entry.PickIndex(Random), NamePattern.Parse); // split into parts once per pattern, not per name
        var prefix = pattern.Has(NamePart.Prefix) ? Prefix(s) : null;
        var first = pattern.Has(NamePart.FirstName) ? firstName ?? FirstName(s) : null;
        var middle = pattern.Has(NamePart.MiddleName) ? MiddleName(s) : null;
        var last = pattern.Has(NamePart.LastName) ? lastName ?? LastName(s) : null;
        var suffix = pattern.Has(NamePart.Suffix) ? Suffix() : null;

        var name = new Templates.TextBuilder(stackalloc char[128]);
        string text;
        try
        {
            foreach (var (part, literal) in pattern.Parts)
            {
                name.Append(part switch
                {
                    NamePart.Prefix => prefix,
                    NamePart.FirstName => first,
                    NamePart.MiddleName => middle,
                    NamePart.LastName => last,
                    NamePart.Suffix => suffix,
                    _ => literal,
                });
            }

            text = name.Written.ToString();
        }
        finally
        {
            name.Dispose();
        }

        return Faker.Parse(text);
    }

    /// <summary>The parts of a name pattern <see cref="FullName"/> substitutes.</summary>
    private enum NamePart : byte
    {
        Literal,
        Prefix,
        FirstName,
        MiddleName,
        LastName,
        Suffix,
    }

    /// <summary>A name pattern split into literal text and the name parts in it, parsed once per pattern.</summary>
    private sealed class NamePattern
    {
        private readonly int _present;

        private NamePattern((NamePart Part, string? Literal)[] parts)
        {
            Parts = parts;
            foreach (var (part, _) in parts)
                _present |= 1 << (int)part;
        }

        public (NamePart Part, string? Literal)[] Parts { get; }

        public bool Has(NamePart part) => (_present & (1 << (int)part)) != 0;

        /// <summary>
        /// Splits <paramref name="pattern"/> at the five tags; any other <c>{{person.…}}</c> expression stays literal text, which <see cref="Faker.Parse"/>
        /// evaluates after the substitution.
        /// </summary>
        public static NamePattern Parse(string pattern)
        {
            var parts = new List<(NamePart, string?)>();
            var literal = new System.Text.StringBuilder();
            var rest = pattern.AsSpan();
            while (rest.IndexOf(TagStart, StringComparison.Ordinal) is var at and >= 0)
            {
                literal.Append(rest[..at]);
                rest = rest[at..];
                var tag = PartAt(rest);
                if (tag is var (part, length))
                {
                    if (literal.Length > 0)
                        parts.Add((NamePart.Literal, literal.ToString()));
                    literal.Clear();
                    parts.Add((part, null));
                    rest = rest[length..];
                }
                else
                {
                    literal.Append(TagStart);
                    rest = rest[TagStart.Length..];
                }
            }

            literal.Append(rest);
            if (literal.Length > 0)
                parts.Add((NamePart.Literal, literal.ToString()));
            return new NamePattern([.. parts]);

            static (NamePart Part, int Length)? PartAt(ReadOnlySpan<char> text)
            {
                foreach (var (tag, part) in Tags)
                {
                    if (text.StartsWith(tag, StringComparison.Ordinal))
                        return (part, tag.Length);
                }

                return null;
            }
        }

        private static readonly (string Tag, NamePart Part)[] Tags =
        [
            (PrefixTag, NamePart.Prefix), (FirstNameTag, NamePart.FirstName), (MiddleNameTag, NamePart.MiddleName),
            (LastNameTag, NamePart.LastName), (SuffixTag, NamePart.Suffix),
        ];
    }

    private const string TagStart = "{{person.";
    private const string PrefixTag = "{{person.prefix}}";
    private const string FirstNameTag = "{{person.firstName}}";
    private const string MiddleNameTag = "{{person.middleName}}";
    private const string LastNameTag = "{{person.lastName}}";
    private const string SuffixTag = "{{person.suffix}}";

    /// <summary>Returns a gender identity, e.g. <c>Trans*Man</c>.</summary>
    public string Gender() => Faker.Pick(DataKeys.PersonGender);

    /// <summary>Returns <see cref="Modules.Sex.Female"/> or <see cref="Modules.Sex.Male"/>.</summary>
    public Sex Sex() => Random.Bool() ? Modules.Sex.Female : Modules.Sex.Male;

    /// <summary>Returns the locale's word for a biological sex, e.g. <c>female</c> or <c>kvinna</c>.</summary>
    public string SexLabel() => Faker.Pick(DataKeys.PersonSex);

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
            sex = Sex();
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
}

/// <summary>A coherent fake person, see <see cref="PersonModule.Profile"/>.</summary>
/// <param name="Sex">Sex; the names are chosen for it.</param>
/// <param name="FirstName">First name.</param>
/// <param name="LastName">Last name.</param>
/// <param name="FullName">Full name in the locale's format, built from the first and last name (may add a prefix or suffix).</param>
/// <param name="Email">Email address built from the names.</param>
/// <param name="Username">Username built from the names.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="BirthDate">Date of birth.</param>
/// <param name="Age">Age in whole years at the faker's reference date.</param>
/// <param name="JobTitle">Job title.</param>
/// <param name="Address">Home address.</param>
/// <param name="NationalId">
/// Personal identity number of the locale's country that encodes <paramref name="BirthDate"/> and <paramref name="Sex"/>
/// where the country's scheme does so; <see langword="null"/> when the country has no scheme (see <see cref="NationalIds"/>).
/// </param>
public sealed record PersonProfile(
    Sex Sex,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string Username,
    string Phone,
    DateOnly BirthDate,
    int Age,
    string JobTitle,
    AddressProfile Address,
    string? NationalId = null);
