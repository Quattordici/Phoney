namespace Phoney;

/// <summary>
/// The logarithm, cosine and sine that normally distributed numbers and nearby coordinates need, written with <c>+ - * /</c>, comparisons and bit operations only. Those are exactly rounded by IEEE 754 on every
/// platform, whereas <see cref="Math.Log(double)"/> and <see cref="Math.Cos(double)"/> call the C library of the machine, which may round the last bit differently on Linux, Windows and ARM.
/// Seeded data must be the same everywhere, so the numbers are made here. Both are accurate to a few units of the last digit, far beyond what fake data needs.
/// </summary>
internal static class PureMath
{
    /// <summary>The double nearest to the natural logarithm of 2.</summary>
    private const double Ln2 = 0.6931471805599453;

    /// <summary>The double nearest to π/2.</summary>
    private const double HalfPi = 1.5707963267948966;

    /// <summary>2<sup>54</sup>, to bring a subnormal number into the normal range.</summary>
    private const double TwoPow54 = 18014398509481984.0;

    /// <summary>The series have 12 terms: that is below 1e-17 for the angles and mantissas used here.</summary>
    private const int Terms = 12;

    // Coefficients of the series, made with the same exactly rounded operations as the rest, once: 1/(2k+1) for atanh, (-1)^k/(2k)! for cos and (-1)^k/(2k+1)! for sin.
    private static readonly double[] AtanhCoefficients = BuildAtanh();
    private static readonly double[] CosCoefficients = BuildTaylor(first: 0);
    private static readonly double[] SinCoefficients = BuildTaylor(first: 1);

    private static double[] BuildAtanh()
    {
        var coefficients = new double[Terms];
        for (var k = 0; k < Terms; k++)
            coefficients[k] = 1.0 / ((2 * k) + 1);
        return coefficients;
    }

    private static double[] BuildTaylor(int first)
    {
        var coefficients = new double[Terms];
        var factorial = 1.0; // (2k + first)!
        for (var k = 0; k < Terms; k++)
        {
            if (k > 0 || first > 0)
            {
                var upper = (2 * k) + first;
                factorial *= (upper - 1 > 0 ? upper - 1 : 1) * upper;
            }

            coefficients[k] = (k % 2 == 0 ? 1.0 : -1.0) / factorial;
        }

        return coefficients;
    }

    /// <summary>The natural logarithm of <paramref name="x"/>: its binary exponent plus an atanh series of the mantissa, which lies in [√½, √2].</summary>
    public static double Ln(double x)
    {
        if (double.IsNaN(x) || x < 0)
            return double.NaN;
        if (x == 0)
            return double.NegativeInfinity;
        if (double.IsPositiveInfinity(x))
            return x;

        var bits = BitConverter.DoubleToInt64Bits(x);
        var exponent = (int)((bits >> 52) & 0x7FF);
        if (exponent == 0)
            return Ln(x * TwoPow54) - (54 * Ln2); // subnormal

        exponent -= 1023;
        var mantissa = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L); // [1, 2)
        if (mantissa > 1.4142135623730951)
        {
            mantissa *= 0.5;
            exponent++;
        }

        // ln m = 2·atanh((m-1)/(m+1)) = 2·f·(1 + f²/3 + f⁴/5 + …) with |f| ≤ 0.1716, summed with Horner's scheme.
        var f = (mantissa - 1) / (mantissa + 1);
        var f2 = f * f;
        var series = AtanhCoefficients;
        var sum = series[Terms - 1];
        for (var k = Terms - 2; k >= 0; k--)
            sum = (sum * f2) + series[k];

        return (2 * f * sum) + (exponent * Ln2);
    }

    /// <summary>cos(2π·<paramref name="turns"/>) for <paramref name="turns"/> in [0, 1]: the quarter turn is found exactly, and a Taylor series is summed for an angle of at most π/4.</summary>
    public static double CosTurns(double turns) => CosSinTurns(turns).Cos;

    /// <summary>sin(2π·<paramref name="turns"/>) for <paramref name="turns"/> in [0, 1], like <see cref="CosTurns"/>.</summary>
    public static double SinTurns(double turns) => CosSinTurns(turns).Sin;

    /// <summary>The cosine and sine of 2π·<paramref name="turns"/>, from the same two series.</summary>
    private static (double Cos, double Sin) CosSinTurns(double turns)
    {
        var quarters = turns * 4; // exact: a power of two
        var quadrant = (int)quarters;
        var within = quarters - quadrant; // [0, 1), exact
        var nearEnd = within > 0.5;
        var angle = (nearEnd ? 1 - within : within) * HalfPi; // [0, π/4]
        var angle2 = angle * angle;

        var cosSeries = CosCoefficients;
        var sinSeries = SinCoefficients;
        var cos = cosSeries[Terms - 1];
        var sin = sinSeries[Terms - 1];
        for (var n = Terms - 2; n >= 0; n--)
        {
            cos = (cos * angle2) + cosSeries[n];
            sin = (sin * angle2) + sinSeries[n];
        }

        sin *= angle;

        // Within the quadrant the angle is φ = within·π/2: its cosine is cos(angle), or sin(angle) when the nearer end was the quadrant's far side.
        var cosPhi = nearEnd ? sin : cos;
        var sinPhi = nearEnd ? cos : sin;
        return (quadrant & 3) switch
        {
            0 => (cosPhi, sinPhi),
            1 => (-sinPhi, cosPhi),
            2 => (-cosPhi, -sinPhi),
            _ => (sinPhi, -cosPhi),
        };
    }
}
