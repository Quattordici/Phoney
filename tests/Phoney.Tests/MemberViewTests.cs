using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>
/// <c>Member(x =&gt; x.Email, m =&gt; m.OneOf(…).Unique())</c> is a view of one member: every method adds the rule its flat twin adds, so the
/// objects are the same, seed for seed, in both models; only what the member's type can take is offered.
/// </summary>
public sealed class MemberViewTests
{
    private static readonly string[] Pool = ["admin", "editor", "viewer", "billing", "support", "auditor", "owner", "guest"];
    private static readonly double[] PoolWeights = [8, 1, 1, 4, 2, 1, 0, 3];
    private static readonly int[] Numbers = [10, 20, 30, 40, 50];
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static string Describe<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items);

    private static ObjectGenerator<Booking> Bookings(bool generated) =>
        (generated ? ConstraintFakes.Booking : new ObjectGenerator<Booking>(new ReflectionModel<Booking>())).Seed(11).ReferenceDate(Reference);

    private static ObjectGenerator<Roster> Rosters(bool generated) =>
        (generated ? SubsetFakes.Roster : new ObjectGenerator<Roster>(new ReflectionModel<Roster>())).Seed(11).ReferenceDate(Reference);

    /// <summary>One row per view method (and overload): the flat chain and the view chain that must give the same bookings.</summary>
    public static IEnumerable<Func<(string Rule, Func<ObjectGenerator<Booking>, ObjectGenerator<Booking>> Flat, Func<ObjectGenerator<Booking>, ObjectGenerator<Booking>> View)>> BookingRules()
    {
        yield return () => ("Is", g => g.With(b => b.Notes, "fixed"), g => g.Member(b => b.Notes, m => m.Is("fixed")));
        yield return () => ("From faker", g => g.With(b => b.Notes, f => f.Lorem.Word()), g => g.Member(b => b.Notes, m => m.From(f => f.Lorem.Word())));
        yield return () => ("From object", g => g.With(b => b.Notes, (f, b) => b.Name + f.Random.Int(0, 9)), g => g.Member(b => b.Notes, m => m.From((f, b) => b.Name + f.Random.Int(0, 9))));
        yield return () => ("OneOf", g => g.WithOneOf(b => b.Notes, Pool), g => g.Member(b => b.Notes, m => m.OneOf(Pool)));
        yield return () => ("OneOf weighted pairs", g => g.WithOneOf(b => b.Size, ("S", 1), ("M", 5), ("L", 2)), g => g.Member(b => b.Size, m => m.OneOf(("S", 1), ("M", 5), ("L", 2))));
        yield return () => ("OneOf weights", g => g.WithOneOf(b => b.Notes, Pool, PoolWeights), g => g.Member(b => b.Notes, m => m.OneOf(Pool, PoolWeights)));
        yield return () => ("OneOf select", g => g.WithOneOf(b => b.Guests, Numbers, n => n / 10), g => g.Member(b => b.Guests, m => m.OneOf(Numbers, n => n / 10)));
        yield return () => ("OneOf select weight", g => g.WithOneOf(b => b.Guests, Numbers, n => n / 10, n => n), g => g.Member(b => b.Guests, m => m.OneOf(Numbers, n => n / 10, n => n)));
        yield return () => ("Normal", g => g.WithNormalDistribution(b => b.Rating, 3, 1, min: 0.5, max: 5, decimals: 1), g => g.Member(b => b.Rating, m => m.Normal(3, 1, min: 0.5, max: 5, decimals: 1)));
        yield return () => ("Normal nullable", g => g.WithNormalDistribution(b => b.Stars, 3, 1, 1, 5), g => g.Member(b => b.Stars, m => m.Normal(3, 1, 1, 5)));
        yield return () => ("Unique", g => g.Unique(b => b.Guests, 50), g => g.Member(b => b.Guests, m => m.Unique(50)));
        yield return () => ("Where", g => g.Where(b => b.Name, n => n.Length > 5, 500), g => g.Member(b => b.Name, m => m.Where(n => n.Length > 5, 500)));
        yield return () => ("Sequence", g => g.Sequence(b => b.Notes, i => $"N{i}"), g => g.Member(b => b.Notes, m => m.Sequence(i => $"N{i}")));
        yield return () => ("Cycle", g => g.Cycle(b => b.Size, ["S", "M"]), g => g.Member(b => b.Size, m => m.Cycle(["S", "M"])));
        yield return () => ("Invalid", g => g.Invalid(b => b.Guests), g => g.Member(b => b.Guests, m => m.Invalid()));
        yield return () => ("Boundary", g => g.Boundary(b => b.Name), g => g.Member(b => b.Name, m => m.Boundary()));
        yield return () => ("Ignore", g => g.Ignore(b => b.Email), g => g.Member(b => b.Email, m => m.Ignore()));
        yield return () => ("Several", g => g.WithOneOf(b => b.Notes, Pool).Unique(b => b.Notes, 100).Where(b => b.Notes, n => n != "guest"),
            g => g.Member(b => b.Notes, m => m.OneOf(Pool).Unique(100).Where(n => n != "guest")));
#pragma warning disable PHONEY008 // the replaced rule is the point of the row
        yield return () => ("Last slot rule wins", g => g.With(b => b.Notes, "second"), g => g.Member(b => b.Notes, m => m.OneOf(Pool).Is("second")));
#pragma warning restore PHONEY008
    }

    [Test]
    [MethodDataSource(nameof(BookingRules))]
    public void A_view_rule_gives_the_objects_its_flat_rule_gives_in_both_models(
        (string Rule, Func<ObjectGenerator<Booking>, ObjectGenerator<Booking>> Flat, Func<ObjectGenerator<Booking>, ObjectGenerator<Booking>> View) row)
    {
        foreach (var generated in new[] { true, false })
        {
            var flat = Describe(row.Flat(Bookings(generated)).Generate(6));
            Describe(row.View(Bookings(generated)).Generate(6)).ShouldBe(flat, row.Rule);
        }
    }

    /// <summary>Every collection shape <c>WithSubsetOf</c> accepts has a <c>SubsetOf</c>, with and without weights.</summary>
    public static IEnumerable<Func<(string Shape, Func<ObjectGenerator<Roster>, ObjectGenerator<Roster>> Flat, Func<ObjectGenerator<Roster>, ObjectGenerator<Roster>> View)>> SubsetRules()
    {
        yield return () => ("List", g => g.WithSubsetOf(r => r.Roles, Pool, 3), g => g.Member(r => r.Roles, m => m.SubsetOf(Pool, 3)));
        yield return () => ("array", g => g.WithSubsetOf(r => r.Tags, Pool, (1, 3)), g => g.Member(r => r.Tags, m => m.SubsetOf(Pool, (1, 3))));
        yield return () => ("HashSet", g => g.WithSubsetOf(r => r.Numbers, Numbers, 2), g => g.Member(r => r.Numbers, m => m.SubsetOf(Numbers, 2)));
        yield return () => ("ISet", g => g.WithSubsetOf(r => r.Groups, Pool, 2), g => g.Member(r => r.Groups, m => m.SubsetOf(Pool, 2)));
        yield return () => ("IReadOnlyList", g => g.WithSubsetOf(r => r.Permissions, Pool, 4), g => g.Member(r => r.Permissions, m => m.SubsetOf(Pool, 4)));
        yield return () => ("IEnumerable", g => g.WithSubsetOf(r => r.Favorites, Pool, 1), g => g.Member(r => r.Favorites, m => m.SubsetOf(Pool, 1)));
        yield return () => ("List weighted", g => g.WithSubsetOf(r => r.Roles, Pool, PoolWeights, 3), g => g.Member(r => r.Roles, m => m.SubsetOf(Pool, PoolWeights, 3)));
        yield return () => ("array weighted", g => g.WithSubsetOf(r => r.Tags, Pool, PoolWeights, 2), g => g.Member(r => r.Tags, m => m.SubsetOf(Pool, PoolWeights, 2)));
        yield return () => ("HashSet weighted", g => g.WithSubsetOf(r => r.Numbers, Numbers, [1, 2, 3, 4, 5], 2), g => g.Member(r => r.Numbers, m => m.SubsetOf(Numbers, [1, 2, 3, 4, 5], 2)));
        yield return () => ("ISet weighted", g => g.WithSubsetOf(r => r.Groups, Pool, PoolWeights, 2), g => g.Member(r => r.Groups, m => m.SubsetOf(Pool, PoolWeights, 2)));
        yield return () => ("nested path", g => g.WithSubsetOf(r => r.Squad.Members, Pool, 2), g => g.Member(r => r.Squad.Members, m => m.SubsetOf(Pool, 2)));
        yield return () => ("each element", g => g.WithSubsetOf(r => r.Squads.Each().Members, Pool, 2), g => g.Member(r => r.Squads.Each().Members, m => m.SubsetOf(Pool, 2)));
        yield return () => ("nested generator", g => g.With(r => r.Squad, new ObjectGenerator<Squad>(new ReflectionModel<Squad>()).WithSubsetOf(s => s.Members, Pool, 1)),
            g => g.Member(r => r.Squad, m => m.From(new ObjectGenerator<Squad>(new ReflectionModel<Squad>()).WithSubsetOf(s => s.Members, Pool, 1))));
    }

    [Test]
    [MethodDataSource(nameof(SubsetRules))]
    public void Subsets_and_paths_in_a_view_give_the_objects_of_the_flat_rule_in_both_models(
        (string Shape, Func<ObjectGenerator<Roster>, ObjectGenerator<Roster>> Flat, Func<ObjectGenerator<Roster>, ObjectGenerator<Roster>> View) row)
    {
        foreach (var generated in new[] { true, false })
        {
            var flat = Describe(row.Flat(Rosters(generated)).Generate(6));
            Describe(row.View(Rosters(generated)).Generate(6)).ShouldBe(flat, row.Shape);
        }
    }

    [Test]
    public void Views_and_flat_rules_mix_in_chain_order()
    {
#pragma warning disable PHONEY008 // replacing a rule is what is tested
        var flat = ConstraintFakes.Booking.Seed(2).With(b => b.Notes, "a").WithOneOf(b => b.Notes, Pool).Ignore(b => b.Email).Generate(5);
        var mixed = ConstraintFakes.Booking.Seed(2).Member(b => b.Notes, m => m.Is("a")).WithOneOf(b => b.Notes, Pool).Member(b => b.Email, m => m.Ignore()).Generate(5);
        var replaced = ConstraintFakes.Booking.Seed(2).WithOneOf(b => b.Notes, Pool).Member(b => b.Notes, m => m.Is("a")).Generate(5);

        Describe(mixed).ShouldBe(Describe(flat));
#pragma warning restore PHONEY008
        replaced.ShouldAllBe(b => b.Notes == "a");
    }

    [Test]
    public void A_view_with_no_rules_changes_nothing()
    {
        Describe(ConstraintFakes.Booking.Seed(4).Member(b => b.Notes, m => m).Generate(3)).ShouldBe(Describe(ConstraintFakes.Booking.Seed(4).Generate(3)));
    }

    [Test]
    public void The_view_refuses_what_the_flat_rule_refuses_with_the_same_message()
    {
#pragma warning disable PHONEY007, PHONEY011 // the rules are refused on purpose
        var flat = Should.Throw<ArgumentException>(() => ConstraintFakes.Booking.Sequence(b => b.Items.Each().Length, i => (int)i)).Message;
        Should.Throw<ArgumentException>(() => ConstraintFakes.Booking.Member(b => b.Items.Each().Length, m => m.Sequence(i => (int)i))).Message.ShouldBe(flat);
        Should.Throw<ArgumentException>(() => ConstraintFakes.Booking.Member(b => b.Notes, m => m.OneOf(Array.Empty<string>()))).ParamName.ShouldBe("values");
        Should.Throw<ArgumentOutOfRangeException>(() => ConstraintFakes.Booking.Member(b => b.Notes, m => m.Unique(0)));
#pragma warning restore PHONEY007, PHONEY011
    }

    [Test]
    public void Arguments_are_checked()
    {
        Should.Throw<ArgumentNullException>(() => ConstraintFakes.Booking.Member<string>(null!, m => m)).ParamName.ShouldBe("member");
        Should.Throw<ArgumentNullException>(() => ConstraintFakes.Booking.Member(b => b.Notes, null!)).ParamName.ShouldBe("rules");
        Should.Throw<InvalidOperationException>(() => default(MemberRules<Booking, string>).Is("x")).Message.ShouldContain("Member(");
    }

    [Test]
    public void The_view_must_return_the_rules_it_was_given()
    {
        MemberRules<Booking, string> other = default;
        _ = ConstraintFakes.Booking.Member(b => b.Name, m => other = m.Is("kept"));

        Should.Throw<InvalidOperationException>(() => ConstraintFakes.Booking.Member(b => b.Notes, _ => other))
            .Message.ShouldContain("return the rules the lambda was given");
        Should.Throw<InvalidOperationException>(() => ConstraintFakes.Booking.Member(b => b.Notes, _ => default))
            .Message.ShouldContain("return the rules the lambda was given");
    }
}
