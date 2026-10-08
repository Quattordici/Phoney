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
}
