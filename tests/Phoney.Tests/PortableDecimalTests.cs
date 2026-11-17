namespace Phoney.Tests;

/// <summary>
/// .NET 11 rounds double → decimal conversions exactly instead of to 15 significant digits (a breaking change with no switch back), so a cast would
/// change seeded decimals between target frameworks. <see cref="PortableDecimal"/> is the conversion of .NET 10 and earlier, on every runtime.
/// </summary>
public sealed class PortableDecimalTests
{
    private static readonly bool CastIsTheOldConversion = Environment.Version.Major < 11;

    [Test]
    [Arguments(1.0217911323039463, "1.02179113230395")]
    [Arguments(0.1, "0.1")]
    [Arguments(1.23, "1.23")]
    [Arguments(-2.5, "-2.5")]
    [Arguments(123456789.123456789, "123456789.123457")]
    [Arguments(1e-28, "0.0000000000000000000000000001")]
    [Arguments(1e-29, "0")]
    [Arguments(7.9e28, "79000000000000000000000000000")]
    [Arguments(1e15, "1000000000000000")]
    [Arguments(0.999999999999999944, "1.0")]
    [Arguments(-0.0, "0")]
    public void Keeps_fifteen_significant_digits_like_net10(double value, string expected)
    {
        PortableDecimal.FromDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Test]
    public void Matches_the_runtime_cast_on_net10_bit_for_bit()
    {
        if (!CastIsTheOldConversion)
            return; // the pinned values above carry what this checked on .NET 10

        var random = new System.Random(20261117);
        Span<byte> bytes = stackalloc byte[8];
        for (var i = 0; i < 1_000_000; i++)
        {
            random.NextBytes(bytes);
            // Exponents of 2^-100 to 2^96: everything a decimal can hold, and the ranges that round to zero.
            var mantissa = BitConverter.ToUInt64(bytes) & 0x800F_FFFF_FFFF_FFFFUL;
            var exponent = (ulong)random.Next(1023 - 100, 1023 + 96) << 52;
            var value = BitConverter.UInt64BitsToDouble(mantissa | exponent);

            decimal.GetBits(PortableDecimal.FromDouble(value)).ShouldBe(decimal.GetBits((decimal)value), $"{value:R}");
        }
    }

    [Test]
    [Arguments(0.1f, "0.1")]
    [Arguments(1.0217911f, "1.021791")]
    [Arguments(-3.4e28f, "-34000000000000000000000000000")]
    [Arguments(1e-29f, "0")]
    public void Keeps_seven_significant_digits_of_a_float_like_net10(float value, string expected)
    {
        PortableDecimal.FromSingle(value).ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Test]
    public void Matches_the_runtime_cast_of_floats_on_net10_bit_for_bit()
    {
        if (!CastIsTheOldConversion)
            return;

        var random = new System.Random(7);
        for (var i = 0; i < 1_000_000; i++)
        {
            var mantissa = (uint)random.NextInt64(0, 1L << 32) & 0x807F_FFFFu;
            var exponent = (uint)random.Next(127 - 100, 127 + 96) << 23;
            var value = BitConverter.UInt32BitsToSingle(mantissa | exponent);

            decimal.GetBits(PortableDecimal.FromSingle(value)).ShouldBe(decimal.GetBits((decimal)value), $"{value:R}");
        }
    }

    [Test]
    public void Out_of_range_values_throw_like_the_cast_and_saturate_like_CreateSaturating()
    {
        Should.Throw<OverflowException>(() => PortableDecimal.FromDouble(1e30));
        Should.Throw<OverflowException>(() => PortableDecimal.FromDouble(double.NaN));
        PortableDecimal.Create<decimal>(1e30).ShouldBe(decimal.MaxValue);
        PortableDecimal.Create<decimal>(double.NegativeInfinity).ShouldBe(decimal.MinValue);
        PortableDecimal.Create<decimal>(1.0217911323039463).ShouldBe(1.02179113230395m);
        PortableDecimal.Create<double>(1.0217911323039463).ShouldBe(1.0217911323039463);
        PortableDecimal.Create<int>(2.7).ShouldBe(2);
    }
}
