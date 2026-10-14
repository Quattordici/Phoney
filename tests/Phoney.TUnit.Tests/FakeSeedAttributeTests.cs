using System.Text.RegularExpressions;
using TUnit.Core.Exceptions;

namespace Phoney.TUnit.Tests;

internal static class Seeds
{
    /// <summary>The first name a faker with <paramref name="seed"/> produces, the same as the facade inside a scope seeded with it.</summary>
    public static string FirstNameFor(long seed) => new Faker(Fake.Locale, seed).Person.FirstName();
}

/// <summary>[FakeSeed] seeds the facade for one test.</summary>
public sealed class FakeSeedAttributeTests
{
    [Test, FakeSeed(42)]
    public void A_pinned_seed_seeds_the_facade() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));

    [Test, FakeSeed]
    public void Without_a_number_the_facade_is_seeded_too() => Fake.IsSeeded.ShouldBeTrue();

    [Test, FakeSeed(42)]
    public void Generators_made_by_the_facade_follow_the_seed()
    {
        var first = Fake.One<Customer>();
        using var _ = Fake.Seeded(42);

        // The same sequence as the seeded scope the attribute opened, started over: the first customer comes out the same.
        Fake.One<Customer>().Email.ShouldBe(first.Email);
    }

    [Test]
    public void Tests_without_the_attribute_are_left_alone() => Fake.IsSeeded.ShouldBeFalse();

    [Test]
    public void The_seed_is_optional_and_readable()
    {
        new FakeSeedAttribute().Seed.ShouldBeNull();
        new FakeSeedAttribute(7).Seed.ShouldBe(7);
    }
}

/// <summary>The attribute on a class applies to its tests; on a method it wins.</summary>
[FakeSeed(7)]
public sealed class ClassLevelFakeSeedTests
{
    [Test]
    public void The_class_seed_applies() => Fake.Person.FirstName().ShouldBe(new Faker(Fake.Locale, 7).Person.FirstName());

    [Test, FakeSeed(8)]
    public void The_method_seed_wins() => Fake.Person.FirstName().ShouldBe(new Faker(Fake.Locale, 8).Person.FirstName());
}

/// <summary>What happens around the test body: seeding, and reporting the seed when the test fails.</summary>
public sealed class SeededRunTests
{
    private static readonly Regex SeedInMessage = new(@"seed (\d+)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static async Task<(List<string> Output, Exception? Failure, string SeenName)> Run(long? pinned, long? environment, Func<Exception?> body)
    {
        var output = new List<string>();
        var seen = "";
        Exception? failure = null;
        try
        {
            await SeededRun.Execute(pinned, environment, output.Add, () =>
            {
                seen = Fake.Person.FirstName();
                if (body() is { } exception)
                    throw exception;
                return ValueTask.CompletedTask;
            });
        }
        catch (Exception e)
        {
            failure = e;
        }

        return (output, failure, seen);
    }

    [Test]
    public async Task A_passing_test_reports_nothing()
    {
        var (output, failure, seen) = await Run(null, null, () => null);

        output.ShouldBeEmpty();
        failure.ShouldBeNull();
        seen.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task A_failing_test_reports_the_seed_that_brings_its_data_back()
    {
        var problem = new InvalidOperationException("boom");

        var (output, failure, seen) = await Run(null, null, () => problem);

        failure.ShouldBeSameAs(problem); // rethrown as it was, stack trace and type intact
        var message = output.ShouldHaveSingleItem();
        var seed = long.Parse(SeedInMessage.Match(message).Groups[1].Value);
        Seeds.FirstNameFor(seed).ShouldBe(seen); // the reported seed replays what the test saw
        message.ShouldContain($"[FakeSeed({seed})]");
        message.ShouldContain($"PHONEY_SEED={seed}");
    }

    [Test]
    public async Task Random_seeds_differ_between_runs()
    {
        var seeds = new List<string>();
        for (var i = 0; i < 10; i++)
            seeds.Add((await Run(null, null, () => new InvalidOperationException())).Output.Single());

        seeds.Distinct().Count().ShouldBeGreaterThan(5);
    }

    [Test]
    public async Task A_pinned_seed_is_used_and_named_as_pinned()
    {
        var (output, _, seen) = await Run(42, null, () => new InvalidOperationException());

        seen.ShouldBe(Seeds.FirstNameFor(42));
        var message = output.ShouldHaveSingleItem();
        message.ShouldContain("pinned seed 42");
        message.ShouldNotContain("Replay"); // it already is the replay
    }

    [Test]
    public async Task The_environment_seed_replaces_a_random_one_and_says_so()
    {
        var (output, _, seen) = await Run(null, 99, () => new InvalidOperationException());

        seen.ShouldBe(Seeds.FirstNameFor(99));
        var message = output.ShouldHaveSingleItem();
        message.ShouldContain("seed 99 from PHONEY_SEED");
        message.ShouldContain("PHONEY_SEED=99");
    }

    [Test]
    public async Task A_pinned_seed_wins_over_the_environment()
    {
        var (_, _, seen) = await Run(42, 99, () => null);

        seen.ShouldBe(Seeds.FirstNameFor(42));
    }

    [Test]
    public async Task A_skipped_test_reports_nothing_and_still_skips()
    {
        var skip = new SkipTestException("not today");

        var (output, failure, _) = await Run(null, null, () => skip);

        output.ShouldBeEmpty();
        failure.ShouldBeSameAs(skip);
    }

    [Test]
    public async Task The_facade_is_seeded_while_the_body_runs_and_free_afterwards()
    {
        var seededInside = false;

        await SeededRun.Execute(5, null, _ => { }, () =>
        {
            seededInside = Fake.IsSeeded;
            return ValueTask.CompletedTask;
        });

        seededInside.ShouldBeTrue();
        Fake.IsSeeded.ShouldBeFalse();
    }
}
