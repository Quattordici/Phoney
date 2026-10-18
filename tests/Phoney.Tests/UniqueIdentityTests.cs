using Phoney.Generation;

namespace Phoney.Tests;

public sealed class UniqueMember
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
}

[FakeFor<UniqueMember>]
public static partial class UniqueMemberFakes;

/// <summary>
/// <c>Unique</c> on members read from the object's identity (names, email, address…): drawing such a member again returns the same cached value,
/// so a duplicate generates the whole object again, and the name and email keep agreeing.
/// </summary>
public sealed class UniqueIdentityTests
{
    [Test]
    public void Unique_first_names_go_far_beyond_the_first_collision()
    {
        // Before the object retry this threw after 87 objects (the first repeated name), with 3,000+ names to choose from.
        var names = Fake.For<UniqueMember>().Seed(1).Unique(x => x.FirstName).Generate(1_000).Select(s => s.FirstName).ToList();

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(1_000);
    }

    [Test]
    public void Unique_emails_still_agree_with_the_names()
    {
        var subscribers = Fake.For<UniqueMember>().Seed(2).Unique(x => x.Email).Generate(20_000);

        subscribers.Select(s => s.Email).Distinct(StringComparer.Ordinal).Count().ShouldBe(20_000);
        foreach (var s in subscribers.Take(200))
        {
            var local = s.Email[..s.Email.IndexOf('@', StringComparison.Ordinal)];
            (local.Contains(Clean(s.FirstName), StringComparison.OrdinalIgnoreCase) || local.Contains(Clean(s.LastName), StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue($"{s.Email} should describe {s.FirstName} {s.LastName}");
        }

        static string Clean(string name) => new([.. name.Where(char.IsAsciiLetterOrDigit).Take(3)]);
    }

    [Test]
    public void Objects_without_a_duplicate_are_the_ones_you_get_without_unique()
    {
        var plain = Fake.For<UniqueMember>().Seed(3).Generate(60);
        var unique = Fake.For<UniqueMember>().Seed(3).Unique(x => x.FirstName).Generate(60);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < plain.Count; i++)
        {
            if (!seen.Add(plain[i].FirstName))
                break; // from the first duplicate on, the objects differ
            unique[i].Email.ShouldBe(plain[i].Email);
        }
    }

    [Test]
    public void Reflection_and_generated_models_agree()
    {
        var reflection = new ObjectGenerator<UniqueMember>(new ReflectionModel<UniqueMember>()).Seed(4).Unique(x => x.FirstName).Generate(300);
        var generated = UniqueMemberFakes.UniqueMember.Seed(4).Unique(x => x.FirstName).Generate(300);

        generated.Select(s => (s.FirstName, s.Email)).ShouldBe(reflection.Select(s => (s.FirstName, s.Email)));
    }

    [Test]
    public void Running_out_of_values_says_which_member()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            Fake.For<UniqueMember>().Seed(5).Unique(x => x.FirstName, maxAttempts: 3).Generate(5_000));

        ex.Message.ShouldContain("unique FirstName");
        ex.Message.ShouldContain("3 attempts");
    }

    [Test]
    public void Combines_with_a_where_condition()
    {
        var subscribers = Fake.For<UniqueMember>().Seed(6)
            .Unique(x => x.FirstName)
            .Where(s => s.LastName.Length > 4)
            .Generate(300);

        subscribers.Select(s => s.FirstName).Distinct(StringComparer.Ordinal).Count().ShouldBe(300);
        subscribers.ShouldAllBe(s => s.LastName.Length > 4);
    }

    [Test]
    public void Parallel_generation_never_repeats_a_value()
    {
        var subscribers = Fake.For<UniqueMember>().Seed(7).Unique(x => x.FirstName).GenerateParallel(1_000);

        subscribers.Select(s => s.FirstName).Distinct(StringComparer.Ordinal).Count().ShouldBe(1_000);
    }

    [Test]
    public void Populate_all_draws_the_object_again()
    {
        var generator = Fake.For<UniqueMember>().Seed(8).Unique(x => x.FirstName);
        var names = Enumerable.Range(0, 300).Select(_ => generator.Populate(new UniqueMember(), PopulateMode.All).FirstName).ToList();

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(300);
    }
}
