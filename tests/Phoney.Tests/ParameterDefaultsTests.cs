using Phoney.Generation;

namespace Phoney.Tests;

public enum ComplaintState
{
    New,
    Open,
    Closed,
}

/// <summary>Optional constructor parameters of every kind of constant C# allows as a default.</summary>
public sealed record Complaint(
    string Title,
    int Priority = 3,
    string Channel = "web",
    ComplaintState State = ComplaintState.Open,
    decimal Fee = 1.5m,
    DateTime? Due = null,
    Guid Id = default,
    double Ratio = double.NaN,
    char Grade = 'b',
    long Big = 5_000_000_000,
    string? Note = null,
    float Weight = 0.25f);

public sealed class HelpDesk
{
    public string Name { get; set; } = "";
    public Complaint Complaint { get; set; } = null!;
}

[FakeFor<Complaint>]
[FakeFor<HelpDesk>]
public static partial class ComplaintFakes;

/// <summary><c>WithParameterDefaults()</c> gives optional constructor parameters the defaults their type declares.</summary>
public sealed class ParameterDefaultsTests
{
    private static ObjectGenerator<Complaint> Complaints(bool generated) =>
        (generated ? ComplaintFakes.Complaint : new ObjectGenerator<Complaint>(new ReflectionModel<Complaint>())).Seed(1);

    private static ObjectGenerator<HelpDesk> Desks(bool generated) =>
        (generated ? ComplaintFakes.HelpDesk : new ObjectGenerator<HelpDesk>(new ReflectionModel<HelpDesk>())).Seed(1);

    private static bool HasDeclaredDefaults(Complaint t) =>
        t.Priority == 3 && t.Channel == "web" && t.State == ComplaintState.Open && t.Fee == 1.5m && t.Due is null && t.Id == Guid.Empty
        && double.IsNaN(t.Ratio) && t.Grade == 'b' && t.Big == 5_000_000_000 && t.Note is null && t.Weight == 0.25f;

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Optional_parameters_get_their_declared_defaults(bool generated) =>
        Complaints(generated).WithParameterDefaults().Generate(20).ShouldAllBe(t => t.Title.Length > 0 && HasDeclaredDefaults(t));

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Without_the_option_they_are_generated(bool generated)
    {
        Complaints(generated).Generate(20).Select(t => t.Priority).Distinct().Count().ShouldBeGreaterThan(1);
        Complaints(generated).WithParameterDefaults(false).Generate(20).ShouldBe(Complaints(generated).Generate(20));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Rules_win_over_the_defaults(bool generated)
    {
        var complaints = Complaints(generated).WithParameterDefaults().With(t => t.Priority, 9).Unique(t => t.Id).Generate(20);

        complaints.ShouldAllBe(t => t.Priority == 9 && t.Channel == "web");
        complaints.Select(t => t.Id).Distinct().Count().ShouldBe(20);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Nested_objects_get_their_defaults_too(bool generated) =>
        Desks(generated).WithParameterDefaults().Generate(10).ShouldAllBe(d => d.Name.Length > 0 && HasDeclaredDefaults(d.Complaint));

    [Test]
    public void A_default_covers_the_member_for_strict_mode() =>
        Should.NotThrow(() => ComplaintFakes.Complaint.Seed(1).Strict().WithParameterDefaults().With(t => t.Title, "Printer jam").Generate(3));

    [Test]
    public void Generated_and_reflection_models_agree() =>
        Desks(generated: false).WithParameterDefaults().Generate(20).Select(d => (d.Name, d.Complaint))
            .ShouldBe(Desks(generated: true).WithParameterDefaults().Generate(20).Select(d => (d.Name, d.Complaint)));
}
