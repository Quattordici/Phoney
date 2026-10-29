using Phoney.Data;
using Phoney.Modules;

namespace Phoney.Generation;

/// <summary>
/// The person (and home) an object describes, created on first use so unrelated objects pay nothing. It first collects what rules give and what a populated
/// object keeps (<see cref="Give"/>, <see cref="Adopt"/>), which draw nothing; <see cref="Settle"/> then draws the person's sex, and from there each part is
/// generated when first needed, so conventions only consume randomness for members the type actually has.
/// </summary>
internal sealed class Identity(Faker faker)
{
    private Sex? _sex;
    private Sex? _givenSex;
    private DateOnly? _givenBirthDate;
    private string? _firstName;
    private string? _lastName;
    private string? _username;
    private string? _email;
    private DateOnly? _birthDate;
    private int? _age;
    private AddressProfile? _address;
    private string? _country;
    private string? _countryCode;
    private string? _nationalId;

    /// <summary>Whether the person is drawn (see <see cref="Settle"/>).</summary>
    public bool IsSettled { get; private set; }

    /// <summary>Draws the person's sex, unless a kept value says it; every part drawn after it relies on it. Does nothing when already settled.</summary>
    public Identity Settle()
    {
        if (!IsSettled)
        {
            if (_sex is null)
            {
                var drawn = faker.Person.Sex(); // drawn even when a rule gave the sex, so everything drawn after it stays where it was
                _sex = _givenSex ?? drawn;
            }

            IsSettled = true;
        }

        return this;
    }

    /// <summary>The person's sex; drives gendered names, avatars and sex/gender enums.</summary>
    public Sex Sex => Settle()._sex!.Value;

    /// <summary>First name, matching <see cref="Sex"/>.</summary>
    public string FirstName => _firstName ??= faker.Person.FirstName(Sex);

    /// <summary>Last name, matching <see cref="Sex"/> where the locale has gendered last names.</summary>
    public string LastName => _lastName ??= faker.Person.LastName(Sex);

    /// <summary>Username derived from the names.</summary>
    public string Username => _username ??= faker.Internet.Username(FirstName, LastName);

    /// <summary>Email address derived from the names.</summary>
    public string Email => _email ??= faker.Internet.Email(FirstName, LastName);

    /// <summary>Date of birth (18–80 years before the reference date).</summary>
    public DateOnly BirthDate => _birthDate ??= DrawBirthDate();

    /// <summary>The birthdate: one draw whatever decides it, then a birthdate a rule gave replaces the drawn one.</summary>
    private DateOnly DrawBirthDate()
    {
        var drawn = _age is { } age ? faker.Date.Birthdate(age, age) : faker.Date.Birthdate();
        return _givenBirthDate ?? drawn;
    }

    /// <summary>
    /// Takes in a value a rule gives a member: an age (a whole number the birthdate is drawn for, unless the birthdate is drawn, given or kept; a negative
    /// age, or one too old for the reference date, is left to the rule), a birthdate (a date; it wins over a given age), a sex (a <see cref="Sex"/> or an enum with
    /// <c>Female</c> and <c>Male</c> members; anything else is ignored), or a country name or code (the other country member then matches it). The first one
    /// counts, and only while that part of the person isn't drawn yet. A given birthdate or sex replaces the drawn one but its draw still happens.
    /// </summary>
    public void Give(GivenValue given, object? value)
    {
        switch (given)
        {
            case GivenValue.Age:
                GiveAge(value);
                break;
            case GivenValue.BirthDate:
                if (_birthDate is null)
                    _givenBirthDate ??= DateOf(value);
                break;
            case GivenValue.Sex:
                if (_sex is null)
                    _givenSex ??= SexOf(value);
                break;
            default:
                GiveCountry(given, value);
                break;
        }
    }

    /// <summary>
    /// Takes in a value that is kept as it is while populating (a set <c>FirstName</c>, birthdate or sex/gender enum), so generated values that belong to the
    /// same person (email, username, age) agree with it. Must be called before any value is generated.
    /// </summary>
    public void Adopt(FakeMember member, object? value)
    {
        if (value is null)
            return;
        switch (member.Convention)
        {
            case ConventionKind.FirstName when value is string first:
                _firstName = first;
                break;
            case ConventionKind.LastName when value is string last:
                _lastName = last;
                break;
            case ConventionKind.Country when value is string { Length: > 0 } country:
                _country = country;
                break;
            case ConventionKind.CountryCode when value is string { Length: > 0 } code:
                _countryCode = code;
                break;
            case ConventionKind.Age:
                GiveAge(value); // the birthdate is drawn for it, unless a kept birthdate says otherwise
                break;
            case ConventionKind.BirthDate:
                _birthDate = DateOf(value) ?? _birthDate;
                break;
            default:
                // A sex/gender enum with Female and Male members says which names and avatar fit the person.
                if (value is Enum e && (member.Convention is ConventionKind.Sex or ConventionKind.Gender || IsSexEnum(e.GetType())))
                    _sex = SexOf(e) ?? _sex;
                break;
        }
    }

    private static DateOnly? DateOf(object? value) => value switch
    {
        DateOnly d => d,
        DateTime t => DateOnly.FromDateTime(t),
        DateTimeOffset o => DateOnly.FromDateTime(o.UtcDateTime),
        _ => null,
    };

    /// <summary>Whether <see cref="Give"/> can use <paramref name="value"/> as <paramref name="given"/>, for the kinds that are drawn where their member is (a birthdate, a sex).</summary>
    internal static bool CanGive(GivenValue given, object? value) => given switch
    {
        GivenValue.BirthDate => DateOf(value) is not null,
        GivenValue.Sex => SexOf(value) is not null,
        _ => false,
    };

    /// <summary>The sex an enum value names (its <c>Female</c> or <c>Male</c> member, whatever the enum is called), or <see langword="null"/> for anything else.</summary>
    private static Sex? SexOf(object? value)
    {
        if (value is not Enum e)
            return null;
        var name = e.ToString();
        if (name.Equals("Female", StringComparison.OrdinalIgnoreCase))
            return Modules.Sex.Female;
        return name.Equals("Male", StringComparison.OrdinalIgnoreCase) ? Modules.Sex.Male : null;
    }

    /// <summary>Whether <paramref name="type"/> (or the type under a nullable) is an enum whose name says it holds the person's sex or gender.</summary>
    internal static bool IsSexEnum(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum
            && (type.Name.Contains("Gender", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Sex", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Makes <paramref name="value"/> (a whole number) the age the birthdate is drawn for, while the birthdate isn't drawn yet. The first one counts;
    /// a negative age, or one too old for the reference date, is left to the rule and the birthdate drawn as usual.
    /// </summary>
    private void GiveAge(object? value)
    {
        int? given = value switch
        {
            int i => i,
            long l and >= 0 and <= int.MaxValue => (int)l,
            short h => h,
            byte b => b,
            sbyte sb => sb,
            ushort us => us,
            uint ui and <= int.MaxValue => (int)ui,
            _ => null,
        };
        if (given is not { } age || age < 0 || age >= faker.ReferenceDate.UtcDateTime.Year)
            return;
        if (_birthDate is null)
            _age ??= age;
    }

    /// <summary>
    /// Makes a given country name or code the one the other country member matches, while that one isn't produced yet. Before the identity is settled each
    /// of the two is kept on its own; once it is, the first one counts and the other is left to match it.
    /// </summary>
    private void GiveCountry(GivenValue given, object? value)
    {
        if (value is not string { Length: > 0 } text)
            return;
        if (!IsSettled)
        {
            if (given == GivenValue.Country)
                _country ??= text;
            else
                _countryCode ??= text;
            return;
        }

        if (_country is not null || _countryCode is not null)
            return;
        _country = given == GivenValue.Country ? text : null;
        _countryCode = given == GivenValue.CountryCode ? text : null;
    }

    /// <summary>Age in whole years at the reference date, matching <see cref="BirthDate"/>.</summary>
    public int Age => AgeAt(BirthDate, DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime));

    /// <summary>One address, so street, city, zip code, state and country agree.</summary>
    public AddressProfile Address => _address ??= faker.Location.Address();

    /// <summary>
    /// The address's country, or in a locale without a country of its own (<c>en</c>, <c>de</c>…) a drawn one that names the same country as a
    /// <see cref="CountryCode"/> the object already has. It is drawn either way, so every member after it draws what it drew before.
    /// </summary>
    public string Country()
    {
        if (Address.Country is { } own)
            return own;
        var drawn = faker.Location.Country();
        if (_countryCode is { } code && CountryNames.NameOf(code) is { } name && faker.Data.TryStrings(DataKeys.LocationCountry) is { } names && Array.IndexOf((string[])names.Values, name) >= 0)
            return name;
        _country ??= drawn;
        return drawn;
    }

    /// <summary>The address's country code, or a drawn one that matches a <see cref="Country"/> the object already has (see there).</summary>
    public string CountryCode()
    {
        if (Address.CountryCode is { } own)
            return own;
        var drawn = faker.Location.CountryCode();
        if (_country is { } country && CountryNames.CodeOf(country) is { } code)
            return code;
        _countryCode ??= drawn;
        return drawn;
    }

    /// <summary>The locale country's personal identity number, encoding <see cref="BirthDate"/> and <see cref="Sex"/>; empty when the country has no scheme.</summary>
    public string NationalId => _nationalId ??= faker.NationalId.TryPerson(BirthDate, Sex) ?? "";

    /// <summary>Whole years between <paramref name="birthDate"/> and <paramref name="today"/>.</summary>
    internal static int AgeAt(DateOnly birthDate, DateOnly today) =>
        today.Year - birthDate.Year - (today < birthDate.AddYears(today.Year - birthDate.Year) ? 1 : 0);
}
