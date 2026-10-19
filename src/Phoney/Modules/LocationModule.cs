using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Addresses, places and coordinates (faker.js <c>location</c>).</summary>
public sealed class LocationModule : FakerModule
{
    internal LocationModule(Faker faker) : base(faker)
    {
    }

    /// <summary>
    /// Returns an address as separate parts. Parts the locale has no data for (e.g. states in Swedish) are
    /// <see langword="null"/>. Country-specific locales (<c>de_AT</c>, <c>en_US</c>…) put the address in their country,
    /// named in the locale's language (<c>Österreich</c>); language-wide locales (<c>de</c>, <c>sv</c>) leave it out.
    /// </summary>
    public AddressProfile Address()
    {
        var street = StreetAddress();
        var city = City();
        string? zipCode, state;
        try
        {
            zipCode = ZipCode();
        }
        catch (PhoneyDataUnavailableException)
        {
            zipCode = null;
        }

        try
        {
            state = State();
        }
        catch (PhoneyDataUnavailableException)
        {
            state = null;
        }

        var info = Faker.Data.Info;
        if (!ReferenceEquals(info, _countryOf))
            (_countryName, _countryOf) = (LocaleCountryName(info), info); // the locale's country name is parsed once per faker, not per address
        return new AddressProfile(street, city, zipCode, state, _countryName, info.Country);
    }

    private LocaleInfo? _countryOf;
    private string? _countryName;

    /// <summary>
    /// The locale's country in its own language, taken from faker.js' native locale name: <c>Deutsch (Österreich)</c>
    /// gives <c>Österreich</c>. <see langword="null"/> for language-wide locales.
    /// </summary>
    internal static string? LocaleCountryName(LocaleInfo info)
    {
        if (info.Country is null || info.Endonym is not { } endonym)
            return null;
        var open = endonym.IndexOf('(');
        var close = endonym.LastIndexOf(')');
        if (open < 0 || close <= open)
            return null;
        var name = endonym[(open + 1)..close].Split(',')[0].Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>Returns a postal code in one of the locale's formats, e.g. <c>91745</c>.</summary>
    /// <param name="format">Custom format (<c>#</c> digit, <c>?</c> letter, <c>*</c> either); defaults to the locale's formats.</param>
    public string ZipCode(string? format = null) =>
        Random.ReplaceSymbols(format ?? Faker.Pick(DataKeys.LocationPostcode));

    /// <summary>Returns a postal code valid for <paramref name="state"/> (supported by locales such as <c>en_US</c> and <c>en_CA</c>).</summary>
    /// <exception cref="PhoneyDataException">The locale has no postal codes for the state.</exception>
    public string ZipCodeByState(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return Faker.TryPick("location.postcode_by_state." + state)
            ?? throw new PhoneyDataException($"No zip code definition found for state '{state}' in locale '{Faker.Locale}'.");
    }

    /// <summary>Returns a city name, possibly composed from prefixes, names and suffixes.</summary>
    public string City() => Faker.Pick(DataKeys.LocationCityPattern);

    /// <summary>Returns a building number, e.g. <c>8432</c>.</summary>
    public string BuildingNumber() => Faker.Helpers.ReplaceHashRunsWithoutLeadingZero(Faker.Pick(DataKeys.LocationBuildingNumber));

    /// <summary>Returns a street name, e.g. <c>Kulas Shoals</c>.</summary>
    public string Street() => Faker.Pick(DataKeys.LocationStreetPattern);

    /// <summary>Returns a street address, e.g. <c>0917 O'Conner Estates</c>; with <paramref name="useFullAddress"/> including a secondary address.</summary>
    public string StreetAddress(bool useFullAddress = false) =>
        Faker.Pick(useFullAddress ? DataKeys.LocationStreetAddressFull : DataKeys.LocationStreetAddressNormal);

    /// <summary>Returns a complete postal address in the locale's format.</summary>
    public string PostalAddress() => Faker.Pick(DataKeys.LocationPostalAddress);

    /// <summary>Returns a secondary address such as <c>Apt. 861</c>.</summary>
    public string SecondaryAddress() => Faker.Helpers.ReplaceHashRunsWithoutLeadingZero(Faker.Pick(DataKeys.LocationSecondaryAddress));

    /// <summary>Returns a county, e.g. <c>Cambridgeshire</c>.</summary>
    public string County() => Faker.Pick(DataKeys.LocationCounty);

    /// <summary>Returns a country name in the locale's language.</summary>
    public string Country() => Faker.Pick(DataKeys.LocationCountry);

    /// <summary>Returns a continent.</summary>
    public string Continent() => Faker.Pick(DataKeys.LocationContinent);

    /// <summary>Returns an ISO 3166 country code.</summary>
    public string CountryCode(CountryCodeFormat format = CountryCodeFormat.Alpha2)
    {
        var (table, row) = Faker.PickRecord(DataKeys.LocationCountryCode);
        return table.Get(row, format switch
        {
            CountryCodeFormat.Alpha3 => "alpha3",
            CountryCodeFormat.Numeric => "numeric",
            _ => "alpha2",
        }) ?? "";
    }

    /// <summary>Returns a state, province or region; abbreviated (e.g. <c>CA</c>) when <paramref name="abbreviated"/>.</summary>
    public string State(bool abbreviated = false) => Faker.Pick(abbreviated ? DataKeys.LocationStateAbbr : DataKeys.LocationState);

    /// <summary>Returns a cardinal or ordinal direction, e.g. <c>Northeast</c> (or <c>NE</c>).</summary>
    public string Direction(bool abbreviated = false) => Random.Bool()
        ? CardinalDirection(abbreviated)
        : OrdinalDirection(abbreviated);

    /// <summary>Returns a cardinal direction, e.g. <c>North</c> (or <c>N</c>).</summary>
    public string CardinalDirection(bool abbreviated = false) =>
        Faker.Pick(abbreviated ? DataKeys.LocationDirectionCardinalAbbr : DataKeys.LocationDirectionCardinal);

    /// <summary>Returns an ordinal direction, e.g. <c>Southwest</c> (or <c>SW</c>).</summary>
    public string OrdinalDirection(bool abbreviated = false) =>
        Faker.Pick(abbreviated ? DataKeys.LocationDirectionOrdinalAbbr : DataKeys.LocationDirectionOrdinal);

    /// <summary>Returns an IANA time zone, e.g. <c>Europe/Stockholm</c>.</summary>
    public string TimeZone() => Faker.Pick(DataKeys.LocationTimeZone);

    /// <summary>Returns a language with its ISO 639 codes.</summary>
    public Language Language()
    {
        var (table, row) = Faker.PickRecord(DataKeys.LocationLanguage);
        return new Language(table.Get(row, "name") ?? "", table.Get(row, "alpha2") ?? "", table.Get(row, "alpha3") ?? "");
    }

    /// <summary>Returns a latitude in degrees between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is outside 0–15, or a limit is not a finite number.</exception>
    public double Latitude(double min = -90, double max = 90, int precision = 4) => Degrees(min, max, precision);

    /// <summary>Returns a longitude in degrees between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="precision"/> is outside 0–15, or a limit is not a finite number.</exception>
    public double Longitude(double min = -180, double max = 180, int precision = 4) => Degrees(min, max, precision);

    /// <summary>Degrees rounded to <paramref name="precision"/> decimals, with the precision checked under its own name.</summary>
    private double Degrees(double min, double max, int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, 15);
        return Faker.Number.Double(min, max, precision);
    }

    /// <summary>Returns a random coordinate within <paramref name="radius"/> of <paramref name="origin"/> (anywhere when no origin is given).</summary>
    /// <param name="origin">Center point: a latitude from -90 to 90 and a longitude from -180 to 180.</param>
    /// <param name="radius">Maximum distance; zero gives the origin.</param>
    /// <param name="isMetric">Whether <paramref name="radius"/> is in kilometers (otherwise miles).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or not finite, or <paramref name="origin"/> is not on the globe.</exception>
    public (double Latitude, double Longitude) NearbyGpsCoordinate((double Latitude, double Longitude)? origin = null, double radius = 10, bool isMetric = false)
    {
        if (!double.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius must be a finite distance that is not negative.");
        if (origin is { } given && !(Math.Abs(given.Latitude) <= 90 && Math.Abs(given.Longitude) <= 180)) // also false for NaN
            throw new ArgumentOutOfRangeException(nameof(origin), given, "The origin needs a latitude from -90 to 90 and a longitude from -180 to 180.");
        if (origin is not { } center)
            return (Latitude(), Longitude());

        // Ported from faker.js: move a random distance in a random direction on a spherical approximation. The sine and cosine are PureMath's, so a seed gives the same
        // coordinate on every platform (Math.Sin/Cos use the machine's C library).
        var angle = Faker.Number.Double(0, 2 * Math.PI, 5);
        var radiusKm = isMetric ? radius : radius * 1.60934;
        var distanceKm = Faker.Number.Double(0, radiusKm, 3) * 0.995; // 0.995 avoids float rounding past the radius
        if (distanceKm <= 0)
            return center; // as given: the normalization below would turn a longitude of 180 into -180
        var distanceDegrees = distanceKm / (40_000.0 / 360);
        var turns = angle / (2 * Math.PI);
        var latitude = (center.Latitude + (PureMath.SinTurns(turns) * distanceDegrees)) % 180;
        var longitude = center.Longitude + (PureMath.CosTurns(turns) * distanceDegrees);
        if (Math.Abs(latitude) > 90)
        {
            latitude = (Math.Sign(latitude) * 180) - latitude;
            longitude += 180;
        }

        longitude = (((longitude % 360) + 540) % 360) - 180;
        return (latitude, longitude);
    }
}

/// <summary>An address as separate parts.</summary>
/// <param name="Street">Street and number, e.g. <c>0917 O'Conner Estates</c>.</param>
/// <param name="City">City.</param>
/// <param name="ZipCode">Postal code, or <see langword="null"/> where the locale has none.</param>
/// <param name="State">State, province or region, or <see langword="null"/> where the locale has none.</param>
/// <param name="Country">The locale's country in its own language (e.g. <c>Österreich</c>), or <see langword="null"/> for language-wide locales such as <c>sv</c>.</param>
/// <param name="CountryCode">ISO 3166 alpha-2 code of the locale's country, or <see langword="null"/> for language-wide locales.</param>
public sealed record AddressProfile(string Street, string City, string? ZipCode, string? State, string? Country, string? CountryCode)
{
    /// <summary>Formats the address on one line, e.g. <c>Hauptstraße 5, 1010 Wien, Österreich</c>.</summary>
    public override string ToString() =>
        string.Join(", ", new[] { Street, string.Join(' ', new[] { ZipCode, City }.Where(p => p is not null)), State, Country }.Where(p => !string.IsNullOrEmpty(p)));
}

/// <summary>ISO 3166 country code formats.</summary>
public enum CountryCodeFormat
{
    /// <summary>Two letters, e.g. <c>SE</c>.</summary>
    Alpha2,

    /// <summary>Three letters, e.g. <c>SWE</c>.</summary>
    Alpha3,

    /// <summary>Three digits, e.g. <c>752</c>.</summary>
    Numeric,
}

/// <summary>A language with its ISO 639 codes.</summary>
/// <param name="Name">English name, e.g. <c>Swedish</c>.</param>
/// <param name="Alpha2">ISO 639-1 code, e.g. <c>sv</c>.</param>
/// <param name="Alpha3">ISO 639-2 code, e.g. <c>swe</c>.</param>
public readonly record struct Language(string Name, string Alpha2, string Alpha3);
