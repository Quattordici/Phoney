namespace Phoney.Tests.Docs;

// Inside the namespace, so the guides' model wins over the test models of Phoney.Tests.
using Phoney.Docs.Shop;

/// <summary>The Phoney side of docs/coming-from-bogus.md, run as tests.</summary>
public sealed class MigrationExamples
{
    [Test]
    public void A_bogus_faker_translated()
    {
        var created = new List<int>();

        // snippet: bogus-translated
        var orders = Fake.For<Order>().Seed(42)
            .With(o => o.Reference, f => f.Random.ReplaceSymbols("ORD-#####"))
            .AfterCreate((f, o) => created.Add(o.Id));
        // Id counts 1, 2, 3…, Status, CreatedAt and the lines come from conventions and annotations

        var some = orders.Generate(10);
        // end snippet

        some.ShouldAllBe(o => o.Reference.StartsWith("ORD-") && o.Reference.Length == 9);
        some.Select(o => o.Id).ShouldBe(Enumerable.Range(1, 10));
        created.ShouldBe(some.Select(o => o.Id));
        some.SelectMany(o => o.Lines).ShouldAllBe(l => l.Quantity >= 1 && l.Quantity <= 10);
    }

    [Test]
    public void Rule_sets_are_derived_generators()
    {
        // snippet: bogus-rule-sets
        var orders = Fake.For<Order>().Seed(1);
        var cancelled = orders.With(o => o.Status, OrderStatus.Cancelled);   // orders itself is unchanged
        var bulk = orders.CollectionSize(20, 50);
        // end snippet

        cancelled.Generate(5).ShouldAllBe(o => o.Status == OrderStatus.Cancelled);
        bulk.Generate().Lines.Count.ShouldBeGreaterThanOrEqualTo(20);
        orders.Generate().Lines.Count.ShouldBeLessThanOrEqualTo(3);
    }
}
