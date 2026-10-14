using System.Collections.Immutable;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Roster
{
    public string Name { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public HashSet<int> Numbers { get; set; } = [];
    public ISet<string> Groups { get; set; } = null!;
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public IEnumerable<string> Favorites { get; set; } = [];
    public ImmutableArray<string> Frozen { get; set; }
    public List<string> Tracked { get; } = [];
    public Squad Squad { get; set; } = null!;
    public List<Squad> Squads { get; set; } = [];
}

public sealed class Squad
{
    public List<string> Members { get; set; } = [];
}

[FakeFor<Roster>]
public static partial class SubsetFakes;

/// <summary><c>WithOneOf</c> for a collection member gives each object its own random subset of the values.</summary>
public sealed class SubsetOfTests
{
    private static readonly string[] Pool = ["admin", "editor", "viewer", "billing", "support", "auditor", "owner", "guest", "bot", "ops"];

    private static Generator<Roster> Generated() => SubsetFakes.Roster.Seed(6);

    private static Generator<Roster> Reflected() => new Generator<Roster>(new ReflectionModel<Roster>()).Seed(6);

    private static void ShouldBeSubsetInPoolOrder(IEnumerable<string> subset, int size)
    {
        var list = subset.ToList();
        list.Count.ShouldBe(size);
        list.Distinct().Count().ShouldBe(size);
        list.ShouldAllBe(v => Pool.Contains(v));
        list.Select(v => Array.IndexOf(Pool, v)).ShouldBe(list.Select(v => Array.IndexOf(Pool, v)).Order()); // in the order of the list they come from
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Every_object_gets_distinct_values_of_the_list_in_its_order_for_every_kind_of_collection(bool generated)
    {
        var accounts = (generated ? Generated() : Reflected())
            .WithOneOf(a => a.Roles, Pool, 3)
            .WithOneOf(a => a.Tags, Pool, 2)
            .WithOneOf(a => a.Numbers, new[] { 10, 20, 30, 40, 50 }, 4)
            .WithOneOf(a => a.Groups, Pool, 3)
            .WithOneOf(a => a.Permissions, Pool, 5)
            .WithOneOf(a => a.Favorites, Pool, 1)
            .Generate(300);

        foreach (var account in accounts)
        {
            ShouldBeSubsetInPoolOrder(account.Roles, 3);
            ShouldBeSubsetInPoolOrder(account.Tags, 2);
            account.Numbers.Count.ShouldBe(4);
            account.Numbers.ShouldAllBe(n => n % 10 == 0 && n >= 10 && n <= 50);
            account.Groups.ShouldBeOfType<HashSet<string>>().Count.ShouldBe(3);
            ShouldBeSubsetInPoolOrder(account.Permissions, 5);
            ShouldBeSubsetInPoolOrder(account.Favorites, 1);
        }
    }

    [Test]
    public void Every_value_is_equally_likely_to_be_in_a_subset()
    {
        var accounts = Reflected().WithOneOf(a => a.Roles, Pool, 3).Generate(20_000);

        foreach (var role in Pool)
            (accounts.Count(a => a.Roles.Contains(role)) / (double)accounts.Count).ShouldBe(0.3, tolerance: 0.015); // 3 of 10

        // And the subsets differ: 120 = 10 over 3 are possible.
        accounts.Select(a => string.Join(',', a.Roles)).Distinct().Count().ShouldBeGreaterThan(100);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_range_gives_every_size_in_it(bool generated)
    {
        var accounts = (generated ? Generated() : Reflected()).WithOneOf(a => a.Roles, Pool, (0, 3)).Generate(2_000);

        accounts.Select(a => a.Roles.Count).Distinct().Order().ShouldBe([0, 1, 2, 3]);
        foreach (var size in new[] { 0, 1, 2, 3 })
            (accounts.Count(a => a.Roles.Count == size) / 2000.0).ShouldBe(0.25, tolerance: 0.04);
        accounts.ShouldAllBe(a => a.Roles.Distinct().Count() == a.Roles.Count);
    }

    [Test]
    [Arguments(30)]
    [Arguments(200)]
    [Arguments(299)]
    public void Large_subsets_are_distinct_and_in_order_too(int size)
    {
        var big = Enumerable.Range(0, 300).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        var rosters = Reflected().WithOneOf(a => a.Roles, big, size).Generate(20);

        foreach (var roster in rosters)
        {
            roster.Roles.Count.ShouldBe(size);
            roster.Roles.Distinct().Count().ShouldBe(size);
            roster.Roles.Select(int.Parse).ShouldBe(roster.Roles.Select(int.Parse).Order());
        }

        rosters.Select(r => string.Join(',', r.Roles)).Distinct().Count().ShouldBeGreaterThan(size == 299 ? 1 : 15);
    }

    [Test]
    public void The_whole_list_and_nothing_are_possible_subsets()
    {
        var all = Reflected().WithOneOf(a => a.Roles, Pool, Pool.Length).Generate(5);
        var none = Reflected().WithOneOf(a => a.Roles, Pool, 0).Generate(5);

        all.ShouldAllBe(a => a.Roles.SequenceEqual(Pool));
        none.ShouldAllBe(a => a.Roles.Count == 0);
        Should.NotThrow(() => Reflected().WithOneOf(a => a.Roles, Array.Empty<string>(), 0).Generate());
    }

    [Test]
    public void The_two_models_make_the_same_subsets_and_a_seed_gives_the_same_ones()
    {
        Generator<Roster> Ruled(Generator<Roster> g) => g.WithOneOf(a => a.Roles, Pool, (1, 4)).WithOneOf(a => a.Tags, Pool, 2);
        string Text(Roster a) => $"{a.Name}|{string.Join(',', a.Roles)}|{string.Join(',', a.Tags)}";

        var generated = Ruled(Generated()).Generate(100).Select(Text).ToList();

        generated.ShouldBe(Ruled(Reflected()).Generate(100).Select(Text));
        generated.ShouldBe(Ruled(Generated()).Generate(100).Select(Text));
        Ruled(SubsetFakes.Roster.Seed(7)).Generate(100).Select(Text).ShouldNotBe(generated);
    }

    [Test]
    public void Item_n_is_the_same_however_it_is_generated()
    {
        Generator<Roster> Ruled() => Generated().WithOneOf(a => a.Roles, Pool, (1, 4));

        Ruled().GenerateParallel(100).Select(a => string.Join(',', a.Roles)).ShouldBe(Ruled().Generate(100).Select(a => string.Join(',', a.Roles)));
        var split = Ruled();
        split.Generate(40).Concat(split.Generate(60)).Select(a => string.Join(',', a.Roles)).ShouldBe(Ruled().Generate(100).Select(a => string.Join(',', a.Roles)));
    }

    [Test]
    public void It_works_for_nested_members_collection_elements_and_collections_filled_in_place()
    {
        var accounts = Reflected()
            .WithOneOf(a => a.Squad.Members, Pool, 2)
            .WithOneOf(a => a.Squads.Each().Members, Pool, (1, 2))
            .WithOneOf(a => a.Tracked, Pool, 3)
            .Generate(50);

        accounts.ShouldAllBe(a => a.Squad.Members.Count == 2 && a.Squads.Count > 0 && a.Tracked.Count == 3);
        accounts.SelectMany(a => a.Squads).All(t => t.Members.Count is 1 or 2).ShouldBeTrue();
        accounts.ShouldAllBe(a => a.Tracked.Distinct().Count() == 3);
    }

    [Test]
    public void Populate_keeps_a_collection_that_is_set()
    {
        var kept = new Roster { Roles = ["mine"] };

        var filled = Reflected().WithOneOf(a => a.Roles, Pool, 3).WithOneOf(a => a.Tags, Pool, 2).Populate(kept);

        filled.Roles.ShouldBe(["mine"]);
        filled.Tags.Length.ShouldBe(2);
    }

    [Test]
    public void A_later_rule_for_the_member_replaces_it()
    {
#pragma warning disable PHONEY008 // replacing the earlier rule is what this test is about
        Reflected().WithOneOf(a => a.Roles, Pool, 3).With(a => a.Roles, ["fixed"]).Generate().Roles.ShouldBe(["fixed"]);
#pragma warning restore PHONEY008
    }

    [Test]
    public void The_size_converts_from_a_number_and_from_a_pair()
    {
        SubsetSize count = 3;
        SubsetSize range = (1, 4);

        (count.Min, count.Max).ShouldBe((3, 3));
        (range.Min, range.Max).ShouldBe((1, 4));
    }

    [Test]
    public void Arguments_are_checked_when_the_rule_is_added()
    {
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(a => a.Roles, Pool, 11)).Message.ShouldContain("11 of the 10 values");
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(a => a.Roles, Pool, (1, 11))).Message.ShouldContain("11 of the 10 values");
        Should.Throw<ArgumentOutOfRangeException>(() => Reflected().WithOneOf(a => a.Roles, Pool, -1));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(a => a.Roles, Pool, (3, 1))).Message.ShouldContain("at most");
        Should.Throw<ArgumentNullException>(() => Reflected().WithOneOf(a => a.Roles, (IReadOnlyList<string>)null!, 1));
        Should.Throw<ArgumentException>(() => Reflected().WithOneOf(a => a.Frozen, Pool, 2)).Message.ShouldContain("ImmutableArray");
    }
}
