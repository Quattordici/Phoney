using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Person180
{
    public double Height { get; set; }
    public decimal Weight { get; set; }
    public int Iq { get; set; }
    public byte Shoe { get; set; }
    public long Steps { get; set; }
    public Half Temperature { get; set; }
    public int? Optional { get; set; }
    public string Name { get; set; } = "";
    public Measures Body { get; set; } = null!;
    public List<Measures> History { get; set; } = [];
}

public sealed class Measures
{
    public double Waist { get; set; }
    public int Pulse { get; set; }
}

[FakeFor<Person180>]
public static partial class NormalFakes;

/// <summary>Numbers drawn from a normal (Gaussian) distribution: the primitive, the number module and the <c>WithNormalDistribution</c> rule.</summary>
public sealed class NormalDistributionTests
{
    private static SeededRandom Seeded(long seed = 7) => new(seed);

    private static double[] Samples(int count, double mean = 0, double deviation = 1, long seed = 7)
    {
        var random = Seeded(seed);
        var samples = new double[count];
        for (var i = 0; i < count; i++)
            samples[i] = random.Normal(mean, deviation);
        return samples;
    }

    // --- the primitive

    [Test]
    public void The_samples_have_the_mean_and_deviation_asked_for()
    {
        var samples = Samples(200_000, mean: 50, deviation: 10);

        var mean = samples.Average();
        var variance = samples.Sum(s => (s - mean) * (s - mean)) / (samples.Length - 1);

        mean.ShouldBe(50, tolerance: 0.1);
        Math.Sqrt(variance).ShouldBe(10, tolerance: 0.1);
    }

    [Test]
    public void The_shape_is_a_bell_with_the_68_95_99_7_rule_and_no_skew()
    {
        var samples = Samples(200_000);

        Fraction(samples, s => Math.Abs(s) < 1).ShouldBe(0.6827, tolerance: 0.005);
        Fraction(samples, s => Math.Abs(s) < 2).ShouldBe(0.9545, tolerance: 0.003);
        Fraction(samples, s => Math.Abs(s) < 3).ShouldBe(0.9973, tolerance: 0.001);
        Fraction(samples, s => s > 0).ShouldBe(0.5, tolerance: 0.005);
        var mean = samples.Average();
        var skew = samples.Sum(s => Math.Pow(s - mean, 3)) / samples.Length;
        skew.ShouldBe(0, tolerance: 0.03);
        var kurtosis = samples.Sum(s => Math.Pow(s - mean, 4)) / samples.Length;
        kurtosis.ShouldBe(3, tolerance: 0.1); // the tails are there too

        static double Fraction(double[] values, Func<double, bool> test) => values.Count(test) / (double)values.Length;
    }

    [Test]
    public void A_seed_gives_the_same_samples_and_another_seed_other_ones()
    {
        Samples(50, seed: 3).ShouldBe(Samples(50, seed: 3));
        Samples(50, seed: 3).ShouldNotBe(Samples(50, seed: 4));
    }

    [Test]
    public void A_sample_always_draws_two_numbers_so_what_follows_does_not_depend_on_its_value()
    {
        var random = Seeded();
        var expected = Seeded();

        random.Normal(100, 15);
        expected.Double();
        expected.Double();

        random.Long(0, long.MaxValue).ShouldBe(expected.Long(0, long.MaxValue));
    }

    [Test]
    public void Exact_samples_are_pinned_so_that_every_platform_gives_the_same_ones()
    {
        // Only +, -, *, / and comparisons are used (no Math.Log or Math.Cos, which the platforms round differently), so CI on Linux and Windows must agree to the last bit.
        var bits = Samples(5, seed: 1).Select(BitConverter.DoubleToInt64Bits).ToArray();

        bits.ShouldBe(PinnedBits);
    }

    // The doubles -1.5452228371402943, -1.0136476397283942, 0.9582971460957352, -0.2818097769535309 and -1.9041346336254346, also reproduced bit for bit by an independent
    // implementation of the same arithmetic in Python (xoshiro256** seeded with SplitMix64, then the Box–Muller steps above).
    private static readonly long[] PinnedBits = [-4613734152688579486L, -4616128154549563117L, 4606806792885410342L, -4624623783310960751L, -4612117757655670133L];

    [Test]
    public void The_logarithm_and_cosine_it_uses_agree_with_the_system_ones()
    {
        var random = Seeded(11);
        for (var i = 0; i < 50_000; i++)
        {
            var u = 1.0 - random.Double(); // (0, 1]
            PureMath.Ln(u).ShouldBe(Math.Log(u), tolerance: 1e-13);
            var turn = random.Double();
            PureMath.CosTurns(turn).ShouldBe(Math.Cos(2 * Math.PI * turn), tolerance: 1e-13);
        }

        PureMath.Ln(1).ShouldBe(0);
        PureMath.Ln(double.Epsilon * 1e300).ShouldBe(Math.Log(double.Epsilon * 1e300), tolerance: 1e-9);
        PureMath.Ln(2).ShouldBe(Math.Log(2), tolerance: 1e-15);
        PureMath.CosTurns(0).ShouldBe(1);
        PureMath.CosTurns(0.25).ShouldBe(0, tolerance: 1e-15);
        PureMath.CosTurns(0.5).ShouldBe(-1, tolerance: 1e-15);
        PureMath.CosTurns(0.75).ShouldBe(0, tolerance: 1e-15);
    }

    [Test]
    public void A_deviation_of_zero_gives_the_mean_and_bad_arguments_are_refused()
    {
        Seeded().Normal(42, 0).ShouldBe(42);
        Should.Throw<ArgumentOutOfRangeException>(() => Seeded().Normal(0, -1)).ParamName.ShouldBe("standardDeviation");
        Should.Throw<ArgumentOutOfRangeException>(() => Seeded().Normal(0, double.NaN));
        Should.Throw<ArgumentOutOfRangeException>(() => Seeded().Normal(0, double.PositiveInfinity));
        Should.Throw<ArgumentOutOfRangeException>(() => Seeded().Normal(double.NaN, 1)).ParamName.ShouldBe("mean");
        Should.Throw<ArgumentOutOfRangeException>(() => Seeded().Normal(double.PositiveInfinity, 1));
    }

    // --- the number module

    [Test]
    public void Normal_stays_inside_the_limits_by_drawing_again_and_keeps_the_bell_shape()
    {
        var faker = new Faker("en", seed: 5);

        var values = Enumerable.Range(0, 50_000).Select(_ => faker.Number.Normal(100, 15, min: 85, max: 130)).ToArray();

        values.ShouldAllBe(v => v >= 85 && v <= 130);
        values.Count(v => v < 100).ShouldBeGreaterThan(values.Count(v => v >= 100) / 2); // the lower side is cut closer, but both sides are there
        values.Average().ShouldBeInRange(100, 105);
        values.Count(v => Math.Abs(v - 100) < 7.5).ShouldBeGreaterThan(values.Length * 4 / 10); // most of it is near the middle
    }

    [Test]
    public void Normal_with_limits_far_in_the_tail_ends_up_inside_them_and_does_not_loop()
    {
        var faker = new Faker("en", seed: 5);

        for (var i = 0; i < 20; i++)
            faker.Number.Normal(0, 1, min: 100, max: 101).ShouldBeInRange(100, 101);
        for (var i = 0; i < 20; i++)
            faker.Number.Normal(0, 1, min: -101, max: -100).ShouldBeInRange(-101, -100);
    }

    [Test]
    public void The_typed_overload_rounds_whole_numbers_and_gives_two_decimals_otherwise()
    {
        var faker = new Faker("en", seed: 5);

        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<int>(100, 15)).ShouldAllBe(v => v > 0 && v < 200);
        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<double>(100, 15)).ShouldAllBe(v => Math.Round(v, 2) == v);
        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<decimal>(100, 15)).ShouldAllBe(v => Math.Round(v, 2) == v);
        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<double>(100, 15, decimals: 0)).ShouldAllBe(v => Math.Round(v) == v);
        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<double>(1, 0.2, decimals: 4)).ShouldAllBe(v => Math.Round(v, 4) == v);
        Enumerable.Range(0, 200).Select(_ => faker.Number.Normal<byte>(128, 100)).ShouldAllBe(v => v >= 0); // saturates at the type's limits
        faker.Number.Normal<System.Numerics.BigInteger>(1000, 10).ShouldBeInRange(500, 1500);
    }

    [Test]
    public void Rounding_never_takes_a_value_out_of_the_limits()
    {
        var faker = new Faker("en", seed: 5);

        Enumerable.Range(0, 2000).Select(_ => faker.Number.Normal<int>(0, 3, min: 0.4, max: 2.6)).ShouldAllBe(v => v >= 1 && v <= 2);
        Enumerable.Range(0, 2000).Select(_ => faker.Number.Normal<double>(0, 3, min: 0.004, max: 0.016)).ShouldAllBe(v => v >= 0.01 && v <= 0.01);
    }

    [Test]
    public void The_module_refuses_bad_arguments()
    {
        var faker = new Faker("en", seed: 5);

        Should.Throw<ArgumentException>(() => faker.Number.Normal(0, 1, min: 5, max: 1)).ParamName.ShouldBe("max");
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Normal(0, -1));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Normal<int>(0, 1, decimals: -1));
        Should.Throw<ArgumentOutOfRangeException>(() => faker.Number.Normal(0, 1, min: double.NaN));
    }

    [Test]
    public void It_is_a_template_function()
    {
        var faker = new Faker("en", seed: 5);

        var values = Enumerable.Range(0, 200).Select(_ => double.Parse(faker.Parse("{{number.normal(50, 5)}}"), System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        values.Average().ShouldBeInRange(48, 52);
        values.ShouldAllBe(v => v > 20 && v < 80);
    }

    // --- the rule

    private static ObjectGenerator<Person180> Generated() => NormalFakes.Person180.Seed(21);

    private static ObjectGenerator<Person180> Reflected() => new ObjectGenerator<Person180>(new ReflectionModel<Person180>()).Seed(21);

    private static ObjectGenerator<Person180> WithRules(ObjectGenerator<Person180> generator) => generator
        .WithNormalDistribution(p => p.Height, 175, 8)
        .WithNormalDistribution(p => p.Weight, 75, 10)
        .WithNormalDistribution(p => p.Iq, 100, 15)
        .WithNormalDistribution(p => p.Shoe, 42, 2)
        .WithNormalDistribution(p => p.Steps, 8000, 2500, min: 0)
        .WithNormalDistribution(p => p.Temperature, 36.6, 0.3)
        .WithNormalDistribution(p => p.Optional, 10, 1)
        .WithNormalDistribution(p => p.Body.Waist, 85, 9)
        .WithNormalDistribution(p => p.History.Each().Pulse, 70, 8, min: 40, max: 120);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_rule_gives_a_member_normally_distributed_values_of_its_type(bool generated)
    {
        var people = WithRules(generated ? Generated() : Reflected()).NullProbability(0).Generate(5_000);

        people.Average(p => p.Height).ShouldBe(175, tolerance: 0.5);
        Math.Sqrt(people.Sum(p => Math.Pow(p.Height - 175, 2)) / people.Count).ShouldBe(8, tolerance: 0.4);
        ((double)people.Average(p => p.Weight)).ShouldBe(75, tolerance: 0.6);
        people.Average(p => p.Iq).ShouldBe(100, tolerance: 1);
        people.Average(p => p.Shoe).ShouldBe(42, tolerance: 0.2);
        people.ShouldAllBe(p => p.Steps >= 0);
        people.Average(p => p.Steps).ShouldBe(8000, tolerance: 150);
        people.Average(p => (double)p.Temperature).ShouldBe(36.6, tolerance: 0.1);
        people.ShouldAllBe(p => p.Optional.HasValue);
        people.Average(p => p.Optional!.Value).ShouldBe(10, tolerance: 0.1);
        people.Average(p => p.Body.Waist).ShouldBe(85, tolerance: 0.5);
        people.SelectMany(p => p.History).ShouldAllBe(m => m.Pulse >= 40 && m.Pulse <= 120);
        people.SelectMany(p => p.History).Average(m => m.Pulse).ShouldBe(70, tolerance: 1);
        people.ShouldAllBe(p => Math.Round(p.Height, 2) == p.Height && Math.Round(p.Weight, 2) == p.Weight);
    }

    [Test]
    public void The_two_models_make_the_same_data()
    {
        string Text(Person180 p) => $"{p.Name}|{p.Height}|{p.Weight}|{p.Iq}|{p.Shoe}|{p.Steps}|{p.Temperature}|{p.Optional}|{p.Body.Waist}|{string.Join(',', p.History.Select(h => $"{h.Waist}/{h.Pulse}"))}";

        WithRules(Reflected()).Generate(20).Select(Text).ShouldBe(WithRules(Generated()).Generate(20).Select(Text));
    }

    [Test]
    public void Items_are_the_same_whichever_way_they_are_generated()
    {
        var sequential = WithRules(Generated()).Generate(30).Select(p => p.Height).ToList();
        var parallel = WithRules(Generated()).GenerateParallel(30).Select(p => p.Height).ToList();

        parallel.ShouldBe(sequential);
    }

    [Test]
    public void Strict_mode_counts_it_as_a_rule_and_a_later_rule_replaces_it()
    {
        var strict = Reflected().Strict().WithNormalDistribution(p => p.Height, 175, 8).WithNormalDistribution(p => p.Weight, 75, 10).WithNormalDistribution(p => p.Iq, 100, 15)
            .WithNormalDistribution(p => p.Steps, 8000, 2500).WithNormalDistribution(p => p.Shoe, 42, 2).WithNormalDistribution(p => p.Temperature, 36.6, 0.3);
        Should.NotThrow(() => strict.Ignore(p => p.Body).Ignore(p => p.History).Ignore(p => p.Optional).Ignore(p => p.Name).Generate());

#pragma warning disable PHONEY008 // replacing the earlier rule is what this test is about
        Reflected().WithNormalDistribution(p => p.Height, 175, 8).With(p => p.Height, 1.0).Generate().Height.ShouldBe(1.0);
        Reflected().With(p => p.Height, 1.0).WithNormalDistribution(p => p.Height, 175, 8).Generate().Height.ShouldNotBe(1.0);
#pragma warning restore PHONEY008
    }

    [Test]
    public void Arguments_are_checked_when_the_rule_is_added()
    {
        double negative = -1, notANumber = double.NaN, ten = 10, one = 1; // not constants: PHONEY011 reports constant mistakes at compile time
        var tooFew = -2;
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().WithNormalDistribution(p => p.Height, 175, negative));
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().WithNormalDistribution(p => p.Height, notANumber, 1));
        Should.Throw<ArgumentException>(() => Reflected().WithNormalDistribution(p => p.Height, 175, 8, min: ten, max: one));
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().WithNormalDistribution(p => p.Height, 175, 8, decimals: tooFew));
        Should.Throw<ArgumentNullException>(() => Reflected().WithNormalDistribution<double>(null!, 175, 8));
    }
}
