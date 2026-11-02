namespace Phoney.Tests.Docs;

/// <summary>
/// The guides show the code their tests run. To change an example, change the test under tests/Phoney.Tests/Docs (the *Examples.cs files) and run
/// <c>PHONEY_UPDATE_DOCS=1 dotnet test --project tests/Phoney.Tests -- --treenode-filter "/*/*/DocExampleTests/*"</c>, which writes it into the guides.
/// </summary>
public sealed class DocExampleTests
{
    private static readonly string Root = FindRoot();

    private static readonly Lazy<Dictionary<string, string>> Snippets = new(() =>
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "tests", "Phoney.Tests", "Docs"), "*Examples.cs").Order(StringComparer.Ordinal))
        {
            foreach (var (name, code) in DocSnippets.Extract(File.ReadAllText(file), Path.GetFileName(file)))
            {
                if (!all.TryAdd(name, code))
                    throw new FormatException($"Snippet '{name}' is defined in two files; give each region its own name.");
            }
        }
        return all;
    });

    public static IEnumerable<string> Guides() =>
        [.. Directory.EnumerateFiles(Path.Combine(Root, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(Root, "README.md"))
            .Select(p => Path.GetRelativePath(Root, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    [Test]
    [MethodDataSource(nameof(Guides))]
    public async Task Shows_the_code_its_tests_run(string guide)
    {
        var path = Path.Combine(Root, guide);
        // Windows checkouts may have CRLF line endings (text=auto); the examples are compared line by line.
        var markdown = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var rendered = DocSnippets.Render(markdown, Snippets.Value, guide);

        if (rendered != markdown && Environment.GetEnvironmentVariable("PHONEY_UPDATE_DOCS") == "1")
            await File.WriteAllTextAsync(path, rendered);
        else
            markdown.ShouldBe(rendered, $"{guide} shows other code than its tests run. Run the tests with PHONEY_UPDATE_DOCS=1 to write the examples into it.");
    }

    [Test]
    public void Every_example_is_shown_in_a_guide()
    {
        var shown = Guides().SelectMany(g => DocSnippets.NamesIn(File.ReadAllText(Path.Combine(Root, g)))).ToHashSet(StringComparer.Ordinal);

        Snippets.Value.Keys.Where(k => !shown.Contains(k)).Order(StringComparer.Ordinal).ShouldBeEmpty("These examples are in no guide; show them or remove them.");
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Phoney.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"No Phoney.slnx above {AppContext.BaseDirectory}; the guide tests read the repository's docs.");
    }
}
