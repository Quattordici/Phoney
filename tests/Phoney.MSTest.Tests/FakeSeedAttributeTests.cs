using System.Reflection;

namespace Phoney.MSTest.Tests;

internal static class Seeds
{
    /// <summary>The first name a faker with <paramref name="seed"/> produces, the same as the facade inside a run seeded with it.</summary>
    public static string FirstNameFor(long seed) => new Faker(Fake.Locale, seed).Person.FirstName();
}

/// <summary>[FakeSeed] in place of [TestMethod] seeds the facade for the test, [TestInitialize] included.</summary>
[TestClass]
public sealed class FakeSeedAttributeTests
{
    private long? _seedInInitialize;

    [TestInitialize]
    public void RecordInitialize() => _seedInInitialize = FakeSeedRun.Current?.Seed;

    [FakeSeed(42)]
    public void A_pinned_seed_seeds_the_facade_and_the_initialize_runs_inside_it()
    {
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(42));
        _seedInInitialize.ShouldBe(42);
    }

    [FakeSeed]
    public void Without_a_number_the_facade_is_seeded_too() => Fake.IsSeeded.ShouldBeTrue();

    [FakeSeed(42)]
    public async Task The_seed_stays_across_awaits()
    {
        await Task.Yield();
        FakeSeedRun.Current!.Seed.ShouldBe(42);
    }

    [FakeSeed(42)]
    [DataRow(1)]
    [DataRow(2)]
    public void Every_row_is_seeded(int row)
    {
        row.ShouldBeGreaterThan(0);
        FakeSeedRun.Current!.Seed.ShouldBe(42);
    }

    [TestMethod]
    public void Tests_without_the_attribute_are_left_alone()
    {
        Fake.IsSeeded.ShouldBeFalse();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [TestMethod]
    public void The_seed_is_optional_and_readable()
    {
        new FakeSeedAttribute().Seed.ShouldBeNull();
        new FakeSeedAttribute(7).Seed.ShouldBe(7);
        new FakeSeedClassAttribute().Seed.ShouldBeNull();
        new FakeSeedClassAttribute(7).Seed.ShouldBe(7);
    }

    [TestMethod]
    [DataRow(UnitTestOutcome.Failed)]
    [DataRow(UnitTestOutcome.Error)]
    [DataRow(UnitTestOutcome.Timeout)]
    public async Task A_failed_test_gets_the_seed_in_its_output(UnitTestOutcome outcome)
    {
        var results = await new FakeSeedAttribute(5).ExecuteAsync(new FixedMethod(outcome));

        results.ShouldHaveSingleItem().LogOutput.ShouldBe($"Phoney: this test ran with the pinned seed 5.{Environment.NewLine}");
    }

    [TestMethod]
    public async Task A_random_seed_is_reported_with_the_attribute_that_pins_it()
    {
        var method = new FixedMethod(UnitTestOutcome.Failed);

        var output = (await new FakeSeedAttribute().ExecuteAsync(method)).ShouldHaveSingleItem().LogOutput;

        output.ShouldNotBeNull().ShouldContain($"[FakeSeed({method.Seed})]");
    }

    [TestMethod]
    [DataRow(UnitTestOutcome.Passed)]
    [DataRow(UnitTestOutcome.Inconclusive)]
    [DataRow(UnitTestOutcome.Ignored)]
    public async Task Other_outcomes_print_nothing(UnitTestOutcome outcome)
    {
        var results = await new FakeSeedAttribute(5).ExecuteAsync(new FixedMethod(outcome));

        results.ShouldHaveSingleItem().LogOutput.ShouldBeNull();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [TestMethod]
    public void The_class_attribute_wraps_every_test_attribute_but_leaves_a_method_seed_alone()
    {
        var plain = new TestMethodAttribute();
        var own = new FakeSeedAttribute(8);

        var wrapped = new FakeSeedClassAttribute(7).GetTestMethodAttribute(plain).ShouldBeOfType<FakeSeedAttribute>();

        wrapped.Seed.ShouldBe(7);
        wrapped.DeclaringFilePath.ShouldBe(plain.DeclaringFilePath);
        wrapped.DeclaringLineNumber.ShouldBe(plain.DeclaringLineNumber);
        new FakeSeedClassAttribute(7).GetTestMethodAttribute(own).ShouldBeSameAs(own);
    }

    private static void Shape()
    {
    }

    /// <summary>A test that ends with <paramref name="outcome"/> and records the seed it ran with.</summary>
    private sealed class FixedMethod(UnitTestOutcome outcome) : ITestMethod
    {
        public long? Seed { get; private set; }

        public string TestMethodName => nameof(Shape);
        public string TestClassName => nameof(FakeSeedAttributeTests);
        public Type ReturnType => typeof(void);
        public object?[]? Arguments => null;
        public ParameterInfo[] ParameterTypes => [];
        public MethodInfo MethodInfo => typeof(FakeSeedAttributeTests).GetMethod(nameof(Shape), BindingFlags.NonPublic | BindingFlags.Static)!;

        public Task<TestResult> InvokeAsync(object[]? arguments)
        {
            Seed = FakeSeedRun.Current?.Seed;
            return Task.FromResult(new TestResult { Outcome = outcome });
        }

        public Attribute[]? GetAllAttributes() => [];
        public TAttributeType[] GetAttributes<TAttributeType>() where TAttributeType : Attribute => [];
    }
}

/// <summary>[FakeSeedClass] in place of [TestClass] seeds every test of the class; [FakeSeed] on a method wins.</summary>
[FakeSeedClass(7)]
public sealed class ClassLevelFakeSeedTests
{
    [TestMethod(DisplayName = "The class seed applies (a display name survives the wrapping)")]
    public void The_class_seed_applies() => Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(7));

    [TestMethod]
    [DataRow(1)]
    public void Rows_get_the_class_seed(int row) => (FakeSeedRun.Current!.Seed + row).ShouldBe(8);

    [FakeCheck(Count = 3, Seed = 1)]
    public void A_check_runs_inside_the_class_seed(int row)
    {
        row.GetType().ShouldBe(typeof(int));
        FakeSeedRun.Current!.Seed.ShouldBe(7);
    }

    [FakeSeed(8)]
    public void The_method_seed_wins()
    {
        Fake.Person.FirstName().ShouldBe(Seeds.FirstNameFor(8));
        FakeSeedRun.Current!.Seed.ShouldBe(8);
    }
}
