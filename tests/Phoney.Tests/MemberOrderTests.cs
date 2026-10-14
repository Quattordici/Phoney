using Phoney.Generation;

namespace Phoney.Tests;

// Fields and properties are mixed on purpose: the models must list and draw them in the same order.
public sealed class Mixed
{
    public string A { get; set; } = "";
    public string B = "";
    public string C { get; set; } = "";
    public int D;
    public int E { get; set; }
    public List<string> Items { get; } = [];
    public readonly List<int> Numbers = [];
}

public class MixedBase
{
    public string P { get; set; } = "";
    public string Q = "";
}

public sealed class MixedDerived : MixedBase
{
    public string R { get; set; } = "";
    public string S = "";
}

[FakeFor<Mixed>, FakeFor<MixedDerived>]
public static partial class MixedFakes;

/// <summary>The source-generated and the reflection model list members in the same order, so a seed gives the same data.</summary>
public sealed class MemberOrderTests
{
    [Test]
    public void Both_models_list_fields_before_properties_and_base_class_members_first()
    {
        string[] Names<T>(IReadOnlyList<FakeMember> members) => [.. members.Select(m => m.Name)];

        var generated = Names<Mixed>(ModelRegistry.For<Mixed>().Members);
        var reflected = Names<Mixed>(new ReflectionModel<Mixed>().Members);

        reflected.ShouldBe(["B", "D", "A", "C", "E", "Numbers", "Items"]); // the order reflection reports: fields, then properties; read-only collections last
        generated.ShouldBe(reflected);
        Names<MixedDerived>(ModelRegistry.For<MixedDerived>().Members).ShouldBe(Names<MixedDerived>(new ReflectionModel<MixedDerived>().Members));
        Names<MixedDerived>(new ReflectionModel<MixedDerived>().Members).ShouldBe(["Q", "P", "S", "R"]);
    }

    [Test]
    public void Both_models_produce_the_same_data_for_a_seed()
    {
        string Describe(Mixed m) => $"{m.A}|{m.B}|{m.C}|{m.D}|{m.E}|{string.Join(",", m.Items)}|{string.Join(",", m.Numbers)}";
        string DescribeDerived(MixedDerived m) => $"{m.P}|{m.Q}|{m.R}|{m.S}";

        var generated = MixedFakes.Mixed.Seed(5).Generate(20);
        var reflected = new ObjectGenerator<Mixed>(new ReflectionModel<Mixed>()).Seed(5).Generate(20);
        var generatedDerived = MixedFakes.MixedDerived.Seed(5).Generate(20);
        var reflectedDerived = new ObjectGenerator<MixedDerived>(new ReflectionModel<MixedDerived>()).Seed(5).Generate(20);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
        reflectedDerived.Select(DescribeDerived).ShouldBe(generatedDerived.Select(DescribeDerived));
    }

    [Test]
    public void Both_models_populate_the_same_way()
    {
        var generated = MixedFakes.Mixed.Seed(5).Populate(new Mixed());
        var reflected = new ObjectGenerator<Mixed>(new ReflectionModel<Mixed>()).Seed(5).Populate(new Mixed());

        $"{reflected.A}|{reflected.B}|{reflected.C}|{reflected.D}|{reflected.E}".ShouldBe($"{generated.A}|{generated.B}|{generated.C}|{generated.D}|{generated.E}");
    }
}
