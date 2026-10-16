using System.Numerics;

namespace Phoney.Tests;

/// <summary>The direct calls of <c>faker.Number</c> for every numeric type a member can have, and <c>SeededRandom.ULong</c>.</summary>
public sealed class NumberTypeTests
{
    private readonly Faker _faker = new("en", seed: 17);

    [Test]
    public void Whole_number_types_stay_inside_their_bounds_and_reach_both_ends()
    {
        var bytes = Enumerable.Range(0, 3000).Select(_ => _faker.Number.Byte(250, 255)).ToList();
        bytes.Min().ShouldBe((byte)250);
        bytes.Max().ShouldBe((byte)255);

        var sbytes = Enumerable.Range(0, 3000).Select(_ => _faker.Number.SByte(-3, 3)).ToList();
        sbytes.Min().ShouldBe((sbyte)-3);
        sbytes.Max().ShouldBe((sbyte)3);

        for (var i = 0; i < 500; i++)
        {
            _faker.Number.Short(-5, 5).ShouldBeInRange((short)-5, (short)5);
            _faker.Number.UShort(65530, ushort.MaxValue).ShouldBeInRange((ushort)65530, ushort.MaxValue);
            _faker.Number.UInt(uint.MaxValue - 5, uint.MaxValue).ShouldBeInRange(uint.MaxValue - 5, uint.MaxValue);
        }
    }

    [Test]
    public void Defaults_cover_zero_up_to_the_largest_value_of_the_type()
    {
        Enumerable.Range(0, 500).Select(_ => _faker.Number.Byte()).Max().ShouldBeGreaterThan((byte)200);
        Enumerable.Range(0, 500).Select(_ => _faker.Number.UShort()).Max().ShouldBeGreaterThan((ushort)60000);
        Enumerable.Range(0, 500).Select(_ => _faker.Number.UInt()).Max().ShouldBeGreaterThan(4_000_000_000u);
        Enumerable.Range(0, 500).Select(_ => _faker.Number.SByte()).Min().ShouldBeGreaterThanOrEqualTo((sbyte)0);
    }

    [Test]
    public void ULong_covers_the_whole_unsigned_range()
    {
        var all = Enumerable.Range(0, 200).Select(_ => _faker.Number.ULong()).ToList();
        all.ShouldContain(v => v > long.MaxValue);
        all.ShouldContain(v => v <= long.MaxValue);

        var top = Enumerable.Range(0, 200).Select(_ => _faker.Number.ULong(ulong.MaxValue - 1, ulong.MaxValue)).ToList();
        top.Distinct().Order().ShouldBe([ulong.MaxValue - 1, ulong.MaxValue]);
        _faker.Number.ULong(7, 7).ShouldBe(7ul);
    }

    [Test]
    public void Int128_and_UInt128_cover_their_whole_ranges()
    {
        for (var i = 0; i < 200; i++)
        {
            _faker.Number.Int128(-10, 10).ShouldBeInRange((Int128)(-10), (Int128)10);
            _faker.Number.UInt128(UInt128.MaxValue - 3, UInt128.MaxValue).ShouldBeGreaterThanOrEqualTo(UInt128.MaxValue - 3);
        }

        var wide = Enumerable.Range(0, 200).Select(_ => _faker.Number.Int128(Int128.MinValue, Int128.MaxValue)).ToList();
        wide.ShouldContain(v => v < 0);
        wide.ShouldContain(v => v > 0);
        wide.ShouldContain(v => v > long.MaxValue || v < long.MinValue);
        Enumerable.Range(0, 200).Select(_ => _faker.Number.UInt128()).ShouldContain(v => v > ulong.MaxValue);
    }

    [Test]
    public void Single_and_Half_are_in_range_and_never_reach_the_open_upper_end()
    {
        for (var i = 0; i < 5000; i++)
        {
            _faker.Number.Single().ShouldBeInRange(0f, 0.99999994f);
            _faker.Number.Half((Half)0, (Half)2).ShouldBeLessThan((Half)2);
        }

        for (var i = 0; i < 200; i++)
        {
            var rounded = _faker.Number.Single(1, 3, fractionDigits: 1);
            rounded.ShouldBeInRange(1f, 3f);
            (Math.Abs(rounded * 10 - MathF.Round(rounded * 10)) < 1e-3).ShouldBeTrue();
            _faker.Number.Half((Half)(-1), (Half)1, fractionDigits: 2).ShouldBeInRange((Half)(-1), (Half)1);
        }
    }

    [Test]
    public void Reversed_bounds_are_refused_with_the_message_of_the_other_calls()
    {
        Should.Throw<ArgumentException>(() => _faker.Number.Byte(5, 4)).Message.ShouldContain("Max");
        Should.Throw<ArgumentException>(() => _faker.Number.SByte(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.Short(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.UShort(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.UInt(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.ULong(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.Int128(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.UInt128(5, 4));
        Should.Throw<ArgumentException>(() => _faker.Number.Single(2, 1));
        Should.Throw<ArgumentException>(() => _faker.Number.Half((Half)2, (Half)1));
        Should.Throw<ArgumentOutOfRangeException>(() => _faker.Number.Single(0, 1, fractionDigits: -1));
    }

    [Test]
    public void A_seed_gives_the_same_values()
    {
        string Take()
        {
            var f = new Faker("en", seed: 4);
            return string.Join("|", f.Number.Byte(), f.Number.SByte(), f.Number.Short(), f.Number.UShort(), f.Number.UInt(), f.Number.ULong(),
                f.Number.Int128(), f.Number.UInt128(), f.Number.Single(), f.Number.Half());
        }

        Take().ShouldBe(Take());
    }

    [Test]
    public void SeededRandom_ULong_validates_and_covers_the_range()
    {
        var random = new SeededRandom(3);

        Should.Throw<ArgumentException>(() => random.ULong(2, 1)).Message.ShouldContain("Max");
        Enumerable.Range(0, 200).Select(_ => random.ULong(0, ulong.MaxValue)).ShouldContain(v => v > long.MaxValue);
        random.ULong(ulong.MaxValue, ulong.MaxValue).ShouldBe(ulong.MaxValue);
        Enumerable.Range(0, 100).Select(_ => random.ULong(10, 12)).Distinct().Order().ShouldBe([10ul, 11ul, 12ul]);
    }
}
