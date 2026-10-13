using System.Linq.Expressions;

namespace Phoney.Tests;

/// <summary>
/// Rules that depend on the object run in the order they were added, as documented. They draw from the seeded faker, so
/// an order that varied between processes (hash order of the member names) would make seeded output irreproducible.
/// </summary>
public sealed class DependentRuleOrderTests
{
    public sealed class Wide
    {
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string C { get; set; } = "";
        public string D { get; set; } = "";
        public string E { get; set; } = "";
        public string F { get; set; } = "";
        public string G { get; set; } = "";
        public string H { get; set; } = "";
        public string I { get; set; } = "";
        public string J { get; set; } = "";
    }

    /// <summary>Adds a dependent rule for <paramref name="member"/> that records when it runs.</summary>
    private static Generator<Wide> Record(Generator<Wide> generator, Expression<Func<Wide, string>> member, List<string> ran)
    {
        var name = ((MemberExpression)member.Body).Member.Name;
        return generator.With(member, (_, _) =>
        {
            ran.Add(name);
            return name;
        });
    }

    [Test]
    public void Dependent_rules_run_in_the_order_they_were_added()
    {
        var ran = new List<string>();
        var generator = Fake.For<Wide>().Seed(1);
        foreach (var member in new Expression<Func<Wide, string>>[] { x => x.H, x => x.C, x => x.J, x => x.A, x => x.F, x => x.D, x => x.I, x => x.B, x => x.G, x => x.E })
            generator = Record(generator, member, ran);

        generator.Generate();

        ran.ShouldBe(["H", "C", "J", "A", "F", "D", "I", "B", "G", "E"]);
    }

    [Test]
    public void Setting_a_member_again_moves_its_rule_to_the_end()
    {
        var ran = new List<string>();
        var generator = Record(Record(Record(Fake.For<Wide>().Seed(1), x => x.A, ran), x => x.B, ran), x => x.A, ran);

        generator.Generate();

        ran.ShouldBe(["B", "A"]);
    }

    [Test]
    public void Populate_runs_dependent_rules_in_the_same_order()
    {
        var ran = new List<string>();
        var generator = Fake.For<Wide>().Seed(1);
        foreach (var member in new Expression<Func<Wide, string>>[] { x => x.G, x => x.B, x => x.I, x => x.A })
            generator = Record(generator, member, ran);

        generator.Populate(new Wide());

        ran.ShouldBe(["G", "B", "I", "A"]);
    }

    public sealed class ReadOnly
    {
        public string Name { get; set; } = "";
        public string First { get; } = "";
        public string Second { get; } = "";
        public string Third { get; } = "";
    }

    [Test]
    public void The_first_rule_added_that_cannot_be_applied_is_the_one_reported()
    {
        // The analyzer reports these rules at compile time (PHONEY007); the test checks what the runtime says without it.
#pragma warning disable PHONEY007
        var generator = Fake.For<ReadOnly>().With(x => x.Third, "c").With(x => x.First, "a").With(x => x.Second, "b");
#pragma warning restore PHONEY007

        var ex = Should.Throw<InvalidOperationException>(() => generator.Generate());

        ex.Message.ShouldContain("ReadOnly.Third");
    }
}
