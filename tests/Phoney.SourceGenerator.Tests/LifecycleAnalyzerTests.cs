using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

/// <summary>
/// What <see cref="LifecycleAnalyzer"/> says and where; <see cref="AnalyzerParityTests"/> checks that it reports exactly the lifecycles and stamps the runtime refuses.
/// </summary>
public sealed class LifecycleAnalyzerTests
{
    private const string Types = """
        using System;
        using System.Collections.Generic;
        using Phoney;
        using Phoney.Generation;

        public enum Phase { Draft, Sent, Paid }

        public class Line
        {
            public Phase Status { get; set; }
            public DateTimeOffset? SentAt { get; set; }
        }

        public class Invoice
        {
            public Phase Status { get; set; }
            public DateTimeOffset? SentAt { get; set; }
            public DateTime PaidOn { get; set; }
            public string Note { get; set; } = "";
            public List<Line> Lines { get; set; } = new();
        }

        public static class Usage
        {
            private static LifecycleBuilder<Invoice, Phase> Shop(LifecycleBuilder<Invoice, Phase> l) => l.From(Phase.Draft);

        """;

    private static async Task<List<Diagnostic>> Diagnose(string body, CancellationToken cancellationToken) =>
        [.. await GeneratorHarness.Analyze(Types + body + "\n}", new LifecycleAnalyzer(), cancellationToken)];

    private static string TextOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    [Test]
    public async Task A_refused_builder_call_is_reported_at_the_call_with_the_fix(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Sent).To(Phase.Sent));
            """, cancellationToken);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("PHONEY013");
        diagnostic.GetMessage().ShouldBe("The transition from Draft to Sent is added twice. Add each transition once.");
        TextOf(diagnostic).ShouldBe("To");
        diagnostic.Location.GetLineSpan().StartLinePosition.Character.ShouldBeGreaterThan(100); // the second To, not the first
    }

    [Test]
    public async Task A_lifecycle_that_can_not_work_is_reported_at_the_lifecycle_call(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Sent).From(Phase.Paid).To(Phase.Sent));
            public static object B() => Fake.For<Invoice>().Member(i => i.Status, m => m.Lifecycle(l => l.From(Phase.Draft).To(Phase.Sent).EndsIn(Phase.Sent, 1).MaxTransitions(1).EndsIn(Phase.Draft, 1)));
            """, cancellationToken);

        diagnostics.Select(d => d.GetMessage()).ShouldBe(
        [
            "The lifecycle of Invoice.Status can't reach Paid from its start state Draft. Add a transition into it, or remove it.",
        ]);
        diagnostics.ShouldAllBe(d => TextOf(d) == "Lifecycle");
    }

    [Test]
    public async Task Stamps_are_reported_at_their_member(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Sent).Stamp(Phase.Sent, i => i.Note));
            public static object B() => Fake.For<Invoice>().Lifecycle(i => i.Lines.Each().Status, l => l.From(Phase.Draft).To(Phase.Sent).Stamp(Phase.Sent, i => i.SentAt));
            public static object C() => Fake.For<Invoice>().Lifecycle(i => i.Lines.Each().Status, l => l.From(Phase.Draft).To(Phase.Sent).Stamp(Phase.Sent, i => i.Lines.Each().SentAt));
            """, cancellationToken);

        diagnostics.ShouldAllBe(d => d.Id == "PHONEY014");
        diagnostics.Select(TextOf).ShouldBe(["i => i.Note", "i => i.SentAt"]);
        diagnostics[0].GetMessage().ShouldContain("a String: a stamp is a DateTimeOffset, DateTime or DateOnly");
        diagnostics[1].GetMessage().ShouldContain("not a member of the object that holds the status (Lines)");
    }

    [Test]
    public async Task A_stamp_some_objects_never_get_is_reported_when_the_chain_generates(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Paid)).Generate();
            public static object B() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Paid));
            public static object C() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Paid)).Ignore(i => i.PaidOn).Generate();
            public static object D(ObjectGenerator<Invoice> g) => g.Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Paid)).Generate();
            public static object E() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Paid)).With(i => i.Status, Phase.Draft).Generate();
            """, cancellationToken);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("PHONEY014");
        diagnostic.GetMessage().ShouldBe(
            "Invoice.PaidOn is the time an object entered Paid, but some objects never get there, and a DateTime can't be null. Make it nullable (DateTime?), or leave it out with .Ignore(x => x.PaidOn).");
        TextOf(diagnostic).ShouldBe("Lifecycle");
    }

    [Test]
    public async Task A_lifecycle_that_can_not_be_read_is_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Lifecycle(i => i.Status, Shop);
            public static object B() => Fake.For<Invoice>().Lifecycle(i => i.Status, l => Shop(l));
            public static object C(Phase p) => Fake.For<Invoice>().Lifecycle(i => i.Status, l => l.From(p).To(Phase.Sent).From(Phase.Sent).To(Phase.Paid)).Generate();
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public async Task History_without_a_lifecycle_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Seed(3).GenerateHistory(5);
            public static object B(ObjectGenerator<Invoice> g) => g.GenerateHistory();
            public static object C() => Fake.For<Invoice>().Lifecycle(i => i.Lines.Each().Status, l => l.From(Phase.Draft).To(Phase.Sent)).GenerateHistory();
            """, cancellationToken);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("PHONEY013");
        TextOf(diagnostic).ShouldBe("GenerateHistory");
        diagnostic.GetMessage().ShouldContain("has no lifecycle");
    }

    [Test]
    public async Task A_Stamp_that_could_never_apply_is_reported_at_the_stamp(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().Ignore(i => i.SentAt).Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Sent).Stamp(Phase.Sent, i => i.SentAt));
            public static object B() => Fake.For<Invoice>().Lifecycle(i => i.Note, l => l.From("a").To("b").Stamp("b", i => i.SentAt)).Lifecycle(i => i.Status, l => l.From(Phase.Draft).To(Phase.Sent).Stamp(Phase.Sent, i => i.SentAt));
            """, cancellationToken);

        diagnostics.Select(d => d.Id).ShouldBe(["PHONEY014", "PHONEY014"]);
        diagnostics.ShouldAllBe(d => TextOf(d) == "i => i.SentAt");
        diagnostics[0].GetMessage().ShouldBe(
            "Invoice.SentAt has a rule of its own, and the lifecycle of Invoice.Status stamps it. The rule wins, so the Stamp would do nothing. Remove the rule or the Stamp.");
        diagnostics[1].GetMessage().ShouldBe("Invoice.SentAt is stamped by the lifecycles of both Invoice.Note and Invoice.Status. Give each member one state.");
    }

    [Test]
    public async Task History_with_a_lifecycle_from_a_generator_for_a_nested_type_is_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Invoice>().WithGeneratorFor(Fake.For<Line>().Lifecycle(l => l.Status, b => b.From(Phase.Draft).To(Phase.Sent))).GenerateHistory();
            public static object B(ObjectGenerator<Line> lines) => Fake.For<Invoice>().WithGeneratorFor(lines).GenerateHistory();
            public static object C() => Fake.For<Invoice>().WithGeneratorFor(Fake.For<Line>().Seed(1)).GenerateHistory();
            """, cancellationToken);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        diagnostic.Location.SourceTree!.GetText(cancellationToken).Lines[line].ToString().ShouldContain("Seed(1)"); // C: the nested generator has none either
        diagnostic.GetMessage().ShouldContain("Fake.Generators");
    }
}
