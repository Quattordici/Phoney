using System.ComponentModel.DataAnnotations;

namespace Phoney.Tests;

public sealed class StageRoot
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    [Compare(nameof(Code))] public string CodeAgain { get; set; } = "";
    public List<StageChild> Children { get; set; } = [];
}

public sealed class StageChild
{
    public int ParentId { get; set; }
    public StageRoot Parent { get; set; } = null!;
}

/// <summary>
/// What runs on an object once its members are produced, created or populated alike: dependent rules, then <c>[Compare]</c>, then back-references and parent keys,
/// then the AfterCreate actions; and the attempts a condition or a unique identity gets, with the same errors for both.
/// </summary>
public sealed class ObjectPipelineTests
{
    private static ObjectGenerator<StageRoot> Generator(List<string> seen) => Fake.For<StageRoot>()
        .Seed(3)
        .With(x => x.Id, 41)
        .With(x => x.Code, (_, x) => "dependent " + x.Id)
        .WithBackReferences()
        .WithParentKey(x => x.Children.Each().ParentId, x => x.Id)
        .AfterCreate((_, x) =>
        {
            seen.Add(x.CodeAgain);
            seen.Add(x.Children.TrueForAll(c => ReferenceEquals(c.Parent, x)) ? "back-references" : "no back-references");
            seen.Add(x.Children.TrueForAll(c => c.ParentId == x.Id) ? "parent keys" : "no parent keys");
        });

    private static readonly string[] Expected = ["dependent 41", "back-references", "parent keys"];

    [Test]
    public void A_created_object_runs_dependent_rules_then_compare_then_the_graph_then_AfterCreate()
    {
        var seen = new List<string>();

        var root = Generator(seen).Generate();

        root.Children.ShouldNotBeEmpty();
        seen.ShouldBe(Expected);
    }

    [Test]
    public void A_populated_object_runs_the_same_steps_in_the_same_order()
    {
        var seen = new List<string>();

        Generator(seen).Populate(new StageRoot(), PopulateMode.All);

        seen.ShouldBe(Expected);
    }

    [Test]
    public void An_object_from_CreateWith_keeps_its_compared_members()
    {
        var root = Fake.For<StageRoot>().CreateWith(_ => new StageRoot { Code = "a", CodeAgain = "b" }).Generate();

        root.CodeAgain.ShouldBe("b");
    }

    [Test]
    public void A_condition_that_never_holds_fails_the_same_way_for_create_and_populate()
    {
        var generator = Fake.For<StageRoot>().Seed(3).Where(_ => false, maxAttempts: 4);

        var created = Should.Throw<InvalidOperationException>(() => generator.Generate());
        var populated = Should.Throw<InvalidOperationException>(() => generator.Populate(new StageRoot(), PopulateMode.All));

        populated.Message.ShouldBe(created.Message);
    }

    [Test]
    public void A_rejected_object_is_made_again_until_one_is_accepted_for_create_and_populate()
    {
        var calls = 0;
        var generator = Fake.For<StageRoot>().Seed(3).Where(_ => ++calls % 3 == 0);

        generator.Generate();
        calls.ShouldBe(3);

        calls = 0;
        generator.Populate(new StageRoot(), PopulateMode.All);
        calls.ShouldBe(3);
    }
}
