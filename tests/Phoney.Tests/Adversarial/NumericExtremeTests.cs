using System.Numerics;

namespace Phoney.Tests.Adversarial;

/// <summary>Numbers at the edges of their types: full ranges, overflowing spreads, NaN and infinity, and weight tables that are empty or add up to nothing.</summary>
public sealed class NumericExtremeTests
{
    private const int Draws = 2_000;

    private static Faker Faker(long seed = 1) => new("en", seed: seed);

    [Test]
    public void Full_ranges_of_every_integer_type_stay_inside_and_reach_both_halves()
    {
        var faker = Faker();
        var longs = Enumerable.Range(0, Draws).Select(_ => faker.Number.Long(long.MinValue, long.MaxValue)).ToList();
        var int128s = Enumerable.Range(0, Draws).Select(_ => faker.Number.Int128(Int128.MinValue, Int128.MaxValue)).ToList();
        var ulongs = Enumerable.Range(0, Draws).Select(_ => faker.Number.ULong(0, ulong.MaxValue)).ToList();
        var uint128s = Enumerable.Range(0, Draws).Select(_ => faker.Number.UInt128(UInt128.MinValue, UInt128.MaxValue)).ToList();

        longs.ShouldContain(x => x < 0);
        longs.ShouldContain(x => x > 0);
        int128s.ShouldContain(x => x < 0);
        int128s.ShouldContain(x => x > long.MaxValue);
        ulongs.ShouldContain(x => x > long.MaxValue);
        uint128s.ShouldContain(x => x > ulong.MaxValue);
    }

    [Test]
    [Arguments(long.MinValue, long.MaxValue, long.MaxValue)]
    [Arguments(long.MinValue, long.MaxValue, 2L)]
    [Arguments(long.MinValue, long.MinValue + 1, long.MaxValue)]
    [Arguments(long.MaxValue - 1, long.MaxValue, 3L)]
    public void Multiples_at_the_ends_of_long_do_not_overflow(long min, long max, long multipleOf)
    {
        var faker = Faker();
        for (var i = 0; i < 200; i++)
        {
            var value = faker.Number.Long(min, max, multipleOf);
            value.ShouldBeInRange(min, max);
            (value % multipleOf).ShouldBe(0L);
        }
    }

    [Test]
    public void An_int_multiple_near_the_top_of_int_is_still_an_int_in_range()
    {
        var faker = Faker();
        for (var i = 0; i < 200; i++)
            faker.Number.Int(int.MaxValue - 5, int.MaxValue, 3).ShouldBeInRange(int.MaxValue - 5, int.MaxValue);
    }

    [Test]
    public void Big_integers_hundreds_of_digits_wide_stay_inside_their_range()
    {
        var limit = BigInteger.Pow(10, 500);
        var faker = Faker();
        for (var i = 0; i < 200; i++)
            BigInteger.Abs(faker.Number.BigInteger(-limit, limit)).ShouldBeLessThanOrEqualTo(limit);
    }

    [Test]
    public void Floating_point_ranges_as_wide_as_the_type_give_finite_values()
    {
        var faker = Faker();
        for (var i = 0; i < Draws; i++)
        {
            float.IsFinite(faker.Number.Single(float.MinValue, float.MaxValue)).ShouldBeTrue();
            float.IsFinite(faker.Number.Single(float.MinValue, float.MaxValue, 2)).ShouldBeTrue();
            Half.IsFinite(faker.Number.Half(Half.MinValue, Half.MaxValue)).ShouldBeTrue();
            double.IsFinite(faker.Number.Double(double.MinValue, double.MaxValue, 3)).ShouldBeTrue();
        }
    }

    [Test]
    public void The_smallest_doubles_and_negative_zero_are_valid_ranges()
    {
        var faker = Faker();

        faker.Number.Double(double.Epsilon, double.Epsilon * 4).ShouldBeInRange(double.Epsilon, double.Epsilon * 4);
        faker.Number.Double(-0.0, 0.0).ShouldBe(0.0);
        faker.Number.Decimal(0.0000000000000000000000000001m, 0.0000000000000000000000000003m, 28)
            .ShouldBeInRange(0.0000000000000000000000000001m, 0.0000000000000000000000000003m);
    }

    [Test]
    public void A_spread_so_wide_that_the_bell_overflows_still_gives_finite_doubles()
    {
        var faker = Faker();
        for (var i = 0; i < Draws; i++)
        {
            double.IsFinite(faker.Number.Normal(0, double.MaxValue)).ShouldBeTrue();
            double.IsFinite(faker.Random.Normal(double.MaxValue, double.MaxValue)).ShouldBeTrue();
            double.IsFinite(faker.Number.Normal<double>(-double.MaxValue, double.MaxValue)).ShouldBeTrue();
        }
    }

    [Test]
    public void A_bell_beyond_a_types_range_saturates_at_its_largest_finite_value()
    {
        var faker = Faker();

        faker.Number.Normal<Half>(1e10, 1).ShouldBe(Half.MaxValue);
        faker.Number.Normal<Half>(-1e10, 1).ShouldBe(Half.MinValue);
        faker.Number.Normal<float>(1e300, 1).ShouldBe(float.MaxValue);
        faker.Number.Normal<float>(-1e300, 1).ShouldBe(float.MinValue);
        faker.Number.Normal<int>(1e300, 1).ShouldBe(int.MaxValue);
        faker.Number.Normal<byte>(-1000, 1).ShouldBe(byte.MinValue);
        faker.Number.Normal<decimal>(1e300, 1).ShouldBe(decimal.MaxValue);
    }

    [Test]
    public void Limits_far_out_in_a_tail_give_the_nearest_limit_instead_of_looping()
    {
        var faker = Faker();

        faker.Number.Normal(0, 1, 1000, 1001).ShouldBe(1000);
        faker.Number.Normal(0, 1, -1001, -1000).ShouldBe(-1000);
        faker.Number.Normal<int>(0, 1, 500, 500).ShouldBe(500);
    }

    [Test]
    public void An_empty_table_of_running_totals_is_an_argument_error()
    {
        var random = new SeededRandom(1);

        Should.Throw<ArgumentException>(() => random.WeightedIndex(ReadOnlySpan<double>.Empty)).ParamName.ShouldBe("cumulativeWeights");
        Should.Throw<ArgumentException>(() => random.WeightedIndex(ReadOnlySpan<int>.Empty)).ParamName.ShouldBe("cumulativeWeights");
    }

    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public void Running_totals_that_end_in_no_usable_weight_are_an_argument_error(double total)
    {
        var random = new SeededRandom(1);

        Should.Throw<ArgumentException>(() => random.WeightedIndex([0.0, total])).ParamName.ShouldBe("cumulativeWeights");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-3)]
    public void Integer_running_totals_that_end_in_no_weight_are_an_argument_error(int total)
    {
        var random = new SeededRandom(1);

        Should.Throw<ArgumentException>(() => random.WeightedIndex([0, total])).ParamName.ShouldBe("cumulativeWeights");
    }

    [Test]
    public void Valid_running_totals_draw_what_they_drew_before()
    {
        // The checks run before the draw and only reject tables that used to fail or give nonsense, so seeded picks don't move.
        var doubles = new SeededRandom(9);
        var ints = new SeededRandom(9);

        Enumerable.Range(0, 20).Select(_ => doubles.WeightedIndex([1.0, 4.0, 10.0])).ShouldBe([0, 1, 1, 2, 2, 2, 2, 2, 1, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2]);
        Enumerable.Range(0, 20).Select(_ => ints.WeightedIndex([1, 4, 10])).ShouldBe([0, 1, 1, 2, 2, 2, 2, 2, 1, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2]);
    }

    [Test]
    public void Weights_that_overflow_or_are_not_numbers_say_so()
    {
        var random = new SeededRandom(1);

        Should.Throw<ArgumentException>(() => random.Weighted(("a", double.MaxValue), ("b", double.MaxValue))).Message.ShouldContain("scale them down");
        Should.Throw<ArgumentException>(() => random.Weighted(("a", double.NaN)));
        Should.Throw<ArgumentException>(() => random.Weighted(("a", -1.0), ("b", 2.0)));
    }

    [Test]
    public void Numbers_in_other_bases_handle_the_smallest_int()
    {
        var faker = Faker();

        faker.Number.Binary(int.MinValue, int.MinValue).ShouldBe("-1" + new string('0', 31));
        faker.Number.Hex(int.MinValue, int.MinValue).ShouldBe("-80000000");
    }
}
