using Phoney.Xunit;

namespace Phoney.Xunit.Tests;

internal static class Seeds
{
    /// <summary>The first name a faker with <paramref name="seed"/> produces, the same as the facade inside a run seeded with it.</summary>
    public static string FirstNameFor(long seed) => new Faker(Fake.Locale, seed).Person.FirstName();
}

/// <summary>[FakeSeed] sits next to [Fact], [Theory] and [FakeCheck] and seeds the facade for the test.</summary>
public sealed class FakeSeedAttributeTests
{
    [Fact, FakeSeed(42)]
    public void A_pinned_seed_seeds_the_facade() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));

    [Fact, FakeSeed]
    public void Without_a_number_the_facade_is_seeded_too() => Fake.IsSeeded.ShouldBeTrue();

    [Fact, FakeSeed(42)]
    public async Task The_seed_stays_across_awaits()
    {
        await Task.Yield();
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));
    }

    [Theory, FakeSeed(42)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_row_of_a_theory_is_seeded(int row)
    {
        row.ShouldBeGreaterThan(0);
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));
    }

    [FakeCheck(Count = 3, Seed = 1), FakeSeed(42)]
    public void A_check_runs_inside_the_seed(int row)
    {
        row.GetType().ShouldBe(typeof(int));
        Fake.IsSeeded.ShouldBeTrue();
    }

    [Fact]
    public void Tests_without_the_attribute_are_left_alone()
    {
        Fake.IsSeeded.ShouldBeFalse();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [Fact]
    public void The_seed_is_optional_and_readable_and_the_report_offers_the_attribute()
    {
        new FakeSeedAttribute().Seed.ShouldBeNull();
        new FakeSeedAttribute(7).Seed.ShouldBe(7);
        FakeSeedAttribute.PinHint.ShouldBe("[FakeSeed({0})]");
    }
}

/// <summary>The attribute on a class applies to its tests; on a method it wins, and only one run is open.</summary>
[FakeSeed(7)]
public sealed class ClassLevelFakeSeedTests
{
    [Fact]
    public void The_class_seed_applies() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(7));

    [Fact, FakeSeed(8)]
    public void The_method_seed_wins()
    {
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(8));
        FakeSeedRun.Current!.Seed.ShouldBe(8);
    }
}
