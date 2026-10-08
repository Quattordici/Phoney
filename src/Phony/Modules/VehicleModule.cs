using Phony.Data;

namespace Phony.Modules;

/// <summary>Vehicles, VINs and registration marks (faker.js <c>vehicle</c>).</summary>
public sealed class VehicleModule : FakerModule
{
    // Letters I, O and Q are never used in VINs (confusable with 1 and 0).
    private const string VinExclude = "oiqOIQ";

    /// <summary>ISO 3779 position weights used for the VIN check digit.</summary>
    private static readonly int[] VinWeights = [8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2];

    internal VehicleModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a manufacturer and model, e.g. <c>Ford Mustang</c>.</summary>
    public string Vehicle() => $"{Manufacturer()} {Model()}";

    /// <summary>Returns a manufacturer, e.g. <c>Volvo</c>.</summary>
    public string Manufacturer() => Faker.Pick(DataKeys.VehicleManufacturer);

    /// <summary>Returns a model, e.g. <c>Golf</c>.</summary>
    public string Model() => Faker.Pick(DataKeys.VehicleModel);

    /// <summary>Returns a vehicle type, e.g. <c>SUV</c>.</summary>
    public string Type() => Faker.Pick(DataKeys.VehicleType);

    /// <summary>Returns a fuel type, e.g. <c>Electric</c>.</summary>
    public string Fuel() => Faker.Pick(DataKeys.VehicleFuel);

    /// <summary>Returns a bicycle type, e.g. <c>Cyclocross Bicycle</c>.</summary>
    public string Bicycle() => Faker.Pick(DataKeys.VehicleBicycleType);

    /// <summary>Returns a color name, e.g. <c>red</c>.</summary>
    public string Color() => Faker.Color.Human();

    /// <summary>Returns a 17-character vehicle identification number with a valid check digit (position 9).</summary>
    public string Vin()
    {
        var s = Faker.String;
        var vin = s.AlphaNumeric(10, Casing.Upper, VinExclude)
            + s.Alpha(1, Casing.Upper, VinExclude)
            + s.AlphaNumeric(1, Casing.Upper, VinExclude)
            + s.Numeric(5);
        return string.Concat(vin.AsSpan(0, 8), VinCheckDigit(vin), vin.AsSpan(9));
    }

    /// <summary>Returns a UK-style vehicle registration mark, e.g. <c>MF59EEW</c>.</summary>
    public string Vrm() =>
        Faker.String.Alpha(2, Casing.Upper) + Faker.String.Numeric(2) + Faker.String.Alpha(3, Casing.Upper);

    /// <summary>Computes the ISO 3779 check digit (0-9 or X) of a 17-character VIN.</summary>
    internal static string VinCheckDigit(string vin)
    {
        var checksum = 0;
        for (var i = 0; i < vin.Length && i < VinWeights.Length; i++)
            checksum += Transliterate(vin[i]) * VinWeights[i];
        var remainder = checksum % 11;
        return remainder == 10 ? "X" : ((char)('0' + remainder)).ToString();

        // Letters map to numbers per ISO 3779 (I, O and Q never occur).
        static int Transliterate(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            'A' or 'J' => 1,
            'B' or 'K' or 'S' => 2,
            'C' or 'L' or 'T' => 3,
            'D' or 'M' or 'U' => 4,
            'E' or 'N' or 'V' => 5,
            'F' or 'W' => 6,
            'G' or 'P' or 'X' => 7,
            'H' or 'Y' => 8,
            'R' or 'Z' => 9,
            _ => 0,
        };
    }
}
