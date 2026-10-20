using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Issue
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Title { get; set; } = "";
    public List<IssueNote> Notes { get; set; } = [];
}

public sealed class IssueNote
{
    public DateTimeOffset CreatedAt { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>Declares its updated date before its creation date.</summary>
public sealed class Revision
{
    public DateTime UpdatedAt { get; set; }
    public string Author { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[FakeFor<Issue>]
[FakeFor<Revision>]
public static partial class IssueFakes;

/// <summary>A creation time the object already has, or that a rule gives it, is the one its other dates are built around.</summary>
public sealed class GivenCreationTimeTests
{
    private static readonly DateTime Created = new(2024, 12, 30, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Reference = Faker.DefaultSeededReferenceDate;

    private static ObjectGenerator<Issue> Issues(bool generated) =>
        (generated ? IssueFakes.Issue : new ObjectGenerator<Issue>(new ReflectionModel<Issue>())).Seed(1);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Updated_dates_follow_a_creation_time_from_a_rule(bool generated)
    {
        var issues = Issues(generated).With(t => t.CreatedAt, Created).Generate(300);

        issues.ShouldAllBe(t => t.UpdatedAt >= Created && t.UpdatedAt <= Reference.UtcDateTime);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Updated_dates_follow_a_creation_time_from_a_factory(bool generated)
    {
        var issues = Issues(generated).With(t => t.CreatedAt, f => f.Date.Recent(3).UtcDateTime).Generate(300);

        issues.ShouldAllBe(t => t.UpdatedAt >= t.CreatedAt);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populate_builds_the_dates_around_a_kept_creation_time(bool generated)
    {
        var issues = Issues(generated).Populate(Enumerable.Range(0, 300).Select(_ => new Issue { CreatedAt = Created }));

        issues.ShouldAllBe(t => t.CreatedAt == Created && t.UpdatedAt >= Created);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_creation_time_after_the_reference_date_is_never_followed_by_earlier_dates(bool generated)
    {
        var later = Reference.UtcDateTime.AddDays(40);

        var issues = Issues(generated).WithCausalTimelines().Populate(Enumerable.Range(0, 100).Select(_ => new Issue { CreatedAt = later }));

        issues.ShouldAllBe(t => t.UpdatedAt == later && t.ExpiresAt > later);
        issues.SelectMany(t => t.Notes).ShouldAllBe(n => n.CreatedAt == later);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Causal_children_are_created_after_a_given_creation_time(bool generated)
    {
        var issues = Issues(generated).WithCausalTimelines().With(t => t.CreatedAt, Created).Generate(200);

        issues.SelectMany(t => t.Notes).ShouldNotBeEmpty();
        issues.SelectMany(t => t.Notes).ShouldAllBe(n => n.CreatedAt >= Created);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_other_members_draw_what_they_drew_before(bool generated)
    {
        var plain = Issues(generated).Generate(50);
        var ruled = Issues(generated).With(t => t.CreatedAt, Created).Generate(50);

        ruled.Select(Others).ShouldBe(plain.Select(Others));

        static string Others(Issue t) => $"{t.Id}|{t.ExpiresAt:O}|{t.Title}|{string.Join(",", t.Notes.Select(n => $"{n.CreatedAt:O} {n.Text}"))}";
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_constant_creation_time_counts_for_dates_declared_before_it(bool generated)
    {
        var revisions = (generated ? IssueFakes.Revision : new ObjectGenerator<Revision>(new ReflectionModel<Revision>())).Seed(1)
            .With(r => r.CreatedAt, Created).Generate(300);

        revisions.ShouldAllBe(r => r.CreatedAt == Created && r.UpdatedAt >= Created && r.UpdatedAt <= Reference.UtcDateTime);
    }

    [Test]
    public void Generated_and_reflection_models_agree()
    {
        var generated = Issues(generated: true).With(t => t.CreatedAt, Created).Generate(30);
        var reflected = Issues(generated: false).With(t => t.CreatedAt, Created).Generate(30);

        reflected.Select(t => (t.CreatedAt, t.UpdatedAt, t.ExpiresAt)).ShouldBe(generated.Select(t => (t.CreatedAt, t.UpdatedAt, t.ExpiresAt)));
    }
}
