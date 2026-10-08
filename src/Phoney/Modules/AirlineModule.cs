using System.Globalization;
using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Airlines, airports, flights and seats (faker.js <c>airline</c>).</summary>
public sealed class AirlineModule : FakerModule
{
    private const string Digits = "0123456789";
    private const string VisuallySimilar = "0O1IL";

    internal AirlineModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an airport, e.g. <c>(Stockholm Arlanda Airport, ARN)</c>.</summary>
    public Airport Airport()
    {
        var (table, row) = Faker.PickRecord(DataKeys.AirlineAirport);
        return new Airport(table.Get(row, "name") ?? "", table.Get(row, "iataCode") ?? "");
    }

    /// <summary>Returns an airline, e.g. <c>(Scandinavian Airlines, SK)</c>.</summary>
    public Airline Airline()
    {
        var (table, row) = Faker.PickRecord(DataKeys.AirlineAirline);
        return new Airline(table.Get(row, "name") ?? "", table.Get(row, "iataCode") ?? "");
    }

    /// <summary>Returns an airplane model, e.g. <c>(Airbus A320, 320)</c>.</summary>
    public Airplane Airplane()
    {
        var (table, row) = Faker.PickRecord(DataKeys.AirlineAirplane);
        return new Airplane(table.Get(row, "name") ?? "", table.Get(row, "iataTypeCode") ?? "");
    }

    /// <summary>Returns a 6-character booking reference, e.g. <c>KXTYWZ</c>.</summary>
    /// <param name="allowNumerics">Whether digits may appear.</param>
    /// <param name="allowVisuallySimilarCharacters">Whether confusable characters (0, O, 1, I, L) may appear.</param>
    public string RecordLocator(bool allowNumerics = false, bool allowVisuallySimilarCharacters = false)
    {
        var exclude = (allowNumerics ? "" : Digits) + (allowVisuallySimilarCharacters ? "" : VisuallySimilar);
        return Faker.String.AlphaNumeric(6, Casing.Upper, exclude);
    }

    /// <summary>Returns a seat, e.g. <c>23F</c>; rows and letters depend on <paramref name="aircraftType"/>.</summary>
    public string Seat(Modules.AircraftType aircraftType = Modules.AircraftType.Narrowbody)
    {
        var (maxRow, letters) = aircraftType switch
        {
            Modules.AircraftType.Regional => (20, "ABCD"),
            Modules.AircraftType.Widebody => (60, "ABCDEFGHJK"),
            _ => (35, "ABCDEF"),
        };
        return Random.Int(1, maxRow).ToString(CultureInfo.InvariantCulture) + letters[Random.Index(letters.Length)];
    }

    /// <summary>Returns a random aircraft size category.</summary>
    public Modules.AircraftType AircraftType() => Random.Enum<Modules.AircraftType>();

    /// <summary>Returns a flight number of 1–4 digits (no leading zero), optionally padded to 4 digits.</summary>
    public string FlightNumber(int? length = null, bool addLeadingZeros = false)
    {
        var number = Faker.String.Numeric(length ?? Random.Int(1, 4), allowLeadingZeros: false);
        return addLeadingZeros ? number.PadLeft(4, '0') : number;
    }
}

/// <summary>Aircraft size categories, which determine seat layouts.</summary>
public enum AircraftType
{
    /// <summary>Single-aisle airliner (up to 35 rows, seats A–F).</summary>
    Narrowbody,

    /// <summary>Regional jet or turboprop (up to 20 rows, seats A–D).</summary>
    Regional,

    /// <summary>Twin-aisle airliner (up to 60 rows, seats A–K).</summary>
    Widebody,
}

/// <summary>An airline.</summary>
/// <param name="Name">Airline name.</param>
/// <param name="IataCode">Two-character IATA airline code.</param>
public readonly record struct Airline(string Name, string IataCode);

/// <summary>An airport.</summary>
/// <param name="Name">Airport name.</param>
/// <param name="IataCode">Three-letter IATA airport code.</param>
public readonly record struct Airport(string Name, string IataCode);

/// <summary>An airplane model.</summary>
/// <param name="Name">Model name.</param>
/// <param name="IataTypeCode">IATA aircraft type code.</param>
public readonly record struct Airplane(string Name, string IataTypeCode);
