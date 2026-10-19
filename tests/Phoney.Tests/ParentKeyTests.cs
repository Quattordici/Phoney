using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Shopper
{
    public int Id { get; set; }
    public Guid Reference { get; set; }
    public List<ShopOrder> Orders { get; set; } = [];
    public ShopOrder? LatestOrder { get; set; }
}

public sealed class ShopOrder
{
    public int Id { get; set; }
    public int ShopperId { get; set; }
    public Guid? ShopperReference { get; set; }
    public ShopOrderLine[] Lines { get; set; } = [];
    public DateTime PlacedAt { get; set; }
}

public sealed class ShopOrderLine
{
    public int OrderId { get; set; }
    public int ShopperId { get; set; }
    public string Sku { get; set; } = "";
    public int AuditId { get; private set; }
}

public readonly record struct Waypoint(int X, int Y);

public sealed class Route
{
    public int Id { get; set; }
    public List<Waypoint> Points { get; set; } = [];
}

[FakeFor<Shopper>]
public static partial class ParentKeyFakes;

/// <summary><c>WithParentKey</c> copies a key of an object to the foreign keys of the objects below it, so a generated aggregate joins up.</summary>
public sealed class ParentKeyTests
{
    private static ObjectGenerator<Shopper> Keyed(ObjectGenerator<Shopper> generator) => generator
        .WithParentKey(s => s.Orders.Each().ShopperId, s => s.Id)
        .WithParentKey(s => s.Orders.Each().Lines.Each().OrderId, (ShopOrder o) => o.Id)
        .WithParentKey(s => s.Orders.Each().Lines.Each().ShopperId, s => s.Id);

    [Test]
    public void Without_it_foreign_keys_do_not_match()
    {
        var shoppers = Fake.For<Shopper>().Seed(1).Generate(50);

        shoppers.SelectMany(s => s.Orders.Select(o => o.ShopperId == s.Id)).ShouldContain(false);
    }

    [Test]
    public void Foreign_keys_match_the_nearest_object_of_the_parent_type()
    {
        var shoppers = Keyed(Fake.For<Shopper>()).Seed(1).Generate(50);

        shoppers.SelectMany(s => s.Orders).ShouldNotBeEmpty();
        foreach (var shopper in shoppers)
        {
            foreach (var order in shopper.Orders)
            {
                order.ShopperId.ShouldBe(shopper.Id);
                order.Lines.ShouldNotBeEmpty();
                foreach (var line in order.Lines)
                {
                    line.OrderId.ShouldBe(order.Id);
                    line.ShopperId.ShouldBe(shopper.Id);
                }
            }
        }
    }

    [Test]
    public void Nullable_foreign_keys_take_the_key() =>
        Fake.For<Shopper>().Seed(2).WithParentKey(s => s.Orders.Each().ShopperReference, s => s.Reference).Generate(20)
            .ShouldAllBe(s => s.Orders.All(o => o.ShopperReference == s.Reference));

    [Test]
    public void Nothing_else_changes_so_seeds_stay_the_same()
    {
        var plain = Fake.For<Shopper>().Seed(3).Generate(20);
        var keyed = Keyed(Fake.For<Shopper>()).Seed(3).Generate(20);

        keyed.Select(s => s.Id).ShouldBe(plain.Select(s => s.Id));
        keyed.SelectMany(s => s.Orders.Select(o => (o.Id, o.PlacedAt))).ShouldBe(plain.SelectMany(s => s.Orders.Select(o => (o.Id, o.PlacedAt))));
        keyed.SelectMany(s => s.Orders.SelectMany(o => o.Lines.Select(l => l.Sku))).ShouldBe(plain.SelectMany(s => s.Orders.SelectMany(o => o.Lines.Select(l => l.Sku))));
    }

    [Test]
    public void Keys_are_set_before_conditions_and_AfterCreate_see_the_object()
    {
        var seen = new List<bool>();
        Keyed(Fake.For<Shopper>()).Seed(4)
            .AfterCreate((_, s) => seen.Add(s.Orders.All(o => o.ShopperId == s.Id)))
            .Where(s => s.Orders.All(o => o.ShopperId == s.Id), maxAttempts: 1)
            .Generate(10);

        seen.ShouldAllBe(ok => ok);
    }

    [Test]
    public void Keys_follow_a_value_the_parent_key_gets_from_a_rule() =>
        Keyed(Fake.For<Shopper>()).Seed(5).Sequence(s => s.Id, i => (int)i + 100).Generate(5)
            .ShouldAllBe(s => s.Orders.All(o => o.ShopperId == s.Id && o.Lines.All(l => l.ShopperId == s.Id)));

    [Test]
    public void Populate_sets_the_keys_of_an_existing_graph()
    {
        var shopper = new Shopper { Id = 7, Orders = [new ShopOrder { Id = 3, Lines = [new ShopOrderLine()] }] };

        Keyed(Fake.For<Shopper>()).Seed(6).Populate(shopper);

        shopper.Id.ShouldBe(7);
        shopper.Orders[0].ShopperId.ShouldBe(7);
        shopper.Orders[0].Lines[0].OrderId.ShouldBe(3);
    }

    [Test]
    public void A_single_nested_object_works_like_a_collection_element() =>
        Fake.For<Shopper>().Seed(7).WithParentKey(s => s.LatestOrder!.ShopperId, s => s.Id).Generate(20)
            .Where(s => s.LatestOrder is not null).ShouldAllBe(s => s.LatestOrder!.ShopperId == s.Id);

    [Test]
    public void Reflection_and_generated_models_agree()
    {
        var reflection = Keyed(new ObjectGenerator<Shopper>(new ReflectionModel<Shopper>())).Seed(8).Generate(20);
        var generated = Keyed(ParentKeyFakes.Shopper).Seed(8).Generate(20);

        Flatten(generated).ShouldBe(Flatten(reflection));

        static IEnumerable<(int, int, int, int)> Flatten(List<Shopper> shoppers) =>
            shoppers.SelectMany(s => s.Orders.SelectMany(o => o.Lines.Select(l => (s.Id, o.ShopperId, l.OrderId, l.ShopperId))));
    }

    [Test]
    public void The_parent_must_be_an_object_on_the_path() =>
        Should.Throw<ArgumentException>(() => Fake.For<Shopper>().WithParentKey(s => s.Orders.Each().ShopperId, (ShopOrderLine l) => l.OrderId))
            .Message.ShouldContain("no object on the path to s.Orders.Each().ShopperId is a ShopOrderLine");

    [Test]
    public void The_foreign_key_must_be_in_a_nested_object() =>
        Should.Throw<ArgumentException>(() => Fake.For<Shopper>().WithParentKey(s => s.Id, s => s.Id))
            .Message.ShouldContain("must select a member of an object inside Shopper");

    [Test]
    public void Objects_on_the_path_must_be_reference_types() =>
        Should.Throw<ArgumentException>(() => Fake.For<Route>().WithParentKey(r => r.Points.Each().X, r => r.Id))
            .Message.ShouldContain("Waypoint is a value type");

    [Test]
    public void The_foreign_key_needs_a_public_setter() =>
        Should.Throw<ArgumentException>(() => Fake.For<Shopper>().WithParentKey(s => s.Orders.Each().Lines.Each().AuditId, (ShopOrder o) => o.Id))
            .Message.ShouldContain("AuditId has no public setter");
}
