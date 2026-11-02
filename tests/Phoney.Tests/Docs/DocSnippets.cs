using System.Text;

namespace Phoney.Tests.Docs;

/// <summary>
/// Keeps the C# examples of the guides in step with code that compiles and runs. An example is a region of a test,
/// between <c>// snippet: name</c> and <c>// end snippet</c>; a guide shows it between <c>&lt;!-- snippet: name --&gt;</c>
/// and <c>&lt;!-- end snippet --&gt;</c>. <see cref="DocExampleTests"/> fails when a guide shows other code than its test runs.
/// </summary>
internal static class DocSnippets
{
    private const string CodeStart = "// snippet:";
    private const string CodeEnd = "// end snippet";
    private const string DocStart = "<!-- snippet:";
    private const string DocEnd = "<!-- end snippet -->";

    /// <summary>The regions of one source file by name, without their common indentation.</summary>
    public static Dictionary<string, string> Extract(string code, string fileName)
    {
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;
        var lines = new List<string>();

        foreach (var line in Lines(code))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(CodeStart, StringComparison.Ordinal))
            {
                if (name is not null)
                    throw new FormatException($"{fileName}: snippet '{name}' is not closed before snippet '{trimmed[CodeStart.Length..].Trim()}' starts. End it with '{CodeEnd}'.");
                name = trimmed[CodeStart.Length..].Trim();
                lines.Clear();
            }
            else if (trimmed == CodeEnd)
            {
                if (name is null)
                    throw new FormatException($"{fileName}: '{CodeEnd}' without a '{CodeStart} name' before it.");
                if (!snippets.TryAdd(name, Dedent(lines)))
                    throw new FormatException($"{fileName}: snippet '{name}' is defined twice; give each region its own name.");
                name = null;
            }
            else if (name is not null)
            {
                lines.Add(line);
            }
        }

        return name is null ? snippets : throw new FormatException($"{fileName}: snippet '{name}' is not closed. End it with '{CodeEnd}'.");
    }

    /// <summary>The names of the snippets a guide shows, in order.</summary>
    public static List<string> NamesIn(string markdown) =>
        [.. Lines(markdown).Select(l => l.Trim()).Where(IsDocStart).Select(DocName)];

    /// <summary>The guide with the code of every snippet written between its markers.</summary>
    public static string Render(string markdown, IReadOnlyDictionary<string, string> snippets, string fileName)
    {
        var result = new StringBuilder();
        var inside = false;

        foreach (var line in Lines(markdown))
        {
            var trimmed = line.Trim();
            if (IsDocStart(trimmed))
            {
                var name = DocName(trimmed);
                if (!snippets.TryGetValue(name, out var code))
                    throw new InvalidOperationException($"{fileName} shows snippet '{name}', which no test defines. Add a '{CodeStart} {name}' region to a test in tests/Phoney.Tests/Docs/*Examples.cs.");
                result.Append(line).Append('\n').Append("```csharp\n").Append(code).Append("\n```\n");
                inside = true;
            }
            else if (trimmed == DocEnd)
            {
                result.Append(line).Append('\n');
                inside = false;
            }
            else if (!inside)
            {
                result.Append(line).Append('\n');
            }
        }

        if (inside)
            throw new InvalidOperationException($"{fileName}: a snippet is not closed. End it with '{DocEnd}'.");

        // Every line was written with a line break after it, one more than the text has.
        return result.ToString(0, result.Length - 1);
    }

    private static bool IsDocStart(string trimmed) => trimmed.StartsWith(DocStart, StringComparison.Ordinal) && trimmed.EndsWith("-->", StringComparison.Ordinal);

    private static string DocName(string trimmed) => trimmed[DocStart.Length..^"-->".Length].Trim();

    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string Dedent(List<string> lines)
    {
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(l => l.Trim().Length == 0 ? "" : l[indent..].TrimEnd()));
    }
}
