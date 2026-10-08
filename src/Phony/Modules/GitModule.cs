using System.Globalization;
using System.Text.RegularExpressions;

namespace Phony.Modules;

/// <summary>Git branches, commits and SHAs (faker.js <c>git</c>).</summary>
public sealed partial class GitModule : FakerModule
{
    internal GitModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a branch name, e.g. <c>feed-parse</c>.</summary>
    public string Branch() => $"{ReplaceFirstSpace(Faker.Hacker.Noun())}-{ReplaceFirstSpace(Faker.Hacker.Verb())}";

    /// <summary>Returns a commit entry as printed by <c>git log</c>.</summary>
    /// <param name="merge">Whether to include a <c>Merge:</c> line; 20% chance when not given.</param>
    /// <param name="useCrLf">Use CRLF line endings (the faker.js default) instead of LF.</param>
    /// <param name="referenceDate">Reference for the commit date.</param>
    public string CommitEntry(bool? merge = null, bool useCrLf = true, DateTimeOffset? referenceDate = null)
    {
        var lines = new List<string> { $"commit {CommitSha()}" };
        if (merge ?? Random.Bool(0.2))
            lines.Add($"Merge: {CommitSha(7)} {CommitSha(7)}");

        var firstName = Faker.Person.FirstName();
        var lastName = Faker.Person.LastName();
        var fullName = Faker.Person.FullName(firstName, lastName);
        var username = Faker.Internet.Username(firstName, lastName);
        var user = AuthorCleanup().Replace(Random.Bool() ? fullName : username, "");
        var email = Faker.Internet.Email(firstName, lastName);
        lines.Add($"Author: {user} <{email}>");
        lines.Add($"Date: {CommitDate(referenceDate)}");
        lines.Add("");
        lines.Add(new string('\u00A0', 4) + CommitMessage());
        lines.Add("");
        return string.Join(useCrLf ? "\r\n" : "\n", lines);
    }

    /// <summary>Returns a commit message, e.g. <c>navigate neural capacitor</c>.</summary>
    public string CommitMessage() => $"{Faker.Hacker.Verb()} {Faker.Hacker.Adjective()} {Faker.Hacker.Noun()}";

    /// <summary>Returns a commit date in git's format, e.g. <c>Mon Nov 7 05:38:37 2022 +0600</c>.</summary>
    public string CommitDate(DateTimeOffset? referenceDate = null)
    {
        var date = Faker.Date.Recent(1, referenceDate);
        var zone = Random.Int(-11, 12);
        var inv = CultureInfo.InvariantCulture;
        return string.Create(inv, $"{date.ToString("ddd MMM d HH:mm:ss yyyy", inv)} {(zone >= 0 ? '+' : '-')}{Math.Abs(zone):00}00");
    }

    /// <summary>Returns a commit SHA of <paramref name="length"/> lower-case hexadecimal characters (40 by default).</summary>
    public string CommitSha(int length = 40) => Faker.String.Hexadecimal(length, Casing.Lower, prefix: "");

    /// <summary>Replaces the first space with a dash, as faker.js does for branch names.</summary>
    private static string ReplaceFirstSpace(string text)
    {
        var index = text.IndexOf(' ');
        return index < 0 ? text : string.Concat(text.AsSpan(0, index), "-", text.AsSpan(index + 1));
    }

    /// <summary>Characters git doesn't allow at the edges of author names, and angle brackets/newlines anywhere.</summary>
    [GeneratedRegex("""^[.,:;"\\']|[<>\n]|[.,:;"\\']$""")]
    private static partial Regex AuthorCleanup();
}
