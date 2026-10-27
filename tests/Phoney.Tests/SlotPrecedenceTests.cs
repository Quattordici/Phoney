using Phoney.Generation;

namespace Phoney.Tests;

public sealed record PrecedenceLeaf(string Label = "declared");

public sealed record PrecedenceRoot(string Label = "declared")
{
    public PrecedenceLeaf Leaf { get; set; } = null!;

    public List<PrecedenceLeaf> Leaves { get; set; } = [];
}

[FakeFor<PrecedenceRoot>]
[FakeFor<PrecedenceLeaf>]
internal static partial class PrecedenceFakes;

/// <summary>
/// Which source gives a member its value when several apply: one table of cases for the root object, a nested object and the elements of a collection, run through the
/// reflection model and the generated one. It describes today's behaviour, so a change to how rules and conventions are bound has to keep every row.
/// </summary>
public sealed class SlotPrecedenceTests
{
    /// <summary>The values the sources in the table produce; a member that none of them decided has some other value.</summary>
    private const string Other = "*";

    private static readonly string[] Sentinels = ["rule", "feed", "declared", "conv", "type", "path", "tg", "seq0", "<null>"];

    private sealed record Case(string Name, Func<ObjectGenerator<PrecedenceRoot>, ObjectGenerator<PrecedenceRoot>> Configure, Func<PrecedenceRoot, string?> Read, string Expected);

    private static Case Root(string name, Func<ObjectGenerator<PrecedenceRoot>, ObjectGenerator<PrecedenceRoot>> configure, string expected) =>
        new("root: " + name, configure, r => r.Label, expected);

    private static Case Nested(string name, Func<ObjectGenerator<PrecedenceRoot>, ObjectGenerator<PrecedenceRoot>> configure, string expected) =>
        new("nested: " + name, configure, r => r.Leaf.Label, expected);

    private static Case Element(string name, Func<ObjectGenerator<PrecedenceRoot>, ObjectGenerator<PrecedenceRoot>> configure, string expected) =>
        new("element: " + name, configure, r => r.Leaves[0].Label, expected);

    private static ObjectGenerator<PrecedenceRoot> Convention(ObjectGenerator<PrecedenceRoot> g) => g.WithConvention<string>(n => n == "Label", _ => "conv");

    private static ObjectGenerator<PrecedenceRoot> EveryString(ObjectGenerator<PrecedenceRoot> g) => g.WithEveryMemberOfType<string>(_ => "type");

    private static ObjectGenerator<PrecedenceRoot> Feed(ObjectGenerator<PrecedenceRoot> g) => g.WithFeed(DataFeed.FromCsv("Label\nfeed"));

    private static readonly Case[] Cases =
    [
        Root("nothing decides it", g => g, Other),
        Root("a declared default", g => g.WithParameterDefaults(), "declared"),
        Root("a convention", Convention, "conv"),
        Root("every member of a type", EveryString, "type"),
        Root("the first custom convention added wins", g => EveryString(Convention(g)), "conv"),
        Root("the first custom convention added wins, the other way round", g => Convention(EveryString(g)), "type"),
        Root("a declared default beats a convention", g => Convention(g.WithParameterDefaults()), "declared"),
        Root("the feed beats a declared default and a convention", g => Feed(Convention(g.WithParameterDefaults())), "feed"),
        Root("the feed beats every member of a type", g => Feed(EveryString(g)), "feed"),
        Root("a rule beats the feed", g => Feed(g.With(x => x.Label, "rule")), "rule"),
        Root("a rule beats the feed, a default and a convention", g => Feed(Convention(g.WithParameterDefaults().With(x => x.Label, "rule"))), "rule"),
        Root("a rule beats a declared default", g => g.WithParameterDefaults().With(x => x.Label, "rule"), "rule"),
        Root("Sequence is a rule", g => Feed(g.Sequence(x => x.Label, i => "seq" + i)), "seq0"),
        Root("Unique decides the member, so it gets no declared default", g => g.WithParameterDefaults().Unique(x => x.Label), Other),
        Root("Unique does not stop a convention", g => Convention(g.WithParameterDefaults().Unique(x => x.Label)), "conv"),
        Root("a condition on the member gets it no declared default", g => g.WithParameterDefaults().Where(x => x.Label, v => true), Other),
        Root("a condition on the member does not stop a convention", g => Convention(g.WithParameterDefaults().Where(x => x.Label, v => true)), "conv"),
        Root("a condition filters what a rule gives", g => g.With(x => x.Label, "rule").Where(x => x.Label, v => v == "rule"), "rule"),
        Root("an ignored member is left alone", g => Convention(g.WithParameterDefaults().Ignore(x => x.Label)), "<null>"),

        Nested("nothing decides it", g => g, Other),
        Nested("a declared default", g => g.WithParameterDefaults(), "declared"),
        Nested("a convention", Convention, "conv"),
        Nested("a declared default beats a convention", g => Convention(g.WithParameterDefaults()), "declared"),
        Nested("every member of a type reaches nested objects", EveryString, "type"),
        Nested("the feed is for the root object only", Feed, Other),
        Nested("a path rule", g => g.With(x => x.Leaf.Label, "path"), "path"),
        Nested("a path rule beats a declared default and a convention", g => Convention(g.WithParameterDefaults().With(x => x.Leaf.Label, "path")), "path"),
        Nested("a rule for the member replaces the object, so path rules inside it do not apply", g => g.With(x => x.Leaf, new PrecedenceLeaf("rule")).With(x => x.Leaf.Label, "path"), "rule"),
        Nested("a type generator brings its own rules", g => Convention(g.WithGeneratorFor(Fake.For<PrecedenceLeaf>().With(l => l.Label, "tg")).With(x => x.Leaf.Label, "path")), "tg"),

        Element("a path rule through Each()", g => g.With(x => x.Leaves.Each().Label, "path"), "path"),
        Element("a path rule beats a convention", g => Convention(g.With(x => x.Leaves.Each().Label, "path")), "path"),
        Element("a declared default", g => g.WithParameterDefaults(), "declared"),
        Element("a convention", Convention, "conv"),
    ];

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_source_that_decides_a_member_is_the_one_the_table_says(bool generated)
    {
        var mismatches = new List<string>();
        foreach (var row in Cases)
        {
            var start = (generated ? PrecedenceFakes.PrecedenceRoot : new ObjectGenerator<PrecedenceRoot>(new ReflectionModel<PrecedenceRoot>())).Seed(1).CollectionSize(1, 1);
            var actual = row.Read(row.Configure(start).Generate());
            var matches = row.Expected == Other ? actual is not null && !Sentinels.Contains(actual) : actual == (row.Expected == "<null>" ? null : row.Expected);
            if (!matches)
                mismatches.Add($"{row.Name}: expected {row.Expected}, got {actual ?? "<null>"}");
        }

        mismatches.ShouldBeEmpty();
    }
}
