using System.Globalization;
using System.Numerics;

namespace Phonery.Modules;

/// <summary>Random numbers (faker.js <c>number</c>). All ranges are inclusive unless noted.</summary>
public sealed class NumberModule : FakerModule
{
    /// <summary>Roman numeral symbols, largest first, including subtractive pairs.</summary>
    private static readonly (string Symbol, int Value)[] RomanNumerals =
    [
        ("M", 1000), ("CM", 900), ("D", 500), ("CD", 400), ("C", 100), ("XC", 90),
        ("L", 50), ("XL", 40), ("X", 10), ("IX", 9), ("V", 5), ("IV", 4), ("I", 1),
    ];

    internal NumberModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an integer between <paramref name="min"/> and <paramref name="max"/> that is a multiple of <paramref name="multipleOf"/>.</summary>
    public int Int(int min = 0, int max = int.MaxValue, int multipleOf = 1) => (int)Long(min, max, multipleOf);

    /// <summary>Returns a long between <paramref name="min"/> and <paramref name="max"/> that is a multiple of <paramref name="multipleOf"/>.</summary>
    public long Long(long min = 0, long max = long.MaxValue, long multipleOf = 1)
    {
        if (multipleOf <= 0)
            throw new ArgumentOutOfRangeException(nameof(multipleOf), multipleOf, "multipleOf must be greater than 0.");
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        if (multipleOf == 1)
            return Random.Long(min, max);

        // Pick among the multiples inside the range, as faker.js does.
        var effectiveMin = CeilingDiv(min, multipleOf);
        var effectiveMax = FloorDiv(max, multipleOf);
        if (effectiveMax < effectiveMin)
            throw new ArgumentException($"No multiple of {multipleOf} between {min} and {max}.", nameof(multipleOf));
        return Random.Long(effectiveMin, effectiveMax) * multipleOf;
    }

    /// <summary>
    /// Returns a double in <c>[min, max)</c>, optionally rounded to <paramref name="fractionDigits"/> decimals
    /// (then <paramref name="max"/> is inclusive).
    /// </summary>
    public double Double(double min = 0, double max = 1, int? fractionDigits = null)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        if (fractionDigits is not { } digits)
            return Random.Double(min, max);
        ArgumentOutOfRangeException.ThrowIfNegative(digits, nameof(fractionDigits));

        // Choose an integer number of steps of 10^-digits, so every representable value is equally likely.
        var factor = Math.Pow(10, digits);
        var steps = Long((long)Math.Ceiling(min * factor), (long)Math.Floor(max * factor));
        return Math.Round(steps / factor, digits);
    }

    /// <summary>Returns a decimal between <paramref name="min"/> and <paramref name="max"/> with <paramref name="decimals"/> decimal places.</summary>
    public decimal Decimal(decimal min = 0, decimal max = 1, int decimals = 2)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        var factor = Pow10(decimals);
        var low = decimal.Ceiling(min * factor);
        var high = decimal.Floor(max * factor);
        if (high < low)
            throw new ArgumentException($"No value with {decimals} decimals between {min} and {max}.", nameof(decimals));
        var range = high - low;
        // decimal has more precision than one double draw; combine two draws for wide ranges.
        var offset = range <= long.MaxValue
            ? Random.Long(0, (long)range)
            : decimal.Floor((decimal)Random.Double() * (range + 1));
        return decimal.Round((low + offset) / factor, decimals);
    }

    /// <summary>Returns a random <see cref="BigInteger"/> between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public BigInteger BigInteger(BigInteger min, BigInteger max)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        var range = max - min + 1;
        var bytes = new byte[range.GetByteCount(isUnsigned: true) + 8];
        Random.Bytes(bytes);
        var value = new BigInteger(bytes, isUnsigned: true);
        return min + (value % range);
    }

    /// <summary>Returns an integer between <paramref name="min"/> and <paramref name="max"/> formatted in binary (base 2).</summary>
    public string Binary(int min = 0, int max = 1) => Convert.ToString(Int(min, max), 2);

    /// <summary>Returns an integer between <paramref name="min"/> and <paramref name="max"/> formatted in octal (base 8).</summary>
    public string Octal(int min = 0, int max = 7) => Convert.ToString(Int(min, max), 8);

    /// <summary>Returns an integer between <paramref name="min"/> and <paramref name="max"/> formatted in lower-case hexadecimal.</summary>
    public string Hex(int min = 0, int max = 15) => Int(min, max).ToString("x", CultureInfo.InvariantCulture);

    /// <summary>Returns a Roman numeral between <paramref name="min"/> and <paramref name="max"/> (1–3999).</summary>
    public string RomanNumeral(int min = 1, int max = 3999)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(min, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(max, 3999);
        var number = Int(min, max);
        var sb = new System.Text.StringBuilder();
        foreach (var (symbol, value) in RomanNumerals)
        {
            while (number >= value)
            {
                sb.Append(symbol);
                number -= value;
            }
        }

        return sb.ToString();
    }

    /// <summary>Integer division rounding up (for positive divisors).</summary>
    private static long CeilingDiv(long a, long b) => (a / b) + (a % b > 0 ? 1 : 0);

    /// <summary>Integer division rounding down (for positive divisors).</summary>
    private static long FloorDiv(long a, long b) => (a / b) - (a % b < 0 ? 1 : 0);

    /// <summary>10 to the power of <paramref name="exponent"/>, exactly.</summary>
    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10;
        return result;
    }
}
