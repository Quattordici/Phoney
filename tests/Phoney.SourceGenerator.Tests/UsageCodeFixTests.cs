using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Phoney.CodeFixes;

namespace Phoney.SourceGenerator.Tests;

/// <summary>The code fixes for PHONEY004, 005, 006, 008 and 009.</summary>
public sealed class UsageCodeFixTests
{
    private const string Types = """
        using System;
        using Phoney;
        using Phoney.Generation;

        public class Gadget
        {
            public string Email { get; set; } = "";
            public int Alpha { get; set; }
            public int @class { get; set; }
        }

        public struct Point { public int X; public int Y; }

        """;

    private static (Project Project, string Name) Project(string usage) =>
        (CodeFixHarness.CreateProject(("Usage.cs", Types + usage)), "Usage.cs");

    private static async Task<(Project Project, ImmutableArray<Diagnostic> Diagnostics)> Diagnose(string usage, Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer analyzer, string id, CancellationToken cancellationToken)
    {
        var (project, _) = Project(usage);
        return (project, await CodeFixHarness.Diagnose(project, analyzer, id, cancellationToken));
    }

    private static async Task<CodeFixHarness.Fix> SingleFix(Project project, Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider provider, Diagnostic diagnostic, CancellationToken cancellationToken) =>
        (await CodeFixHarness.Fixes(project, provider, diagnostic, cancellationToken)).ShouldHaveSingleItem();

    /// <summary>The fixed document compiles without errors.</summary>
    private static async Task ShouldCompile(Project project, CancellationToken cancellationToken)
    {
        var compilation = (await project.GetCompilationAsync(cancellationToken))!;
        compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    // PHONEY004: Strict generator leaves members without a value

    [Test]
    public async Task Strict_gets_an_Ignore_for_every_uncovered_member(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static Gadget A() => Fake.For<Gadget>().Strict().Generate();

                public static Gadget B() =>
                    Fake.For<Gadget>()
                        .With(x => x.Alpha, 1)
                        .Strict()
                        .Generate();
            }
            """, new StrictAnalyzer(), "PHONEY004", cancellationToken);
        diagnostics.Length.ShouldBe(2);

        var all = await SingleFix(project, new IgnoreUncoveredMembersCodeFix(), diagnostics[0], cancellationToken);
        all.Title.ShouldBe("Ignore the 2 members without a value");
        (await all.TextOf("Usage.cs", cancellationToken)).ShouldContain("Fake.For<Gadget>().Ignore(x => x.Alpha).Ignore(x => x.@class).Strict().Generate()");
        await ShouldCompile(all.Result, cancellationToken);

        var one = await SingleFix(project, new IgnoreUncoveredMembersCodeFix(), diagnostics[1], cancellationToken);
        one.Title.ShouldBe("Ignore 'class'");
        (await one.TextOf("Usage.cs", cancellationToken)).ShouldContain("""
                        .With(x => x.Alpha, 1)
                        .Ignore(x => x.@class)
                        .Strict()
            """);
        await ShouldCompile(one.Result, cancellationToken);
        (await CodeFixHarness.Diagnose(one.Result, new StrictAnalyzer(), "PHONEY004", cancellationToken)).Length.ShouldBe(1);
    }

    // PHONEY005: discarded results

    [Test]
    public async Task Discarded_results_are_assigned_back(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                private static ObjectGenerator<Gadget> _field = Fake.For<Gadget>();

                public static void A()
                {
                    var gen = Fake.For<Gadget>();
                    gen.With(x => x.Alpha, 1)
                       .Seed(2);
                    _field.Seed(3);
                    var point = new Point();
                    gen.Seed(1).Generate();
                    Fake.ForGenerated<Point>().Populate(point);
                }
            }
            """, new UsageAnalyzer(), "PHONEY005", cancellationToken);
        diagnostics.Length.ShouldBe(3);

        var chain = await SingleFix(project, new UseReturnedValueCodeFix(), diagnostics[0], cancellationToken);
        chain.Title.ShouldBe("Assign the result to 'gen'");
        (await chain.TextOf("Usage.cs", cancellationToken)).ShouldContain("""
                    gen = gen.With(x => x.Alpha, 1)
                       .Seed(2);
            """);

        var field = await SingleFix(project, new UseReturnedValueCodeFix(), diagnostics[1], cancellationToken);
        (await field.TextOf("Usage.cs", cancellationToken)).ShouldContain("_field = _field.Seed(3);");

        var populate = await SingleFix(project, new UseReturnedValueCodeFix(), diagnostics[2], cancellationToken);
        populate.Title.ShouldBe("Assign the result to 'point'");
        (await populate.TextOf("Usage.cs", cancellationToken)).ShouldContain("point = Fake.ForGenerated<Point>().Populate(point);");

        var fixedAll = await CodeFixHarness.FixAll(project, new UseReturnedValueCodeFix(), diagnostics, cancellationToken);
        (await CodeFixHarness.Diagnose(fixedAll, new UsageAnalyzer(), "PHONEY005", cancellationToken)).ShouldBeEmpty();
        await ShouldCompile(fixedAll, cancellationToken);
    }

    [Test]
    public async Task Discarded_results_without_a_variable_to_assign_get_no_fix(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                private static readonly ObjectGenerator<Gadget> Gen = Fake.For<Gadget>();

                public static void A()
                {
                    Fake.For<Gadget>().Seed(1);
                    Gen.Seed(1);
                    foreach (var point in new Point[1])
                        Fake.ForGenerated<Point>().Populate(point);
                }
            }
            """, new UsageAnalyzer(), "PHONEY005", cancellationToken);
        diagnostics.Length.ShouldBe(3);

        foreach (var diagnostic in diagnostics)
            (await CodeFixHarness.Fixes(project, new UseReturnedValueCodeFix(), diagnostic, cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Discarded_results_are_only_assigned_back_through_receivers_that_are_read_once(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public sealed class Holder
            {
                public Point Value;
                public Point Property { get; set; }
                public ObjectGenerator<Gadget> Gen = Fake.For<Gadget>();
                public Holder Inner = null!;
            }

            public sealed class Usage
            {
                private Holder _holder = new();

                private static Holder GetHolder() => new();

                public void A(Holder parameter)
                {
                    var holder = new Holder();
                    Fake.ForGenerated<Point>().Populate(GetHolder().Value);
                    Fake.ForGenerated<Point>().Populate(GetHolder().Property);
                    GetHolder().Gen.Seed(1);
                    Fake.ForGenerated<Point>().Populate(holder.Value);
                    Fake.ForGenerated<Point>().Populate(parameter.Property);
                    Fake.ForGenerated<Point>().Populate(_holder.Inner.Value);
                    this._holder.Gen.Seed(2);
                }
            }
            """, new UsageAnalyzer(), "PHONEY005", cancellationToken);
        diagnostics.Length.ShouldBe(7);

        // A call (or a property getter) as receiver would run twice, possibly returning another object each time.
        foreach (var diagnostic in diagnostics.Take(3))
            (await CodeFixHarness.Fixes(project, new UseReturnedValueCodeFix(), diagnostic, cancellationToken)).ShouldBeEmpty();

        string[] expected =
        [
            "holder.Value = Fake.ForGenerated<Point>().Populate(holder.Value);",
            "parameter.Property = Fake.ForGenerated<Point>().Populate(parameter.Property);",
            "_holder.Inner.Value = Fake.ForGenerated<Point>().Populate(_holder.Inner.Value);",
            "this._holder.Gen = this._holder.Gen.Seed(2);",
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            var fix = await SingleFix(project, new UseReturnedValueCodeFix(), diagnostics[3 + i], cancellationToken);
            (await fix.TextOf("Usage.cs", cancellationToken)).ShouldContain(expected[i]);
            await ShouldCompile(fix.Result, cancellationToken);
        }
    }

    // PHONEY006: Fake.Seeded scope never disposed

    [Test]
    public async Task Seeded_scopes_become_using_declarations(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static void A()
                {
                    // seed everything below
                    Fake.Seeded(1);
                    Fake.One<Gadget>();
                }

                public static void B() { _ = Fake.Seeded(1, "sv"); }

                public static void C() { var scope = Fake.Seeded(1); Fake.One<Gadget>(); }

                public static void D() { using var _ = Fake.Seeded(1); Fake.Seeded(2); }

                public static void E(bool b) { if (b) Fake.Seeded(1); }
            }
            """, new UsageAnalyzer(), "PHONEY006", cancellationToken);
        diagnostics.Length.ShouldBe(5);
        var provider = new DisposeSeededScopeCodeFix();
        provider.GetFixAllProvider().ShouldBeNull();

        var statement = await SingleFix(project, provider, diagnostics[0], cancellationToken);
        statement.Title.ShouldBe("Dispose the seeded scope with 'using'");
        (await statement.TextOf("Usage.cs", cancellationToken)).ShouldContain("""
                    // seed everything below
                    using var _ = Fake.Seeded(1);
                    Fake.One<Gadget>();
            """);
        await ShouldCompile(statement.Result, cancellationToken);

        (await (await SingleFix(project, provider, diagnostics[1], cancellationToken)).TextOf("Usage.cs", cancellationToken))
            .ShouldContain("public static void B() { using var _ = Fake.Seeded(1, \"sv\"); }");
        (await (await SingleFix(project, provider, diagnostics[2], cancellationToken)).TextOf("Usage.cs", cancellationToken))
            .ShouldContain("public static void C() { using var scope = Fake.Seeded(1); Fake.One<Gadget>(); }");

        // '_' is taken, so the second scope gets another name.
        var second = await SingleFix(project, provider, diagnostics[3], cancellationToken);
        (await second.TextOf("Usage.cs", cancellationToken)).ShouldContain("using var _ = Fake.Seeded(1); using var seeded = Fake.Seeded(2);");
        await ShouldCompile(second.Result, cancellationToken);

        // A using declaration can't be the body of an if.
        (await CodeFixHarness.Fixes(project, provider, diagnostics[4], cancellationToken)).ShouldBeEmpty();
    }

    // PHONEY008: rules replaced in the same chain

    [Test]
    public async Task Replaced_rules_are_removed(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static Gadget A() =>
                    Fake.For<Gadget>()
                        .With(x => x.Alpha, 1)
                        .Seed(5)
                        .With(x => x.Alpha, 2)
                        .Generate();

                public static Gadget B() => Fake.For<Gadget>().Ignore(x => x.Email).With(x => x.Email, "a@b.c").With(x => x.Email, "d@e.f").Generate();
            }
            """, new UsageAnalyzer(), "PHONEY008", cancellationToken);
        diagnostics.Length.ShouldBe(3);

        var fix = await SingleFix(project, new RemoveReplacedRuleCodeFix(), diagnostics[0], cancellationToken);
        fix.Title.ShouldBe("Remove the replaced rule");
        (await fix.TextOf("Usage.cs", cancellationToken)).ShouldContain("""
                    Fake.For<Gadget>()
                        .Seed(5)
                        .With(x => x.Alpha, 2)
                        .Generate();
            """);

        var fixedAll = await CodeFixHarness.FixAll(project, new RemoveReplacedRuleCodeFix(), diagnostics, cancellationToken);
        var text = (await fixedAll.Documents.Single().GetTextAsync(cancellationToken)).ToString();
        text.ShouldContain("public static Gadget B() => Fake.For<Gadget>().With(x => x.Email, \"d@e.f\").Generate();");
        (await CodeFixHarness.Diagnose(fixedAll, new UsageAnalyzer(), "PHONEY008", cancellationToken)).ShouldBeEmpty();
        await ShouldCompile(fixedAll, cancellationToken);
    }

    [Test]
    public async Task Replaced_type_registrations_are_removed(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static Gadget A() => Fake.For<Gadget>().WithGeneratorFor(Fake.For<Gadget>().Seed(1)).WithGeneratorFor(Fake.For<Gadget>().Seed(2)).Generate();
            }
            """, new UsageAnalyzer(), "PHONEY008", cancellationToken);

        var fix = await SingleFix(project, new RemoveReplacedRuleCodeFix(), diagnostics.ShouldHaveSingleItem(), cancellationToken);
        (await fix.TextOf("Usage.cs", cancellationToken)).ShouldContain("Fake.For<Gadget>().WithGeneratorFor(Fake.For<Gadget>().Seed(2)).Generate()");
    }

    // PHONEY009: [FakeSubtype] with a subtype that can't be created

    [Test]
    [Arguments("[FakeSubtype<IShape, Square>]\npublic static partial class Shapes;", "FakeSubtype<IShape, Square>", "public static partial class Shapes;")]
    [Arguments("[FakeSubtype<IShape, Square>, FakeSubtype<IShape, Circle>]\npublic static partial class Shapes;", "FakeSubtype<IShape, Square>", "[FakeSubtype<IShape, Circle>]\npublic static partial class Shapes;")]
    [Arguments("[FakeSubtype<IShape, Circle>, FakeSubtype<IShape, Square>]\npublic static partial class Shapes;", "FakeSubtype<IShape, Square>", "[FakeSubtype<IShape, Circle>]\npublic static partial class Shapes;")]
    public async Task Subtype_registrations_that_cannot_be_created_are_removed(string declaration, string attribute, string expected, CancellationToken cancellationToken)
    {
        const string header = "using Phoney.Generation;\npublic interface IShape;\npublic abstract class Square : IShape { }\npublic sealed class Circle : IShape { }\n";
        var project = CodeFixHarness.CreateProject(("Shapes.cs", header + declaration));

        // The generator reports PHONEY009 on the attribute.
        var (_, _, generated) = GeneratorHarness.Run(header + declaration);
        var reported = generated.ShouldHaveSingleItem();
        reported.Id.ShouldBe("PHONEY009");
        reported.Location.SourceSpan.Start.ShouldBe((header + declaration).IndexOf(attribute, StringComparison.Ordinal));

        var tree = (await project.Documents.Single().GetSyntaxTreeAsync(cancellationToken))!;
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("PHONEY009", "Subtype cannot be registered", "", "Phoney", DiagnosticSeverity.Error, true),
            Location.Create(tree, reported.Location.SourceSpan));

        var fix = await SingleFix(project, new RemoveSubtypeRegistrationCodeFix(), diagnostic, cancellationToken);
        fix.Title.ShouldBe($"Remove '[{attribute}]'");
        (await fix.TextOf("Shapes.cs", cancellationToken)).ShouldBe(header + expected);
    }

    // Fix-all support and diagnostics a fix can't act on

    [Test]
    public void Fixes_that_can_apply_together_support_fix_all()
    {
        Microsoft.CodeAnalysis.CodeFixes.CodeFixProvider[] batched =
            [new IgnoreUncoveredMembersCodeFix(), new UseReturnedValueCodeFix(), new RemoveReplacedRuleCodeFix(), new RemoveSubtypeRegistrationCodeFix()];
        foreach (var provider in batched)
            provider.GetFixAllProvider().ShouldBeSameAs(Microsoft.CodeAnalysis.CodeFixes.WellKnownFixAllProviders.BatchFixer);
    }

    private const string Unfixable = """
        public static class Usage
        {
            public static Gadget A() => Make();

            private static Gadget Make() => new();
        }
        """;

    /// <summary>A diagnostic made by hand at the first occurrence of <paramref name="at"/>, as a stale or foreign diagnostic could be.</summary>
    private static async Task<Diagnostic> Handmade(Project project, string id, string at, string? additional = null, ImmutableDictionary<string, string?>? properties = null)
    {
        var tree = (await project.Documents.Single().GetSyntaxTreeAsync())!;
        var text = tree.ToString();
        Location LocationOf(string part) => Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(text.IndexOf(part, StringComparison.Ordinal), part.Length));
        IEnumerable<Location>? additionalLocations = additional is null ? null : [LocationOf(additional)];
        return Diagnostic.Create(new DiagnosticDescriptor(id, id, "", "Phoney", DiagnosticSeverity.Warning, true), LocationOf(at), additionalLocations, properties);
    }

    [Test]
    public async Task Ignore_fix_needs_the_member_names_and_a_Strict_call(CancellationToken cancellationToken)
    {
        var (project, _) = Project(Unfixable);
        var members = ImmutableDictionary<string, string?>.Empty.Add("Members", "Alpha");

        (await CodeFixHarness.Fixes(project, new IgnoreUncoveredMembersCodeFix(), await Handmade(project, "PHONEY004", "Make()"), cancellationToken)).ShouldBeEmpty();
        (await CodeFixHarness.Fixes(project, new IgnoreUncoveredMembersCodeFix(), await Handmade(project, "PHONEY004", "Usage", properties: members), cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Remove_replaced_rule_fix_needs_the_replaced_call(CancellationToken cancellationToken)
    {
        var (project, _) = Project(Unfixable);

        (await CodeFixHarness.Fixes(project, new RemoveReplacedRuleCodeFix(), await Handmade(project, "PHONEY008", "Make()"), cancellationToken)).ShouldBeEmpty();
        // The replaced call must be a member call (receiver.With(...)), so there is a receiver to keep.
        (await CodeFixHarness.Fixes(project, new RemoveReplacedRuleCodeFix(), await Handmade(project, "PHONEY008", "Usage", additional: "Make()"), cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Remove_subtype_fix_needs_an_attribute(CancellationToken cancellationToken)
    {
        var (project, _) = Project(Unfixable);

        (await CodeFixHarness.Fixes(project, new RemoveSubtypeRegistrationCodeFix(), await Handmade(project, "PHONEY009", "Usage"), cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Seeded_scope_names_avoid_every_name_the_member_declares(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static void F(int seeded)
                {
                    Func<int, int> f = _ => 1;
                    int.TryParse("1", out var seeded2);
                    foreach (var item in new int[0]) { }
                    try { } catch (Exception e) { }
                    void Local() { }
                    Fake.Seeded(1);
                }

                public static void G() { IDisposable a = Fake.Seeded(1), b = Fake.Seeded(2); }
            }
            """, new UsageAnalyzer(), "PHONEY006", cancellationToken);
        diagnostics.Length.ShouldBe(3);
        var provider = new DisposeSeededScopeCodeFix();

        var fix = await SingleFix(project, provider, diagnostics[0], cancellationToken);
        (await fix.TextOf("Usage.cs", cancellationToken)).ShouldContain("using var seeded3 = Fake.Seeded(1);");
        await ShouldCompile(fix.Result, cancellationToken);

        // A declaration of several variables can't become one using declaration.
        (await CodeFixHarness.Fixes(project, provider, diagnostics[1], cancellationToken)).ShouldBeEmpty();
        (await CodeFixHarness.Fixes(project, provider, diagnostics[2], cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Populated_structs_are_assigned_back_to_writable_parameters_only(CancellationToken cancellationToken)
    {
        var (project, diagnostics) = await Diagnose("""
            public static class Usage
            {
                public static void H(Point p, in Point q, Point[] points)
                {
                    Fake.ForGenerated<Point>().Populate(p);
                    Fake.ForGenerated<Point>().Populate(q);
                    Fake.ForGenerated<Point>().Populate(points[0]);
                    Fake.Populate<Point?>(p);
                }
            }
            """, new UsageAnalyzer(), "PHONEY005", cancellationToken);
        diagnostics.Length.ShouldBe(4);

        var fix = await SingleFix(project, new UseReturnedValueCodeFix(), diagnostics[0], cancellationToken);
        (await fix.TextOf("Usage.cs", cancellationToken)).ShouldContain("p = Fake.ForGenerated<Point>().Populate(p);");
        await ShouldCompile(fix.Result, cancellationToken);

        // An 'in' parameter is read-only, an array element is not a variable the fix assigns, and a Point? result doesn't fit back into a Point.
        foreach (var diagnostic in diagnostics.Skip(1))
            (await CodeFixHarness.Fixes(project, new UseReturnedValueCodeFix(), diagnostic, cancellationToken)).ShouldBeEmpty();
    }
}
