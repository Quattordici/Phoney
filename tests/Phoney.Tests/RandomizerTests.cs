namespace Phoney.Tests;

public sealed class RandomizerTests
{
    [Test]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new Randomizer(42);
        var b = new Randomizer(42);
        for (var i = 0; i < 1000; i++)
            a.NextUInt64().ShouldBe(b.NextUInt64());
    }

    [Test]
    public void Sequence_is_stable_across_platforms_and_versions()
    {
        // Golden values: xoshiro256** seeded through SplitMix64. Changing them breaks every seeded user test.
        var random = new Randomizer(42);
        var values = Enumerable.Range(0, 3).Select(_ => random.NextUInt64()).ToArray();
        values.ShouldBe(GoldenValues);
    }

    [Test]
    public void Reseed_restarts_the_sequence()
    {
        var random = new Randomizer(7);
        var first = random.Int(0, 1_000_000);
        random.Reseed(7);
        random.Int(0, 1_000_000).ShouldBe(first);
        random.Seed.ShouldBe(7);
    }

    [Test]
    public void Int_is_inclusive_and_covers_the_whole_range()
    {
        var random = new Randomizer(1);
        var seen = new HashSet<int>();
        for (var i = 0; i < 10_000; i++)
        {
            var value = random.Int(-3, 3);
            value.ShouldBeInRange(-3, 3);
            seen.Add(value);
        }

        seen.Count.ShouldBe(7);
    }

    [Test]
    public void Extreme_ranges_do_not_overflow()
    {
        var random = new Randomizer(1);
        _ = random.Int(int.MinValue, int.MaxValue);
        _ = random.Long(long.MinValue, long.MaxValue);
        random.Int(5, 5).ShouldBe(5);
        Should.Throw<ArgumentException>(() => random.Int(2, 1));
    }

    [Test]
    public void Double_is_in_the_half_open_unit_interval()
    {
        var random = new Randomizer(3);
        for (var i = 0; i < 10_000; i++)
            random.Double().ShouldBeInRange(0, 0.9999999999999999);
    }

    [Test]
    public void Weighted_index_follows_the_weights()
    {
        var random = new Randomizer(5);
        int[] cumulative = [1, 1, 10]; // weights 1, 0, 9
        var counts = new int[3];
        for (var i = 0; i < 10_000; i++)
            counts[random.WeightedIndex(cumulative)]++;

        counts[1].ShouldBe(0);
        counts[2].ShouldBeGreaterThan(counts[0] * 5);
    }

    [Test]
    public void Guid_is_version_4()
    {
        var guid = new Randomizer(9).Guid().ToString();
        guid[14].ShouldBe('4');
        "89ab".ShouldContain(guid[19]);
    }

    [Test]
    public void Derived_seeds_are_distinct_and_deterministic()
    {
        var seeds = Enumerable.Range(0, 1000).Select(i => Randomizer.DeriveSeed(42, i)).ToList();
        seeds.Distinct().Count().ShouldBe(1000);
        Randomizer.DeriveSeed(42, 10).ShouldBe(seeds[10]);
    }

    [Test]
    public void Replace_substitutes_symbols()
    {
        var value = new Randomizer(2).Replace("##-??-**");
        value.ShouldMatch("^[0-9]{2}-[A-Z]{2}-[0-9A-Z]{2}$");
    }

    private static readonly ulong[] GoldenValues = ComputeGolden();

    /// <summary>
    /// Reference implementation written independently from <see cref="Randomizer"/> (straight from the
    /// published xoshiro256** and SplitMix64 algorithms) to pin the output.
    /// </summary>
    private static ulong[] ComputeGolden()
    {
        var x = 42UL;
        ulong Mix()
        {
            var z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        ulong[] s = [Mix(), Mix(), Mix(), Mix()];
        static ulong Rotl(ulong v, int k) => (v << k) | (v >> (64 - k));
        var result = new ulong[3];
        for (var i = 0; i < 3; i++)
        {
            result[i] = Rotl(s[1] * 5, 7) * 9;
            var t = s[1] << 17;
            s[2] ^= s[0];
            s[3] ^= s[1];
            s[1] ^= s[2];
            s[0] ^= s[3];
            s[2] ^= t;
            s[3] = Rotl(s[3], 45);
        }

        return result;
    }
}
