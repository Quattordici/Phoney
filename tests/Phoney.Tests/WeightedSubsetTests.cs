using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Crew
{
    public List<string> Roles { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public HashSet<int> Numbers { get; set; } = [];
    public ISet<string> Groups { get; set; } = null!;
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public Squad2 Squad { get; set; } = null!;
}

public sealed class Squad2
{
    public List<string> Members { get; set; } = [];
}

[FakeFor<Crew>]
public static partial class WeightedSubsetFakes;

/// <summary>A random subset in which heavier values are more likely to be included.</summary>
public sealed class WeightedSubsetTests
{
    private static readonly string[] Pool = ["common", "usual", "rare", "unique"];
    private static readonly double[] Weights = [8, 4, 2, 1];

    private static ObjectGenerator<Crew> Generated() => WeightedSubsetFakes.Crew.Seed(8);

    private static ObjectGenerator<Crew> Reflected() => new ObjectGenerator<Crew>(new ReflectionModel<Crew>()).Seed(8);

    /// <summary>The chance that each value is in a sample of two taken one at a time, each time by weight among those left: P(first) + Σ P(other first) · w / (rest).</summary>
    private static double[] ExpectedInclusionOfTwo(double[] weights)
    {
        var total = weights.Sum();
        return weights.Select((w, i) => (w / total) + weights.Select((o, j) => j == i ? 0 : o / total * (w / (total - o))).Sum()).ToArray();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Values_are_included_in_proportion_to_their_weight_like_taking_one_at_a_time(bool generated)
    {
        var crews = (generated ? Generated() : Reflected()).WithOneOf(c => c.Roles, Pool, Weights, 2).Generate(30_000);

        var expected = ExpectedInclusionOfTwo(Weights);
        for (var i = 0; i < Pool.Length; i++)
            (crews.Count(c => c.Roles.Contains(Pool[i])) / (double)crews.Count).ShouldBe(expected[i], tolerance: 0.012, Pool[i]);

        crews.ShouldAllBe(c => c.Roles.Count == 2 && c.Roles.Distinct().Count() == 2);
    }

    [Test]
    public void A_subset_of_one_is_a_weighted_pick()
    {
        var crews = Reflected().WithOneOf(c => c.Roles, Pool, Weights, 1).Generate(30_000);

        for (var i = 0; i < Pool.Length; i++)
            (crews.Count(c => c.Roles.Contains(Pool[i])) / 30_000.0).ShouldBe(Weights[i] / 15, tolerance: 0.012);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void It_works_for_every_kind_of_collection_in_the_order_of_the_list(bool generated)
    {
        var crews = (generated ? Generated() : Reflected())
            .WithOneOf(c => c.Roles, Pool, Weights, 3)
            .WithOneOf(c => c.Tags, Pool, Weights, (1, 3))
            .WithOneOf(c => c.Numbers, new[] { 1, 2, 3, 4 }, Weights, 2)
            .WithOneOf(c => c.Groups, Pool, Weights, 2)
            .WithOneOf(c => c.Permissions, Pool, Weights, 4)
            .WithOneOf(c => c.Squad.Members, Pool, Weights, 2)
            .Generate(200);

        foreach (var crew in crews)
        {
            crew.Roles.Count.ShouldBe(3);
            crew.Roles.Select(r => Array.IndexOf(Pool, r)).ShouldBe(crew.Roles.Select(r => Array.IndexOf(Pool, r)).Order());
            crew.Tags.Length.ShouldBeInRange(1, 3);
            crew.Tags.Distinct().Count().ShouldBe(crew.Tags.Length);
            crew.Numbers.Count.ShouldBe(2);
            crew.Groups.ShouldBeOfType<HashSet<string>>().Count.ShouldBe(2);
            crew.Permissions.Count.ShouldBe(4);
            crew.Squad.Members.Count.ShouldBe(2);
        }
    }

    [Test]
    public void A_value_with_weight_zero_is_never_included_and_the_others_can_fill_a_subset_alone()
    {
        string[] pool = ["a", "b", "never", "c"];
        double[] weights = [1, 1, 0, 1];

        var crews = Reflected().WithOneOf(c => c.Roles, pool, weights, 3).Generate(500);

        crews.ShouldAllBe(c => c.Roles.SequenceEqual(new[] { "a", "b", "c" })); // all three that can be, in the order of the list
    }

    [Test]
    public void Very_uneven_weights_neither_loop_nor_fail()
    {
        string[] pool = ["huge", "tiny1", "tiny2", "tiny3"];
        double[] weights = [1e12, 1, 1, 1];

        var crews = Reflected().WithOneOf(c => c.Roles, pool, weights, 3).Generate(2_000);

        crews.ShouldAllBe(c => c.Roles.Count == 3 && c.Roles.Distinct().Count() == 3);
        (crews.Count(c => c.Roles.Contains("huge")) / 2000.0).ShouldBeGreaterThan(0.999);
    }

    [Test]
    public void It_draws_the_size_when_it_is_a_range_and_then_one_number_per_value()
    {
        var picker = SubsetPicker<string>.Create(Pool, Weights, (2, 2), "values");
        var random = new SeededRandom(5);
        var expected = new SeededRandom(5);

        picker.List(random);
        expected.Double();
        expected.Double();

        random.Long(0, long.MaxValue).ShouldBe(expected.Long(0, long.MaxValue));

        var ranged = SubsetPicker<string>.Create(Pool, Weights, (1, 3), "values");
        var other = new SeededRandom(5);
        var otherExpected = new SeededRandom(5);
        var size = ranged.List(other).Count;
        otherExpected.Int(1, 3);
        for (var i = 0; i < size; i++)
            otherExpected.Double();
        other.Long(0, long.MaxValue).ShouldBe(otherExpected.Long(0, long.MaxValue));
    }

    [Test]
    public void The_two_models_make_the_same_subsets_a_seed_repeats_them_and_item_n_is_stable()
    {
        ObjectGenerator<Crew> Ruled(ObjectGenerator<Crew> g) => g.WithOneOf(c => c.Roles, Pool, Weights, (1, 3)).WithOneOf(c => c.Tags, Pool, Weights, 2);
        string Text(Crew c) => $"{string.Join(',', c.Roles)}|{string.Join(',', c.Tags)}";

        var generated = Ruled(Generated()).Generate(100).Select(Text).ToList();

        generated.ShouldBe(Ruled(Reflected()).Generate(100).Select(Text));
        generated.ShouldBe(Ruled(Generated()).Generate(100).Select(Text));
        Ruled(Generated()).GenerateParallel(100).Select(Text).ShouldBe(generated);
        Ruled(WeightedSubsetFakes.Crew.Seed(9)).Generate(100).Select(Text).ShouldNotBe(generated);
    }

    [Test]
    public void The_lists_are_copied_when_the_rule_is_added()
    {
        string[] pool = ["a", "b"];
        double[] weights = [1, 0];
        var generator = Reflected().WithOneOf(c => c.Roles, pool, weights, 1);

        weights[0] = 0;
        weights[1] = 1;
        pool[0] = "changed";

        generator.Generate(50).ShouldAllBe(c => c.Roles.SequenceEqual(new[] { "a" }));
    }

    [Test]
    public void Arguments_are_checked_when_the_rule_is_added()
    {
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, 2.0 }, 1)).Message.ShouldContain("4 values and 2 weights");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, -2.0, 1, 1 }, 1)).Message.ShouldContain("negative");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, double.NaN, 1, 1 }, 1));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, double.PositiveInfinity, 1, 1 }, 1));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, 0, 0, 1 }, 3)).Message.ShouldContain("3 of the 2 values with a weight above zero");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(c => c.Roles, Pool, new[] { 1.0, 1, 1, 1 }, 5)).Message.ShouldContain("5 of the 4 values");
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(c => c.Roles, Pool, (IReadOnlyList<double>)null!, 1));
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(c => c.Roles, (IReadOnlyList<string>)null!, Weights, 1));
    }
}
