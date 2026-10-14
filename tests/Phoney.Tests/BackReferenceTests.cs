using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Buyer
{
    public string Name { get; set; } = "";
    public List<Purchase> Purchases { get; set; } = [];
}

public sealed class Purchase
{
    public string Reference { get; set; } = "";
    public Buyer Buyer { get; set; } = null!;
    public List<PurchaseLine> Lines { get; set; } = [];
    public Dictionary<string, PurchaseLine> ByCode { get; set; } = [];
}

public sealed class PurchaseLine
{
    public string Sku { get; set; } = "";
    public Purchase Purchase { get; set; } = null!;
    public Buyer? Owner { get; set; }
    public Address? Delivery { get; set; }
}

[FakeFor<Buyer>]
public static partial class GraphFakes;

/// <summary><c>WithBackReferences</c> points members that would close a cycle at the object that contains them.</summary>
public sealed class BackReferenceTests
{
    private static ObjectGenerator<Buyer> Make() => Fake.For<Buyer>().Seed(4);

    [Test]
    public void Without_it_a_cycle_stops_with_null()
    {
        var buyer = Make().Generate();

        buyer.Purchases.ShouldNotBeEmpty();
        buyer.Purchases.ShouldAllBe(p => p.Buyer == null);
        buyer.Purchases.SelectMany(p => p.Lines).ShouldAllBe(l => l.Purchase == null);
    }

    [Test]
    public void Members_point_at_the_nearest_object_that_contains_them()
    {
        var buyers = Make().WithBackReferences().Generate(10);

        foreach (var buyer in buyers)
        {
            buyer.Purchases.ShouldNotBeEmpty();
            foreach (var purchase in buyer.Purchases)
            {
                purchase.Buyer.ShouldBeSameAs(buyer);
                purchase.Lines.ShouldNotBeEmpty();
                foreach (var line in purchase.Lines.Concat(purchase.ByCode.Values))
                {
                    line.Purchase.ShouldBeSameAs(purchase); // the nearest Purchase, not an outer one
                    line.Owner.ShouldBeSameAs(buyer);        // a member that wants a Buyer gets the Buyer further up
                }
            }
        }
    }

    [Test]
    public void Only_members_that_can_hold_an_ancestor_are_filled()
    {
        var buyers = Make().WithBackReferences().Generate(5);

        buyers.SelectMany(b => b.Purchases).SelectMany(p => p.Lines).ShouldAllBe(l => l.Delivery != null); // an Address is generated as usual
        Make().NullProbability(1).WithBackReferences().Generate(3)
            .SelectMany(b => b.Purchases).SelectMany(p => p.Lines).ShouldAllBe(l => l.Delivery == null); // nothing above is an Address
    }

    [Test]
    public void Values_that_are_set_are_kept()
    {
        var other = new Buyer { Name = "someone else" };

        var buyer = Make().WithBackReferences().With(b => b.Purchases.Each().Buyer, other).Generate();

        buyer.Purchases.ShouldAllBe(p => ReferenceEquals(p.Buyer, other));
        buyer.Purchases.SelectMany(p => p.Lines).ShouldAllBe(l => l.Owner == buyer);
    }

    [Test]
    public void Nothing_else_changes_so_the_data_is_the_data_of_the_same_seed()
    {
        string Describe(Buyer b) => $"{b.Name}|{string.Join(",", b.Purchases.Select(p => p.Reference + ":" + string.Join("/", p.Lines.Select(l => l.Sku))))}";

        Make().WithBackReferences().Generate(10).Select(Describe).ShouldBe(Make().Generate(10).Select(Describe));
    }

    [Test]
    public void A_condition_sees_the_finished_graph_and_so_does_AfterCreate()
    {
        var seen = new List<bool>();

        var buyers = Make().WithBackReferences()
            .AfterCreate((_, b) => seen.Add(b.Purchases.All(p => ReferenceEquals(p.Buyer, b))))
            .Where(b => b.Purchases.SelectMany(p => p.Lines).All(l => l.Purchase != null))
            .Generate(5);

        buyers.Count.ShouldBe(5);
        seen.ShouldAllBe(x => x);
    }

    [Test]
    public void Populate_fills_the_missing_references_of_an_existing_graph()
    {
        var buyer = new Buyer { Name = "kept", Purchases = [new Purchase { Reference = "P1", Lines = [new PurchaseLine { Sku = "S1" }] }] };

        var populated = Make().WithBackReferences().Populate(buyer);

        populated.ShouldBeSameAs(buyer);
        buyer.Purchases[0].Buyer.ShouldBeSameAs(buyer);
        buyer.Purchases[0].Lines[0].Purchase.ShouldBeSameAs(buyer.Purchases[0]);
        buyer.Purchases[0].Lines[0].Owner.ShouldBeSameAs(buyer);
    }

    [Test]
    public void Generated_and_reflection_models_wire_the_same_graph()
    {
        string Describe(Buyer b) =>
            $"{b.Name}|{string.Join(",", b.Purchases.Select(p => $"{p.Reference}:{ReferenceEquals(p.Buyer, b)}:{string.Join("/", p.Lines.Select(l => $"{l.Sku}{ReferenceEquals(l.Purchase, p)}{ReferenceEquals(l.Owner, b)}"))}"))}";

        var generated = GraphFakes.Buyer.Seed(9).WithBackReferences().Generate(15);
        var reflected = new ObjectGenerator<Buyer>(new ReflectionModel<Buyer>()).Seed(9).WithBackReferences().Generate(15);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        generated.SelectMany(b => b.Purchases).ShouldAllBe(p => p.Buyer != null);
    }

    [Test]
    public void It_can_be_switched_off_again_and_generators_stay_immutable()
    {
        var on = Make().WithBackReferences();

        on.WithBackReferences(false).Generate().Purchases.ShouldAllBe(p => p.Buyer == null);
        on.Generate().Purchases.ShouldAllBe(p => p.Buyer != null);
    }
}
