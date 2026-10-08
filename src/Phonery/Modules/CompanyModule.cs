using Phonery.Data;

namespace Phonery.Modules;

/// <summary>Company names and corporate jargon (faker.js <c>company</c>).</summary>
public sealed class CompanyModule : FakerModule
{
    internal CompanyModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a company name, e.g. <c>Zieme, Hauck and McClure</c>.</summary>
    public string Name() => Faker.Pick(DataKeys.CompanyNamePattern);

    /// <summary>Returns a catch phrase, e.g. <c>Upgradable systematic flexibility</c>.</summary>
    public string CatchPhrase() => $"{CatchPhraseAdjective()} {CatchPhraseDescriptor()} {CatchPhraseNoun()}";

    /// <summary>Returns a buzz phrase, e.g. <c>cultivate synergistic e-markets</c>.</summary>
    public string BuzzPhrase() => $"{BuzzVerb()} {BuzzAdjective()} {BuzzNoun()}";

    /// <summary>Returns a catch phrase adjective, e.g. <c>Multi-tiered</c>.</summary>
    public string CatchPhraseAdjective() => Faker.Pick(DataKeys.CompanyAdjective);

    /// <summary>Returns a catch phrase descriptor, e.g. <c>composite</c>.</summary>
    public string CatchPhraseDescriptor() => Faker.Pick(DataKeys.CompanyDescriptor);

    /// <summary>Returns a catch phrase noun, e.g. <c>leverage</c>.</summary>
    public string CatchPhraseNoun() => Faker.Pick(DataKeys.CompanyNoun);

    /// <summary>Returns a buzz adjective, e.g. <c>one-to-one</c>.</summary>
    public string BuzzAdjective() => Faker.Pick(DataKeys.CompanyBuzzAdjective);

    /// <summary>Returns a buzz verb, e.g. <c>empower</c>.</summary>
    public string BuzzVerb() => Faker.Pick(DataKeys.CompanyBuzzVerb);

    /// <summary>Returns a buzz noun, e.g. <c>paradigms</c>.</summary>
    public string BuzzNoun() => Faker.Pick(DataKeys.CompanyBuzzNoun);
}
