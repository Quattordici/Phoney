using System.Globalization;

namespace Phoney.Modules;

/// <summary>Git branch names, commit messages and SHAs.</summary>
public sealed class GitModule : FakerModule
{
    internal GitModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a lower-case branch name such as <c>feed-parse</c> or <c>neural-net-compress</c>.</summary>
    public string Branch() => $"{Faker.Hacker.Noun()}-{Faker.Hacker.Verb()}".Replace(' ', '-').ToLowerInvariant();

    /// <summary>Returns a commit message, e.g. <c>navigate neural capacitor</c>.</summary>
    public string CommitMessage() => $"{Faker.Hacker.Verb()} {Faker.Hacker.Adjective()} {Faker.Hacker.Noun()}";

    /// <summary>Returns a commit SHA of <paramref name="length"/> lower-case hexadecimal characters (40 by default).</summary>
    public string CommitSha(int length = 40) => Faker.String.Hexadecimal(length, Casing.Lower, prefix: "");

    /// <summary>Returns a date in the format of <c>git log</c>, e.g. <c>Mon Aug  7 14:03:21 2023 +0000</c> (UTC, recent past).</summary>
    public string CommitDate()
    {
        var date = Faker.Date.Recent().UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{date:ddd MMM} {date.Day,2} {date:HH:mm:ss yyyy} +0000");
    }

    /// <summary>Returns a commit as <c>git log</c> prints it: commit, author (name and email of one person), date and an indented message.</summary>
    /// <param name="lineEnding"><c>"\n"</c> (default) or <c>"\r\n"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lineEnding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="lineEnding"/> is neither <c>"\n"</c> nor <c>"\r\n"</c>.</exception>
    public string CommitEntry(string lineEnding = "\n")
    {
        ArgumentNullException.ThrowIfNull(lineEnding);
        if (lineEnding is not ("\n" or "\r\n"))
            throw new ArgumentException("The line ending must be \"\\n\" or \"\\r\\n\".", nameof(lineEnding));

        var sha = CommitSha();
        var firstName = Faker.Person.FirstName();
        var lastName = Faker.Person.LastName();
        var author = $"{firstName} {lastName}";
        var email = Faker.Internet.Email(firstName, lastName); // one person, as in faker.js
        var date = CommitDate();
        var message = CommitMessage();
        return string.Join(lineEnding, $"commit {sha}", $"Author: {author} <{email}>", $"Date:   {date}", "", $"    {message}", "");
    }
}
