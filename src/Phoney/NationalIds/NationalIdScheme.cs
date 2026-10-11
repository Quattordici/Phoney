using Phoney.Modules;

namespace Phoney;

/// <summary>What a national identifier identifies.</summary>
public enum NationalIdKind
{
    /// <summary>A natural person: personnummer, SSN, CPR, CPF…</summary>
    Person,

    /// <summary>A company or organisation in the national registry: organisationsnummer, EIN, CNPJ, SIREN…</summary>
    Company,

    /// <summary>A VAT number, including the country prefix: <c>SE806320405401</c>.</summary>
    Vat,
}

/// <summary>What a <see cref="NationalIdScheme"/> may use to build an identifier.</summary>
/// <param name="BirthDate">Date of birth that person identifiers encode (ignored by company and VAT schemes).</param>
/// <param name="Sex">Sex that person identifiers encode where the scheme does so.</param>
/// <param name="Formatted">Whether to write the identifier with its customary separators (<c>214200-3165</c>) instead of plain characters.</param>
public readonly record struct NationalIdRequest(DateOnly BirthDate, Sex Sex, bool Formatted);

/// <summary>
/// Generates and validates one kind of identifier of one country. Generation and validation live together so a
/// generated value can always be checked, and so custom schemes can be registered with <see cref="NationalIds.Register"/>.
/// </summary>
public abstract class NationalIdScheme
{
    /// <summary>Creates a scheme.</summary>
    /// <param name="country">ISO 3166 alpha-2 country code, e.g. <c>SE</c>.</param>
    /// <param name="kind">What the identifier identifies.</param>
    protected NationalIdScheme(string country, NationalIdKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        Country = country.Trim().ToUpperInvariant();
        Kind = kind;
    }

    /// <summary>ISO 3166 alpha-2 country code in upper case.</summary>
    public string Country { get; }

    /// <summary>What the identifier identifies.</summary>
    public NationalIdKind Kind { get; }

    /// <summary>Generates a valid identifier. Take all randomness from <paramref name="random"/> so seeding stays reproducible.</summary>
    public abstract string Generate(Randomizer random, NationalIdRequest request);

    /// <summary>Whether <paramref name="value"/> is a well-formed identifier with correct check digits (separators allowed).</summary>
    public abstract bool IsValid(string value);
}
