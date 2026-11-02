namespace Phoney.NUnit.Tests;

internal static class Seeds
{
    /// <summary>The first name a faker with <paramref name="seed"/> produces, the same as the facade inside a run seeded with it.</summary>
    public static string FirstNameFor(long seed) => new Faker(Fake.Locale, seed).Person.FirstName();
}

/// <summary>[FakeSeed] sits next to [Test], [TestCase] and [FakeCheck] and seeds the facade for the test.</summary>
public sealed class FakeSeedAttributeTests
{
    [Test, FakeSeed(42)]
    public void A_pinned_seed_seeds_the_facade() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));

    [Test, FakeSeed]
    public void Without_a_number_the_facade_is_seeded_too() => Fake.IsSeeded.ShouldBeTrue();

    [Test, FakeSeed(42)]
    public async Task The_seed_stays_across_awaits()
    {
        await Task.Yield();
        FakeSeedRun.Current!.Seed.ShouldBe(42);
    }

    [TestCase(1), TestCase(2), FakeSeed(42)]
    public void Every_case_is_seeded(int row)
    {
        row.ShouldBeGreaterThan(0);
        FakeSeedRun.Current!.Seed.ShouldBe(42);
    }

    [FakeCheck(Count = 3, Seed = 1), FakeSeed(42)]
    public void A_check_runs_inside_the_seed(int row)
    {
        row.GetType().ShouldBe(typeof(int));
        FakeSeedRun.Current!.Seed.ShouldBe(42);
    }

    [Test]
    public void Tests_without_the_attribute_are_left_alone()
    {
        Fake.IsSeeded.ShouldBeFalse();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [Test]
    public void The_seed_is_optional_and_readable_and_the_report_offers_the_attribute()
    {
        new FakeSeedAttribute().Seed.ShouldBeNull();
        new FakeSeedAttribute(7).Seed.ShouldBe(7);
        FakeSeedAttribute.PinHint.ShouldBe("[FakeSeed({0})]");
    }
}

/// <summary>The attribute on a class applies to its tests, set-up included; on a method it wins.</summary>
[FakeSeed(7)]
public sealed class ClassLevelFakeSeedTests
{
    private long? _seedInSetUp;

    [SetUp]
    public void RecordSetUp() => _seedInSetUp = FakeSeedRun.Current?.Seed;

    [Test]
    public void The_set_up_runs_inside_the_class_seed() => _seedInSetUp.ShouldBe(7);

    [Test]
    public void The_class_seed_applies() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(7));

    [Test, FakeSeed(8)]
    public void The_method_seed_wins()
    {
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(8));
        FakeSeedRun.Current!.Seed.ShouldBe(8);
    }
}
