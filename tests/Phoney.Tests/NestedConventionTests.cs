using Phoney.Generation;

namespace Phoney.Tests;

// Reflection-only types: no [FakeFor] reaches them.
public sealed class Warehouse
{
    public string Name { get; set; } = "";
    public Shelf Shelf { get; set; } = null!;
    public List<Shelf> Shelves { get; set; } = [];
}

public sealed class Shelf
{
    public string ShelfCode { get; set; } = "";
    public string Label { get; set; } = "";
    public Bin? Bin { get; set; }
}

public sealed record Bin(string ShelfCode, int Slot);

/// <summary>Custom conventions apply to nested objects as well as to the generated object itself.</summary>
public sealed class NestedConventionTests
{
    static NestedConventionTests() =>
        Fake.Conventions.Add<string>("ShelfCode", f => "SC-" + f.Random.Int(100, 999)); // a name no other test uses

    [Test]
    public void App_wide_conventions_apply_to_nested_objects_collections_and_records()
    {
        var warehouses = Fake.For<Warehouse>().Seed(1).Generate(5);

        warehouses.ShouldAllBe(w => w.Shelf.ShelfCode.StartsWith("SC-"));
        warehouses.SelectMany(w => w.Shelves).ShouldAllBe(s => s.ShelfCode.StartsWith("SC-"));
        warehouses.ShouldAllBe(w => w.Shelf.Bin!.ShelfCode.StartsWith("SC-")); // constructor parameter
    }

    [Test]
    public void Generator_conventions_apply_to_nested_objects()
    {
        var warehouse = Fake.For<Warehouse>().Seed(2).WithConvention<string>(n => n == "Label", _ => "L").Generate();

        warehouse.Shelf.Label.ShouldBe("L");
        warehouse.Shelves.ShouldAllBe(s => s.Label == "L");
    }

    [Test]
    public void Generator_conventions_come_before_app_wide_ones()
    {
        Fake.For<Warehouse>().Seed(3).WithConvention<string>(n => n == "ShelfCode", _ => "mine").Generate()
            .Shelf.ShelfCode.ShouldBe("mine");
    }

    [Test]
    public void Populate_applies_conventions_to_existing_nested_objects()
    {
        var warehouse = Fake.For<Warehouse>().Seed(4).Populate(new Warehouse { Shelf = new Shelf { Label = "kept" } });

        warehouse.Shelf.Label.ShouldBe("kept");
        warehouse.Shelf.ShelfCode.ShouldStartWith("SC-");
    }

    [Test]
    public void Arguments_apply_conventions_to_nested_objects()
    {
        var row = new ArgumentGenerator([new FakeArgument("warehouse", typeof(Warehouse))]).Seed(1).Generate();

        ((Warehouse)row[0]!).Shelf.ShelfCode.ShouldStartWith("SC-");
    }
}
