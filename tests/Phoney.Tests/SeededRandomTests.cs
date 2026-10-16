namespace Phoney.Tests;

public sealed class SeededRandomTests
{
    [Test]
    public void Same_seed_gives_the_same_sequence()
    {
        var a = new SeededRandom(42);
        var b = new SeededRandom(42);
        for (var i = 0; i < 1000; i++)
            a.NextUInt64().ShouldBe(b.NextUInt64());
    }

    [Test]
    public void Sequence_is_stable_across_platforms_and_versions()
    {
        // Golden values: xoshiro256** seeded through SplitMix64. Changing them breaks every seeded user test.
        var random = new SeededRandom(42);
        var values = Enumerable.Range(0, 3).Select(_ => random.NextUInt64()).ToArray();
        values.ShouldBe(GoldenValues);
    }

    [Test]
    public void Element_picks_from_any_sequence_with_the_draw_of_a_list()
    {
        var set = new HashSet<int> { 10, 20, 30, 40 };
        var list = set.ToList();

        new SeededRandom(5).Element(set).ShouldBe(new SeededRandom(5).Element(list));
        new SeededRandom(5).Element(set).ShouldBe(20);
        new SeededRandom(5).Element(new Dictionary<string, int> { ["a"] = 1 }.Keys).ShouldBe("a");
        new SeededRandom(5).Element(list.Select(x => x + 1)).ShouldBe(new SeededRandom(5).Element(list) + 1);
    }

    [Test]
    public void Element_of_an_empty_or_missing_sequence_fails_clearly()
    {
        Should.Throw<ArgumentException>(() => new SeededRandom(1).Element(new HashSet<int>())).ParamName.ShouldBe("items");
        Should.Throw<ArgumentNullException>(() => new SeededRandom(1).Element((IEnumerable<int>)null!));
    }

    [Test]
    public void Shuffled_returns_a_new_array_with_the_same_items_and_leaves_the_source_alone()
    {
        var source = Enumerable.Range(1, 20).ToList();

        var shuffled = new SeededRandom(9).Shuffled(source.Where(_ => true));

        shuffled.Order().ShouldBe(source);
        shuffled.SequenceEqual(source).ShouldBeFalse();
        shuffled.ShouldBe(new SeededRandom(9).Shuffled(source));
        shuffled.ShouldBe([4, 18, 14, 20, 11, 17, 6, 16, 9, 8, 19, 2, 7, 10, 12, 15, 13, 3, 5, 1]);
        source.ShouldBe(Enumerable.Range(1, 20));
        Should.Throw<ArgumentNullException>(() => new SeededRandom(1).Shuffled<int>(null!));
    }

    [Test]
    public void Enum_picks_a_value_without_allocating()
    {
        var random = new SeededRandom(1);
        random.Enum<DayOfWeek>(); // warm up the per-enum cache
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            random.Enum<DayOfWeek>();
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBeLessThan(1024);
    }

    [Test]
    public void Reseed_restarts_the_sequence()
    {
        var random = new SeededRandom(7);
        var first = random.Int(0, 1_000_000);
        random.Reseed(7);
        random.Int(0, 1_000_000).ShouldBe(first);
        random.Seed.ShouldBe(7);
    }

    [Test]
    public void Int_is_inclusive_and_covers_the_whole_range()
    {
        var random = new SeededRandom(1);
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
        var random = new SeededRandom(1);
        _ = random.Int(int.MinValue, int.MaxValue);
        _ = random.Long(long.MinValue, long.MaxValue);
        random.Int(5, 5).ShouldBe(5);
        Should.Throw<ArgumentException>(() => random.Int(2, 1));
    }

    [Test]
    public void Double_is_in_the_half_open_unit_interval()
    {
        var random = new SeededRandom(3);
        for (var i = 0; i < 10_000; i++)
            random.Double().ShouldBeInRange(0, 0.9999999999999999);
    }

    [Test]
    public void Weighted_index_follows_the_weights()
    {
        var random = new SeededRandom(5);
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
        var guid = new SeededRandom(9).Guid().ToString();
        guid[14].ShouldBe('4');
        "89ab".ShouldContain(guid[19]);
    }

    [Test]
    public void Derived_seeds_are_distinct_and_deterministic()
    {
        var seeds = Enumerable.Range(0, 1000).Select(i => SeededRandom.DeriveSeed(42, i)).ToList();
        seeds.Distinct().Count().ShouldBe(1000);
        SeededRandom.DeriveSeed(42, 10).ShouldBe(seeds[10]);
    }

    [Test]
    public void Replace_substitutes_symbols()
    {
        var value = new SeededRandom(2).ReplaceSymbols("##-??-**");
        value.ShouldMatch("^[0-9]{2}-[A-Z]{2}-[0-9A-Z]{2}$");
    }

    private static readonly ulong[] GoldenValues = ComputeGolden();

    /// <summary>
    /// Reference implementation written independently from <see cref="SeededRandom"/> (straight from the
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

    [Test]
    public void With_a_comparer_the_source_order_does_not_matter()
    {
        string[] one = ["delta", "alpha", "charlie", "bravo", "echo"];
        string[] other = ["echo", "bravo", "alpha", "delta", "charlie"];

        for (var seed = 1; seed <= 20; seed++)
        {
            new SeededRandom(seed).Element(one, StringComparer.Ordinal).ShouldBe(new SeededRandom(seed).Element(other, StringComparer.Ordinal));
            new SeededRandom(seed).Shuffled(one, StringComparer.Ordinal).ShouldBe(new SeededRandom(seed).Shuffled(other, StringComparer.Ordinal));
        }

        new SeededRandom(1).Shuffled(one, StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBe(["alpha", "bravo", "charlie", "delta", "echo"]);
        one.ShouldBe(["delta", "alpha", "charlie", "bravo", "echo"]); // the source is left as it is
        Should.Throw<ArgumentNullException>(() => new SeededRandom(1).Element(one, null!));
        Should.Throw<ArgumentNullException>(() => new SeededRandom(1).Shuffled(one, null!));
        Should.Throw<ArgumentException>(() => new SeededRandom(1).Element(Array.Empty<string>(), StringComparer.Ordinal));
    }
}
