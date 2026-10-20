using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Phoney.CodeFixes;

namespace Phoney.SourceGenerator.Tests;

public sealed class CodeFixTests
{
    private const string Models = """
        namespace Shop
        {
            public class Address { public string Street { get; set; } = ""; }
            public class Product { public string Name { get; set; } = ""; public Address? Address { get; set; } }
            public class Order { public string Number { get; set; } = ""; }
        }
        """;

    private const string Usage = """
        using Phoney;
        using Shop;

        public static class Usage
        {
            public static object A() => Fake.For<Product>();
        }
        """;

    private static async Task<(Project Project, Diagnostic Diagnostic)> Suggest(CancellationToken cancellationToken, params (string, string)[] documents)
    {
        var project = CodeFixHarness.CreateProject(documents);
        var diagnostic = (await CodeFixHarness.Diagnose(project, new ModelSuggestionAnalyzer(), "PHONEY012", cancellationToken)).ShouldHaveSingleItem();
        return (project, diagnostic);
    }

    [Test]
    public async Task AddFakeFor_extends_the_existing_FakeFor_attribute_list(CancellationToken cancellationToken)
    {
        var (project, diagnostic) = await Suggest(
            cancellationToken,
            ("Models.cs", Models),
            ("Data.cs", """
                using Phoney.Generation;
                using Shop;

                [FakeFor<Order>]
                public static partial class Data;
                """),
            ("Usage.cs", Usage));

        var fix = (await CodeFixHarness.Fixes(project, new AddFakeForCodeFix(), diagnostic, cancellationToken)).ShouldHaveSingleItem();

        fix.Title.ShouldBe("Add [FakeFor<Product>] to 'Data'");
        (await fix.TextOf("Data.cs", cancellationToken)).ShouldContain("[FakeFor<Order>, FakeFor<Product>]");
        (await CodeFixHarness.Diagnose(fix.Result, new ModelSuggestionAnalyzer(), "PHONEY012", cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task AddFakeFor_names_the_type_as_the_target_file_sees_it_and_keeps_the_attribute_name(CancellationToken cancellationToken)
    {
        var (project, diagnostic) = await Suggest(
            cancellationToken,
            ("Models.cs", Models),
            ("Data.cs", """
                namespace Tests;

                [Phoney.Generation.FakeFor<Shop.Order>]
                public static partial class Data;
                """),
            ("Usage.cs", Usage));

        var fix = (await CodeFixHarness.Fixes(project, new AddFakeForCodeFix(), diagnostic, cancellationToken)).ShouldHaveSingleItem();

        (await fix.TextOf("Data.cs", cancellationToken)).ShouldContain("[Phoney.Generation.FakeFor<Shop.Order>, Phoney.Generation.FakeFor<Shop.Product>]");
    }

    [Test]
    public async Task AddFakeFor_creates_a_class_when_the_project_has_none(CancellationToken cancellationToken)
    {
        var (project, diagnostic) = await Suggest(cancellationToken, ("Models.cs", Models), ("Usage.cs", Usage));

        var fix = (await CodeFixHarness.Fixes(project, new AddFakeForCodeFix(), diagnostic, cancellationToken)).ShouldHaveSingleItem();

        fix.Title.ShouldBe("Create 'FakeModels' with [FakeFor<Product>]");
        var text = (await fix.TextOf("FakeModels.cs", cancellationToken)).ReplaceLineEndings("\n");
        text.ShouldBe("""
            using Phoney.Generation;
            using Shop;

            [FakeFor<Product>]
            internal static partial class FakeModels;

            """.ReplaceLineEndings("\n"));
        (await CodeFixHarness.Diagnose(fix.Result, new ModelSuggestionAnalyzer(), "PHONEY012", cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task AddFakeFor_picks_a_free_class_name(CancellationToken cancellationToken)
    {
        var (project, diagnostic) = await Suggest(
            cancellationToken,
            ("Models.cs", Models),
            ("Taken.cs", "public class FakeModels { }"),
            ("Usage.cs", Usage));

        var fix = (await CodeFixHarness.Fixes(project, new AddFakeForCodeFix(), diagnostic, cancellationToken)).ShouldHaveSingleItem();

        fix.Title.ShouldBe("Create 'FakeModels2' with [FakeFor<Product>]");
    }

    [Test]
    [Arguments("[FakeFor<Shop.Order>] public static class Data { }", "public static partial class Data")]
    [Arguments("[FakeFor<Shop.Order>] class Data { }", "partial class Data")]
    [Arguments("[FakeFor<Shop.Order>] public sealed class Data { }", "public sealed partial class Data")]
    public async Task MakePartial_adds_the_modifier_in_the_right_place(string declaration, string expected)
    {
        var project = CodeFixHarness.CreateProject(("Models.cs", Models), ("Data.cs", "using Phoney.Generation;\n" + declaration));
        var diagnostic = NotPartialAt(project, "Data.cs", "Data");

        var fix = (await CodeFixHarness.Fixes(project, new MakePartialCodeFix(), diagnostic, CancellationToken.None)).ShouldHaveSingleItem();

        fix.Title.ShouldBe("Make 'Data' partial");
        (await fix.TextOf("Data.cs", CancellationToken.None)).ShouldContain(expected);
    }

    [Test]
    public async Task MakePartial_also_fixes_containing_types()
    {
        var project = CodeFixHarness.CreateProject(
            ("Models.cs", Models),
            ("Data.cs", "using Phoney.Generation;\npublic class Outer { public partial class Middle { } }\npublic partial class Other { }\npublic static class Wrapper { [FakeFor<Shop.Order>] public static class Data { } }"));
        var diagnostic = NotPartialAt(project, "Data.cs", "Data");

        var fix = (await CodeFixHarness.Fixes(project, new MakePartialCodeFix(), diagnostic, CancellationToken.None)).ShouldHaveSingleItem();

        var text = await fix.TextOf("Data.cs", CancellationToken.None);
        text.ShouldContain("public static partial class Wrapper");
        text.ShouldContain("public static partial class Data");
        text.ShouldContain("public class Outer");
        fix.Title.ShouldBe("Make 'Data' and its containing types partial");
    }

    [Test]
    public async Task MakePartial_fixes_every_class_of_a_document_at_once(CancellationToken cancellationToken)
    {
        var project = CodeFixHarness.CreateProject(
            ("Models.cs", Models),
            ("Data.cs", """
                using Phoney.Generation;
                [FakeFor<Shop.Order>] public static class First { }
                [FakeFor<Shop.Product>] public static class Second { }
                public static class Wrapper
                {
                    [FakeFor<Shop.Order>] public static class Inner { }
                    [FakeFor<Shop.Address>] public static class Other { }
                }
                """));
        var diagnostics = ImmutableArray.Create(
            NotPartialAt(project, "Data.cs", "First"), NotPartialAt(project, "Data.cs", "Second"), NotPartialAt(project, "Data.cs", "Inner"), NotPartialAt(project, "Data.cs", "Other"));

        var fixedProject = await CodeFixHarness.FixAll(project, new MakePartialCodeFix(), diagnostics, cancellationToken);

        var text = (await fixedProject.Documents.Single(d => d.Name == "Data.cs").GetTextAsync(cancellationToken)).ToString();
        foreach (var name in new[] { "First", "Second", "Wrapper", "Inner", "Other" })
            text.ShouldContain($"public static partial class {name}");
        text.ShouldNotContain("partial partial");
    }

    /// <summary>The PHONEY001 diagnostic the generator reports on the identifier of the class.</summary>
    private static Diagnostic NotPartialAt(Project project, string document, string identifier)
    {
        var tree = project.Documents.Single(d => d.Name == document).GetSyntaxTreeAsync().Result!;
        var span = tree.GetRoot().DescendantTokens().Single(t => t.ValueText == identifier).Span;
        var descriptor = new DiagnosticDescriptor("PHONEY001", "Class must be partial", "'{0}' must be declared partial", "Phoney", DiagnosticSeverity.Error, true);
        return Diagnostic.Create(descriptor, Location.Create(tree, span), identifier);
    }
}
