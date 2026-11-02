namespace Phoney.Tests.Docs;

/// <summary>How examples are cut out of test code and written into the guides (<see cref="DocSnippets"/>).</summary>
public sealed class DocSnippetsTests
{
    [Test]
    public void Cuts_a_region_out_of_code_and_removes_its_common_indentation()
    {
        const string code = """
            public void Example()
            {
                // snippet: hello
                var faker = new Faker(seed: 1);
                if (true)
                    faker.Person.FullName();
                // end snippet
                faker.ShouldNotBeNull();
            }
            """;

        var snippets = DocSnippets.Extract(code, "Example.cs");

        snippets["hello"].ShouldBe("var faker = new Faker(seed: 1);\nif (true)\n    faker.Person.FullName();");
    }

    [Test]
    public void Keeps_blank_lines_inside_a_region()
    {
        const string code = "    // snippet: gap\n    var a = 1;\n\n    var b = 2;\n    // end snippet\n";

        DocSnippets.Extract(code, "Gap.cs")["gap"].ShouldBe("var a = 1;\n\nvar b = 2;");
    }

    [Test]
    public void Refuses_a_region_that_is_not_closed()
    {
        var error = Should.Throw<FormatException>(() => DocSnippets.Extract("// snippet: open\nvar a = 1;\n", "Open.cs"));

        error.Message.ShouldContain("open");
        error.Message.ShouldContain("Open.cs");
    }

    [Test]
    public void Refuses_two_regions_with_one_name()
    {
        const string code = "// snippet: twice\n// end snippet\n// snippet: twice\n// end snippet\n";

        Should.Throw<FormatException>(() => DocSnippets.Extract(code, "Twice.cs")).Message.ShouldContain("twice");
    }

    [Test]
    public void Writes_the_code_between_the_markers_of_a_guide()
    {
        const string markdown = "Intro\n\n<!-- snippet: hello -->\n```csharp\nold code\n```\n<!-- end snippet -->\n\nOutro\n";

        var rendered = DocSnippets.Render(markdown, new Dictionary<string, string> { ["hello"] = "new code();" }, "guide.md");

        rendered.ShouldBe("Intro\n\n<!-- snippet: hello -->\n```csharp\nnew code();\n```\n<!-- end snippet -->\n\nOutro\n");
    }

    [Test]
    public void Fills_markers_that_have_no_code_yet()
    {
        const string markdown = "<!-- snippet: hello -->\n<!-- end snippet -->\n";

        DocSnippets.Render(markdown, new Dictionary<string, string> { ["hello"] = "x();" }, "guide.md")
            .ShouldBe("<!-- snippet: hello -->\n```csharp\nx();\n```\n<!-- end snippet -->\n");
    }

    [Test]
    public void Refuses_a_guide_that_names_an_unknown_snippet()
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            DocSnippets.Render("<!-- snippet: missing -->\n<!-- end snippet -->\n", new Dictionary<string, string>(), "guide.md"));

        error.Message.ShouldContain("missing");
        error.Message.ShouldContain("guide.md");
    }

    [Test]
    public void Lists_the_snippets_a_guide_uses()
    {
        const string markdown = "<!-- snippet: a -->\n<!-- end snippet -->\ntext\n<!-- snippet: b -->\n```csharp\nb\n```\n<!-- end snippet -->\n";

        DocSnippets.NamesIn(markdown).ShouldBe(["a", "b"]);
    }
}
