using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Tech jargon (faker.js <c>hacker</c>).</summary>
public sealed class HackerModule : FakerModule
{
    internal HackerModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an abbreviation, e.g. <c>SQL</c>.</summary>
    public string Abbreviation() => Faker.Pick(DataKeys.HackerAbbreviation);

    /// <summary>Returns an adjective, e.g. <c>wireless</c>.</summary>
    public string Adjective() => Faker.Pick(DataKeys.HackerAdjective);

    /// <summary>Returns a noun, e.g. <c>bandwidth</c>.</summary>
    public string Noun() => Faker.Pick(DataKeys.HackerNoun);

    /// <summary>Returns a verb, e.g. <c>override</c>.</summary>
    public string Verb() => Faker.Pick(DataKeys.HackerVerb);

    /// <summary>Returns an -ing verb, e.g. <c>compressing</c>.</summary>
    public string IngVerb() => Faker.Pick(DataKeys.HackerIngverb);

    /// <summary>Returns a hacker phrase, e.g. <c>Try to reboot the SQL bus, maybe it will bypass the virtual application!</c></summary>
    public string Phrase() => Faker.Pick(DataKeys.HackerPhrase);
}
