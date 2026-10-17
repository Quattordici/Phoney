using Phoney.Generation;

namespace Phoney.Tests;

public interface IBadge
{
    string Label { get; }
}

public sealed class Folder
{
    public string Name { get; set; } = "";
    public List<Folder> Children { get; set; } = [];
    public Folder[] Links { get; set; } = [];
    public HashSet<Folder> Related { get; set; } = [];
    public Dictionary<string, Folder> ByName { get; set; } = [];
    public List<IBadge> Badges { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

public sealed class GeneratedFolder
{
    public string Name { get; set; } = "";
    public List<GeneratedFolder> Children { get; set; } = [];
    public GeneratedFolder[] Links { get; set; } = [];
    public HashSet<GeneratedFolder> Related { get; set; } = [];
    public Dictionary<string, GeneratedFolder> ByName { get; set; } = [];
    public List<IBadge> Badges { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

public sealed class Cabinet
{
    public List<Dictionary<IBadge, int>> Lookups { get; set; } = [];
    public List<(string Name, Cabinet Owner)> Pairs { get; set; } = [];
}

[FakeFor<GeneratedFolder>]
public static partial class FolderFakes;

/// <summary>Elements that can't be created (a cycle, the depth limit, an interface without subtypes) are left out instead of added as <see langword="null"/>.</summary>
public sealed class CollectionCutoffTests
{
    [Test]
    public void Elements_cut_off_by_a_cycle_leave_the_collection_empty()
    {
        foreach (var folder in Fake.For<Folder>().Seed(1).Generate(20))
        {
            folder.Children.ShouldBeEmpty();
            folder.Links.ShouldBeEmpty();
            folder.Related.ShouldBeEmpty();
            folder.ByName.ShouldBeEmpty();
            folder.Badges.ShouldBeEmpty();
            folder.Tags.ShouldNotBeEmpty();
        }
    }

    [Test]
    public void Generated_models_leave_cut_off_elements_out_too()
    {
        foreach (var folder in FolderFakes.GeneratedFolder.Seed(1).Generate(20))
        {
            folder.Children.ShouldBeEmpty();
            folder.Links.ShouldBeEmpty();
            folder.Related.ShouldBeEmpty();
            folder.ByName.ShouldBeEmpty();
            folder.Badges.ShouldBeEmpty();
            folder.Tags.ShouldNotBeEmpty();
        }
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        static string Describe(GeneratedFolder f) =>
            $"{f.Name}|{f.Children.Count}|{f.Links.Length}|{f.Related.Count}|{f.ByName.Count}|{f.Badges.Count}|{string.Join(",", f.Tags)}";

        var generated = FolderFakes.GeneratedFolder.Seed(3).Generate(20);
        var reflected = new ObjectGenerator<GeneratedFolder>(new ReflectionModel<GeneratedFolder>()).Seed(3).Generate(20);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
    }

    [Test]
    public void Elements_past_the_depth_limit_leave_the_collection_empty()
    {
        var customers = Fake.For<Customer>().Seed(2).MaxDepth(1).Generate(10);

        customers.ShouldAllBe(c => c.Orders.Count > 0);
        customers.SelectMany(c => c.Orders).ShouldAllBe(o => o.Lines.Length == 0);
    }

    [Test]
    public void Cutting_elements_off_draws_the_same_randomness_for_the_other_members()
    {
        // The count is still drawn, so members after the collections keep the values they had when cut-off elements were added as null.
        var folders = Fake.For<Folder>().Seed(5).Generate(3);

        folders.Select(f => $"{f.Name}|{string.Join(",", f.Tags)}").ShouldBe(
        [
            "Augustus Willms|quaerat,collum,tendo",
            "Miss Ferne Durgan|volva,causa",
            "Ms. Kianna Kling|terga,provident,demonstro",
        ]);
    }

    [Test]
    public void A_collection_whose_own_parts_are_cut_off_is_still_an_element()
    {
        // The dictionaries get no keys (IBadge has no subtype) and the tuples no owner (a cycle), but each is a value of its own, so the lists keep them.
        var cabinet = Fake.For<Cabinet>().Seed(1).Generate();

        cabinet.Lookups.ShouldNotBeEmpty();
        cabinet.Lookups.ShouldAllBe(d => d.Count == 0);
        cabinet.Pairs.ShouldNotBeEmpty();
        cabinet.Pairs.ShouldAllBe(p => p.Owner == null && p.Name.Length > 0);
    }
}
