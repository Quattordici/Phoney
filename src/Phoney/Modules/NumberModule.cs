using System.Globalization;
using System.Numerics;

namespace Phoney.Modules;

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

    /// <summary>The number of draws <see cref="Normal(double, double, double?, double?)"/> makes to land inside limits before it settles for the nearest limit.</summary>
    private const int MaxNormalAttempts = 100;

    /// <summary>The size of a scaled bound from which steps of a rounded double no longer fit a <see cref="long"/> (a little below <see cref="long.MaxValue"/>, 2^63).</summary>
    private const double LongStepLimit = 9.0e18;

    /// <summary>Powers of ten, exactly, for rounding (<see cref="Math.Pow(double, double)"/> is the system's, which rounds differently on some platforms).</summary>
    private static readonly double[] PowersOfTen = [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];

    /// <summary>
    /// Returns a number from a normal (Gaussian) distribution: most values lie near <paramref name="mean"/>, about 68 % within one <paramref name="standardDeviation"/> of it and 95 % within two.
    /// Heights, test scores, response times and other measurements look like this. With <paramref name="min"/> and <paramref name="max"/> a value outside them is drawn again, so the shape inside
    /// the limits stays a bell (cut off); if the limits lie so far in a tail that 100 draws miss them, the nearest limit is returned.
    /// </summary>
    /// <param name="mean">The centre of the distribution.</param>
    /// <param name="standardDeviation">The spread; zero gives the mean every time.</param>
    /// <param name="min">The smallest value, or <see langword="null"/> for no lower limit.</param>
    /// <param name="max">The largest value, or <see langword="null"/> for no upper limit.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mean"/> or a limit is not a number, or <paramref name="standardDeviation"/> is negative or not finite.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public double Normal(double mean = 0, double standardDeviation = 1, double? min = null, double? max = null)
    {
        ValidateNormal(mean, standardDeviation, min, max, decimals: null);
        if (min is null && max is null)
            return Random.Normal(mean, standardDeviation);

        var low = min ?? double.NegativeInfinity;
        var high = max ?? double.PositiveInfinity;
        var value = 0.0;
        for (var attempt = 0; attempt < MaxNormalAttempts; attempt++)
        {
            value = Random.Normal(mean, standardDeviation);
            if (value >= low && value <= high)
                return value;
        }

        return Math.Clamp(value, low, high);
    }

    /// <summary>
    /// Like <see cref="Normal(double, double, double?, double?)"/> as a <typeparamref name="TNumber"/>: whole numbers are rounded to the nearest integer, other types to two decimals (or to
    /// <paramref name="decimals"/>), the result stays inside the limits and a value the type cannot hold is the type's largest or smallest.
    /// </summary>
    /// <typeparam name="TNumber">Any numeric type: <c>int</c>, <c>decimal</c>, <c>double</c>, <c>byte</c>, <c>BigInteger</c>…</typeparam>
    /// <param name="mean">The centre of the distribution.</param>
    /// <param name="standardDeviation">The spread; zero gives the mean every time.</param>
    /// <param name="min">The smallest value, or <see langword="null"/> for no lower limit.</param>
    /// <param name="max">The largest value, or <see langword="null"/> for no upper limit.</param>
    /// <param name="decimals">The number of decimals to round to (0 to 15), or <see langword="null"/> for the type's default.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mean"/> or a limit is not a number, <paramref name="standardDeviation"/> is negative or not finite, or <paramref name="decimals"/> is not 0 to 15.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public TNumber Normal<TNumber>(double mean, double standardDeviation, double? min = null, double? max = null, int? decimals = null)
        where TNumber : INumber<TNumber>
    {
        ValidateNormal(mean, standardDeviation, min, max, decimals);
        var digits = decimals ?? (IsFractional<TNumber>() ? 2 : 0);
        var factor = PowersOfTen[digits];
        var value = Math.Round(Normal(mean, standardDeviation, min, max), digits, MidpointRounding.AwayFromZero);

        // Rounding may take a value just past a limit: move it to the nearest number with those decimals that is inside.
        if (min is { } low && value < low)
            value = Math.Ceiling(low * factor) / factor;
        if (max is { } high && value > high)
            value = Math.Floor(high * factor) / factor;
        return TNumber.CreateSaturating(value);
    }

    /// <summary>Checks the arguments of a normal distribution; <c>ObjectGenerator&lt;T&gt;.WithNormalDistribution</c> calls it when the rule is added, so a mistake shows there and not at the first <c>Generate</c>.</summary>
    internal static void ValidateNormal(double mean, double standardDeviation, double? min, double? max, int? decimals)
    {
        if (!double.IsFinite(mean))
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "The mean must be a finite number.");
        if (!double.IsFinite(standardDeviation) || standardDeviation < 0)
            throw new ArgumentOutOfRangeException(nameof(standardDeviation), standardDeviation, "The standard deviation must be a finite number that is not negative.");
        if (min is { } low && double.IsNaN(low))
            throw new ArgumentOutOfRangeException(nameof(min), min, "The lower limit must be a number.");
        if (max is { } high && double.IsNaN(high))
            throw new ArgumentOutOfRangeException(nameof(max), max, "The upper limit must be a number.");
        if (min > max)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        if (decimals is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(decimals), decimals, "The number of decimals must be between 0 and 15.");
    }

    /// <summary>Whether a numeric type has a fractional part (the whole-number types are rounded to integers).</summary>
    private static bool IsFractional<TNumber>() => typeof(TNumber) == typeof(double) || typeof(TNumber) == typeof(float) || typeof(TNumber) == typeof(decimal) || typeof(TNumber) == typeof(Half);

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
    /// (then <paramref name="max"/> is inclusive). Any finite range works, up to <see cref="double.MinValue"/> to <see cref="double.MaxValue"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> or <paramref name="max"/> is not a finite number, or <paramref name="fractionDigits"/> is outside 0–15.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public double Double(double min = 0, double max = 1, int? fractionDigits = null)
    {
        if (!double.IsFinite(min))
            throw new ArgumentOutOfRangeException(nameof(min), min, "The minimum must be a finite number.");
        if (!double.IsFinite(max))
            throw new ArgumentOutOfRangeException(nameof(max), max, "The maximum must be a finite number.");
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        if (fractionDigits is not { } digits)
            return Random.Double(min, max);
        ArgumentOutOfRangeException.ThrowIfNegative(digits, nameof(fractionDigits));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 15, nameof(fractionDigits));

        // Choose an integer number of steps of 10^-digits, so every representable value is equally likely.
        var factor = PowersOfTen[digits];
        if (Math.Abs(min * factor) >= LongStepLimit || Math.Abs(max * factor) >= LongStepLimit)
        {
            // Too many steps to count in a long: at this size a double has no fractional digits left to round, so draw it directly.
            return Math.Clamp(Math.Round(Random.Double(min, max), digits), min, max);
        }

        var steps = Long((long)Math.Ceiling(min * factor), (long)Math.Floor(max * factor));
        return Math.Round(steps / factor, digits);
    }

    /// <summary>
    /// Returns a decimal between <paramref name="min"/> and <paramref name="max"/> with <paramref name="decimals"/> decimal places (fewer for values
    /// so large that a <see cref="decimal"/> can't hold that many places). Any range works, up to <see cref="decimal.MinValue"/> to <see cref="decimal.MaxValue"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>, or no value with <paramref name="decimals"/> places lies between them.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="decimals"/> is negative or more than 28.</exception>
    public decimal Decimal(decimal min = 0, decimal max = 1, int decimals = 2)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, MaxDecimalPlaces);

        var largest = Math.Max(Math.Abs(min), Math.Abs(max));
        if (decimals <= MaxLongPlaces && largest <= DecimalPowers[MaxLongPlaces - decimals])
        {
            // The common case: every step fits in a long, so the value is built from its digits instead of divided out (same draw, same result).
            var scale = DecimalPowers[decimals];
            var first = decimal.Ceiling(min * scale);
            var last = decimal.Floor(max * scale);
            if (last < first)
                throw new ArgumentException($"No value with {decimals} decimals between {min} and {max}.", nameof(decimals));
            var start = (long)first;
            var drawn = Random.Long(0, (long)last - start);
            var steps = start + drawn;
            // A zero keeps the sign decimal arithmetic gives it (-0 from a negative minimum), so it is divided out like before.
            return steps != 0 ? FromSteps(steps, decimals) : decimal.Round((first + drawn) / scale, decimals);
        }

        // A value too large to scale by 10^decimals can't hold that many places either: keep as many as fit.
        var places = decimals;
        while (places > 0 && largest > decimal.MaxValue / Pow10(places))
            places--;

        var factor = Pow10(places);
        var low = decimal.Ceiling(min * factor);
        var high = decimal.Floor(max * factor);
        if (high < low)
            throw new ArgumentException($"No value with {decimals} decimals between {min} and {max}.", nameof(decimals));
        if (low < 0 && high > decimal.MaxValue + low)
        {
            // The width doesn't fit in a decimal (e.g. MinValue to MaxValue): interpolate between the ends instead.
            var share = (decimal)Random.Double();
            var value = decimal.Floor((low * (1 - share)) + (high * share));
            return decimal.Round(Math.Clamp(value, low, high) / factor, places);
        }

        var range = high - low;
        // decimal has more precision than one double draw; combine two draws for wide ranges.
        var offset = range <= long.MaxValue
            ? Random.Long(0, (long)range)
            : decimal.Floor((decimal)Random.Double() * (range < decimal.MaxValue ? range + 1 : range));
        return decimal.Round((low + offset) / factor, places);
    }

    /// <summary>The most decimal places a <see cref="decimal"/> has.</summary>
    internal const int MaxDecimalPlaces = 28;

    /// <summary>Scaled values below 10^18 fit in a long.</summary>
    private const int MaxLongPlaces = 18;

    /// <summary>Exact powers of ten, 10^0 to 10^18.</summary>
    private static readonly decimal[] DecimalPowers =
    [
        1m, 10m, 100m, 1_000m, 10_000m, 100_000m, 1_000_000m, 10_000_000m, 100_000_000m, 1_000_000_000m, 10_000_000_000m, 100_000_000_000m,
        1_000_000_000_000m, 10_000_000_000_000m, 100_000_000_000_000m, 1_000_000_000_000_000m, 10_000_000_000_000_000m, 100_000_000_000_000_000m,
        1_000_000_000_000_000_000m,
    ];

    /// <summary>
    /// <paramref name="steps"/> (not zero) × 10^-<paramref name="places"/> with trailing zeros dropped, exactly what <c>decimal.Round(steps / 10^places, places)</c> returns
    /// (division keeps the smallest scale), without dividing.
    /// </summary>
    private static decimal FromSteps(long steps, int places)
    {
        var negative = steps < 0;
        var magnitude = negative ? 0UL - (ulong)steps : (ulong)steps;
        while (places > 0 && magnitude % 10 == 0)
        {
            magnitude /= 10;
            places--;
        }

        return new decimal((int)(uint)magnitude, (int)(uint)(magnitude >> 32), 0, negative, (byte)places);
    }

    /// <summary>Returns a byte between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public byte Byte(byte min = 0, byte max = byte.MaxValue) => (byte)Long(min, max);

    /// <summary>Returns an sbyte between <paramref name="min"/> and <paramref name="max"/> (non-negative by default).</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public sbyte SByte(sbyte min = 0, sbyte max = sbyte.MaxValue) => (sbyte)Long(min, max);

    /// <summary>Returns a short between <paramref name="min"/> and <paramref name="max"/> (non-negative by default).</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public short Short(short min = 0, short max = short.MaxValue) => (short)Long(min, max);

    /// <summary>Returns a ushort between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public ushort UShort(ushort min = 0, ushort max = ushort.MaxValue) => (ushort)Long(min, max);

    /// <summary>Returns a uint between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public uint UInt(uint min = 0, uint max = uint.MaxValue) => (uint)Long(min, max);

    /// <summary>Returns a ulong between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public ulong ULong(ulong min = 0, ulong max = ulong.MaxValue) => Random.ULong(min, max);

    /// <summary>Returns an <see cref="Int128"/> between <paramref name="min"/> (default 0) and <paramref name="max"/> (default <see cref="System.Int128.MaxValue"/>).</summary>
    /// <exception cref="ArgumentException">The effective maximum is less than the effective minimum.</exception>
    public Int128 Int128(Int128? min = null, Int128? max = null) =>
        (Int128)BigInteger((BigInteger)(min ?? 0), (BigInteger)(max ?? System.Int128.MaxValue));

    /// <summary>Returns a <see cref="UInt128"/> between <paramref name="min"/> (default 0) and <paramref name="max"/> (default <see cref="System.UInt128.MaxValue"/>).</summary>
    /// <exception cref="ArgumentException">The effective maximum is less than the effective minimum.</exception>
    public UInt128 UInt128(UInt128? min = null, UInt128? max = null) =>
        (UInt128)BigInteger((BigInteger)(min ?? 0), (BigInteger)(max ?? System.UInt128.MaxValue));

    /// <summary>Returns a float in <c>[min, max)</c>, optionally rounded to <paramref name="fractionDigits"/> decimals (then <paramref name="max"/> is inclusive).</summary>
    /// <remarks>Equal finite bounds return that value when no rounding is requested.</remarks>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>, or the range contains no value at the requested decimal precision.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fractionDigits"/> is outside 0–15.</exception>
    public float Single(float min = 0, float max = 1, int? fractionDigits = null) => Fractional(min, max, fractionDigits);

    /// <summary>Returns a <see cref="Half"/> in <c>[min, max)</c> (0 to 1 by default), optionally rounded to <paramref name="fractionDigits"/> decimals (then <paramref name="max"/> is inclusive).</summary>
    /// <remarks>Equal finite effective bounds return that value when no rounding is requested.</remarks>
    /// <exception cref="ArgumentException">The effective maximum is less than the effective minimum, or the range contains no value at the requested decimal precision.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fractionDigits"/> is outside 0–15.</exception>
    public Half Half(Half? min = null, Half? max = null, int? fractionDigits = null) =>
        Fractional(min ?? System.Half.Zero, max ?? System.Half.One, fractionDigits);

    /// <summary>A double narrowed to a smaller floating point type without leaving <c>[min, max)</c> (or <c>[min, max]</c> when rounded).</summary>
    private T Fractional<T>(T min, T max, int? fractionDigits) where T : IFloatingPointIeee754<T>
    {
        var value = T.CreateSaturating(Double(double.CreateSaturating(min), double.CreateSaturating(max), fractionDigits));
        if (fractionDigits is null && value >= max && max > min)
            value = T.BitDecrement(max);
        return T.Clamp(value, min, max);
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
    internal static decimal Pow10(int exponent)
    {
        if (exponent <= MaxLongPlaces)
            return DecimalPowers[exponent];
        var result = 1m;
        for (var i = 0; i < exponent; i++)
            result *= 10;
        return result;
    }
}
