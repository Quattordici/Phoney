namespace Phoney.Modules;

/// <summary>
/// National and tax identifiers with valid check digits: personal identity numbers (personnummer, SSN, CPF…),
/// company registration numbers and VAT numbers. The country is the faker's locale unless you pass one.
/// </summary>
/// <remarks>
/// Values are structurally valid, not issued: they can coincide with real numbers. Use them for test data only.
/// Supported countries are listed by <see cref="NationalIds.Supported"/>; add your own with <see cref="NationalIds.Register"/>.
/// </remarks>
/// <example>
/// <code>
/// var faker = new Faker("sv", seed: 1);
/// faker.NationalId.Person();                          // "19560703-6190"
/// faker.NationalId.Person("NO", sex: Sex.Female);     // a fødselsnummer for a woman: DDMMYY IIIKK
/// faker.NationalId.Company();                         // "214200-3165"
/// faker.NationalId.Vat("GB");                         // "GB 154 4706 67"
/// </code>
/// </example>
public sealed class NationalIdModule : FakerModule
{
    internal NationalIdModule(Faker faker) : base(faker)
    {
    }

    /// <summary>
    /// Returns a personal identity number. It encodes <paramref name="birthDate"/> and <paramref name="sex"/> where the
    /// country's scheme does so (Sweden, Norway, Denmark, Finland, Poland, France).
    /// </summary>
    /// <param name="country">ISO 3166 alpha-2 country code; the locale's country when <see langword="null"/>.</param>
    /// <param name="birthDate">Date of birth; a random adult birthdate at the reference date when <see langword="null"/>.</param>
    /// <param name="sex">Sex; random when <see langword="null"/>.</param>
    /// <param name="formatted">Write the customary separators (<c>19560703-6190</c>) instead of plain characters.</param>
    /// <exception cref="ArgumentException"><paramref name="country"/> has no scheme.</exception>
    /// <exception cref="PhoneyDataUnavailableException">The locale's country has no scheme.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="birthDate"/> is outside the years the scheme can encode.</exception>
    public string Person(string? country = null, DateOnly? birthDate = null, Sex? sex = null, bool formatted = true) =>
        Build(Resolve(NationalIdKind.Person, country), birthDate, sex, formatted);

    /// <summary>Returns a company registration number (organisationsnummer, EIN, CNPJ, SIREN…).</summary>
    /// <inheritdoc cref="Person" path="/param[@name='country' or @name='formatted']"/>
    /// <inheritdoc cref="Person" path="/exception"/>
    public string Company(string? country = null, bool formatted = true) =>
        Build(Resolve(NationalIdKind.Company, country), null, null, formatted);

    /// <summary>Returns a VAT number including the country prefix, e.g. <c>SE806320405401</c>.</summary>
    /// <inheritdoc cref="Person" path="/param[@name='country' or @name='formatted']"/>
    /// <inheritdoc cref="Person" path="/exception"/>
    public string Vat(string? country = null, bool formatted = true) =>
        Build(Resolve(NationalIdKind.Vat, country), null, null, formatted);

    /// <summary>The personal identity number of the locale's country for the given person, or <see langword="null"/> when the country has no scheme.</summary>
    internal string? TryPerson(DateOnly birthDate, Sex sex)
    {
        if (!TryResolve(NationalIdKind.Person, null, out var scheme))
            return null;
        return scheme.Generate(Random, new NationalIdRequest(birthDate, sex, true));
    }

    private string Build(NationalIdScheme scheme, DateOnly? birthDate, Sex? sex, bool formatted)
    {
        // Persons always get a sex and birth date, even from schemes that ignore them, so a call consumes the
        // same randomness whatever the country's scheme encodes.
        var s = scheme.Kind == NationalIdKind.Person ? sex ?? Faker.Person.Sex() : default;
        var b = scheme.Kind == NationalIdKind.Person ? birthDate ?? Faker.Date.Birthdate() : default;
        return scheme.Generate(Random, new NationalIdRequest(b, s, formatted));
    }

    private NationalIdScheme Resolve(NationalIdKind kind, string? country)
    {
        if (country is not null)
            return NationalIds.Get(kind, country);
        if (TryResolve(kind, null, out var scheme))
            return scheme;
        throw new PhoneyDataUnavailableException(
            $"There is no {kind} identifier scheme for locale '{Faker.Locale}'. Pass a country: {string.Join(", ", NationalIds.Supported(kind))}.");
    }

    private bool TryResolve(NationalIdKind kind, string? country, out NationalIdScheme scheme)
    {
        var code = country ?? NationalIds.CountryOf(Faker.Data.Info);
        scheme = code is null ? null! : NationalIds.Find(kind, code)!;
        return scheme is not null;
    }
}
