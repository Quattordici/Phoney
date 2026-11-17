using System.Numerics;

namespace Phoney;

/// <summary>
/// Conversions of <see cref="double"/> and <see cref="float"/> to <see cref="decimal"/> that give the same result on every target framework. .NET 11 made the casts
/// round exactly (a double becomes its exact binary value instead of 15 significant digits, with no switch back), which would change every seeded
/// decimal made from a double draw. This is the conversion of .NET 10 and earlier (<c>VarDecFromR8</c>/<c>VarDecFromR4</c> of dotnet/runtime,
/// MIT), in <c>+ - * /</c> on doubles and integer arithmetic only, so it is identical on every OS and runtime; tests compare it bit for bit with the
/// cast on .NET 10. Use it, never a cast or <c>CreateSaturating</c>, wherever a generated or configured number becomes a decimal (decimal to double changed too; nothing generated takes that way).
/// </summary>
internal static class PortableDecimal
{
    private const int MaxScale = 28;

    // The largest double CreateSaturating doesn't clamp to the decimal limits.
    private const double DecimalLimit = 79228162514264337593543950335.0;

    private static readonly double[] DoublePowers10 =
    [
        1, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22, 1e23, 1e24, 1e25,
        1e26, 1e27, 1e28, 1e29, 1e30, 1e31, 1e32, 1e33, 1e34, 1e35, 1e36, 1e37, 1e38, 1e39, 1e40, 1e41, 1e42, 1e43,
    ];

    /// <summary><paramref name="value"/> rounded to 15 significant digits, as <c>(decimal)value</c> before .NET 11.</summary>
    /// <exception cref="OverflowException"><paramref name="value"/> is outside the decimal range or not a number.</exception>
    public static decimal FromDouble(double value) =>
        Round(value, (int)((BitConverter.DoubleToUInt64Bits(value) >> 52) & 0x7FF) - 1022, digits: 15);

    /// <summary><paramref name="value"/> rounded to 7 significant digits, as <c>(decimal)value</c> before .NET 11.</summary>
    /// <exception cref="OverflowException"><paramref name="value"/> is outside the decimal range or not a number.</exception>
    public static decimal FromSingle(float value) =>
        Round(value, (int)((BitConverter.SingleToUInt32Bits(value) >> 23) & 0xFF) - 126, digits: 7);

    /// <summary>
    /// <paramref name="value"/> (a double, or a float widened exactly) rounded half to even to <paramref name="digits"/> significant digits, its binary
    /// <paramref name="exponent"/> giving the estimate of its power of ten; the shared part of <c>VarDecFromR8</c> and <c>VarDecFromR4</c>.
    /// </summary>
    private static decimal Round(double value, int exponent, int digits)
    {
        // The most a decimal scales by is 10^28, slightly more than 2^93: an exponent below -94 always rounds to zero.
        if (exponent < -94)
            return 0m;
        if (exponent > 96)
            throw new OverflowException("Value was either too large or too small for a Decimal.");

        var negative = value < 0;
        var (scaled, power) = ScaleToDigits(Math.Abs(value), exponent, digits);

        // Round half to even to an integer (exact in IEEE arithmetic, as the runtime's ROUNDSD path does).
        var mantissa = (ulong)Math.Round(scaled, MidpointRounding.ToEven);
        if (mantissa == 0)
            return 0m;
        if (power < 0)
            return Whole(mantissa, -power, negative);

        // Remove trailing zeros the way the runtime does (it tests the low bits first, so some zeros stay).
        var removable = Math.Min(power, digits - 1);
        RemoveZeros(ref mantissa, ref power, ref removable, (byte)mantissa == 0, 8);
        RemoveZeros(ref mantissa, ref power, ref removable, (mantissa & 0xF) == 0, 4);
        RemoveZeros(ref mantissa, ref power, ref removable, (mantissa & 3) == 0, 2);
        RemoveZeros(ref mantissa, ref power, ref removable, (mantissa & 1) == 0, 1);
        return new decimal((int)(uint)mantissa, (int)(uint)(mantissa >> 32), 0, negative, (byte)power);
    }

    /// <summary>
    /// <paramref name="magnitude"/> times a power of ten (the power returned, at most the decimal scale) so that it has <paramref name="digits"/> digits
    /// before the point; log10(2) * 2^16 = 19728.3 estimates the power from the binary <paramref name="exponent"/>.
    /// </summary>
    private static (double Scaled, int Power) ScaleToDigits(double magnitude, int exponent, int digits)
    {
        var limit = DoublePowers10[digits];
        var power = digits - 1 - ((exponent * 19728) >> 16);
        if (power >= 0)
        {
            power = Math.Min(power, MaxScale);
            magnitude *= DoublePowers10[power];
        }
        else if (power != -1 || magnitude >= limit)
        {
            magnitude /= DoublePowers10[-power];
        }
        else
        {
            power = 0;
        }

        return magnitude < limit / 10 && power < MaxScale ? (magnitude * 10, power + 1) : (magnitude, power);
    }

    /// <summary>The whole number <paramref name="mantissa"/> × 10^<paramref name="zeros"/>, which must fit in 96 bits.</summary>
    private static decimal Whole(ulong mantissa, int zeros, bool negative)
    {
        var whole = (UInt128)mantissa;
        for (var i = 0; i < zeros; i++)
            whole *= 10;
        if (whole >> 96 != 0)
            throw new OverflowException("Value was either too large or too small for a Decimal.");
        return new decimal((int)(uint)whole, (int)(uint)(whole >> 32), (int)(uint)(whole >> 64), negative, 0);
    }

    /// <summary>
    /// <c>TNumber.CreateSaturating(value)</c>, with a decimal made by <see cref="FromDouble"/>: the decimal limits beyond them, as
    /// <c>CreateSaturating</c> gives (a value that isn't a number throws, as it did before .NET 11).
    /// </summary>
    public static TNumber Create<TNumber>(double value) where TNumber : INumberBase<TNumber>
    {
        if (typeof(TNumber) != typeof(decimal))
#pragma warning disable PHONEYDEV002 // every type but decimal, which is handled below
            return TNumber.CreateSaturating(value);
#pragma warning restore PHONEYDEV002
        if (value >= DecimalLimit)
            return TNumber.CreateSaturating(decimal.MaxValue);
        if (value <= -DecimalLimit)
            return TNumber.CreateSaturating(decimal.MinValue);
        return TNumber.CreateSaturating(FromDouble(value));
    }

    /// <summary>A number of any type as a decimal: a double or float as <see cref="FromDouble"/>/<see cref="FromSingle"/>, the rest as <see cref="Convert.ToDecimal(object, IFormatProvider)"/>.</summary>
    public static decimal FromNumber(object? value) => value switch
    {
        double d => FromDouble(d),
        float f => FromSingle(f),
        _ => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static void RemoveZeros(ref ulong mantissa, ref int power, ref int removable, bool candidate, int zeros)
    {
        if (!candidate || removable < zeros)
            return;
        var divisor = zeros switch { 8 => 100_000_000UL, 4 => 10_000UL, 2 => 100UL, _ => 10UL };
        var quotient = mantissa / divisor;
        if (mantissa != quotient * divisor)
            return;
        mantissa = quotient;
        power -= zeros;
        removable -= zeros;
    }
}
