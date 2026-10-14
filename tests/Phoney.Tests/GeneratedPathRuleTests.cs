using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Dossier
{
    public string Title { get; set; } = "";
    public Applicant Main { get; set; } = null!;
    public Applicant Partner { get; set; } = null!;
    public List<Entry> Entries { get; set; } = [];
    public Dictionary<string, Entry> ByCode { get; set; } = [];
}

public sealed record Applicant(string Name, string City);

public sealed class Entry
{
    public string Code { get; set; } = "";
    [Range(1, 10)] public int Amount { get; set; }
    public Applicant? Referee { get; set; }
}

[FakeFor<Dossier>]
public static partial class DossierFakes;

/// <summary>Path rules through source-generated models, and their parity with the reflection model.</summary>
public sealed class GeneratedPathRuleTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Dossier> Reflected() => new(new ReflectionModel<Dossier>());

    private static ObjectGenerator<Dossier> Configure(ObjectGenerator<Dossier> g) =>
        g.Seed(9).ReferenceDate(Reference)
            .With(d => d.Main.City, "Oslo")
            .With(d => d.Entries.Each().Code, f => "E-" + f.Random.Int(100, 999))
            .With(d => d.Entries.Each().Referee!.Name, "Referee")
            .Invalid(d => d.ByCode.Values.Each().Amount)
            .Ignore(d => d.Partner.City);

    [Test]
    public void Generated_models_apply_path_rules()
    {
        var dossiers = Configure(DossierFakes.Dossier).Generate(20);

        dossiers.ShouldAllBe(d => d.Main.City == "Oslo");
        dossiers.ShouldAllBe(d => d.Partner.City == null); // ignored: left at its default
        dossiers.SelectMany(d => d.Entries).ShouldNotBeEmpty();
        dossiers.SelectMany(d => d.Entries).ShouldAllBe(e => e.Code.StartsWith("E-") && e.Referee!.Name == "Referee");
        dossiers.SelectMany(d => d.ByCode.Values).ShouldAllBe(e => e.Amount == 0 || e.Amount == 11);
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        string Describe(Dossier d) =>
            $"{d.Title}|{d.Main.Name}/{d.Main.City}|{d.Partner.Name}/{d.Partner.City}|{string.Join(",", d.Entries.Select(e => $"{e.Code}:{e.Amount}:{e.Referee?.Name}/{e.Referee?.City}"))}|" +
            string.Join(",", d.ByCode.Select(p => $"{p.Key}={p.Value.Code}:{p.Value.Amount}"));

        var generated = Configure(DossierFakes.Dossier).Generate(25);
        var reflected = Configure(Reflected()).Generate(25);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
    }

    [Test]
    public void Generated_and_reflection_models_populate_with_path_rules_identically()
    {
        Dossier Existing() => new() { Title = "Kept" };

        var generated = Configure(DossierFakes.Dossier).Populate(Existing());
        var reflected = Configure(Reflected()).Populate(Existing());

        generated.Title.ShouldBe("Kept");
        generated.Main.City.ShouldBe("Oslo");
        reflected.Main.City.ShouldBe(generated.Main.City);
        reflected.Entries.Select(e => e.Code).ShouldBe(generated.Entries.Select(e => e.Code));
    }
}
