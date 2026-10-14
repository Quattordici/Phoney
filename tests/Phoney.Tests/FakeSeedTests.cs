namespace Phoney.Tests;

/// <summary>Picking seeds for tests that run on random data but can be replayed. <c>PHONEY_SEED</c> is process-wide, so these run one at a time.</summary>
[NotInParallel]
public sealed class FakeSeedTests
{
    private static void WithEnvironment(string? value, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(FakeSeed.EnvironmentVariable);
        Environment.SetEnvironmentVariable(FakeSeed.EnvironmentVariable, value);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(FakeSeed.EnvironmentVariable, previous);
        }
    }

    [Test]
    public void The_environment_variable_is_called_PHONEY_SEED() => FakeSeed.EnvironmentVariable.ShouldBe("PHONEY_SEED");

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public void No_seed_in_the_environment_gives_null(string? value) =>
        WithEnvironment(value, () => FakeSeed.FromEnvironment().ShouldBeNull());

    [Test]
    [Arguments("42", 42L)]
    [Arguments(" 7 ", 7L)]
    [Arguments("-5", -5L)]
    [Arguments("9223372036854775807", long.MaxValue)]
    public void A_whole_number_in_the_environment_is_the_seed(string value, long expected) =>
        WithEnvironment(value, () => FakeSeed.FromEnvironment().ShouldBe(expected));

    [Test]
    [Arguments("abc")]
    [Arguments("1.5")]
    [Arguments("0x2A")]
    [Arguments("99999999999999999999")]
    public void Anything_else_is_rejected_and_the_message_says_what_is_expected(string value) =>
        WithEnvironment(value, () =>
        {
            var exception = Should.Throw<FormatException>(() => FakeSeed.FromEnvironment());
            exception.Message.ShouldContain("PHONEY_SEED");
            exception.Message.ShouldContain(value);
            exception.Message.ShouldContain("whole number");
        });

    [Test]
    public void Pick_uses_the_environment_when_it_is_set() =>
        WithEnvironment("123", () => FakeSeed.Pick().ShouldBe(123));

    [Test]
    public void Pick_is_a_new_seed_otherwise() =>
        WithEnvironment(null, () =>
        {
            var seeds = Enumerable.Range(0, 20).Select(_ => FakeSeed.Pick()).ToList();

            seeds.Distinct().Count().ShouldBeGreaterThan(15);
            seeds.ShouldAllBe(s => s >= 1 && s < 1_000_000_000); // short enough to read out and type
        });

    [Test]
    public void New_ignores_the_environment() =>
        WithEnvironment("123", () => Enumerable.Range(0, 20).Select(_ => FakeSeed.New()).Distinct().Count().ShouldBeGreaterThan(15));

    [Test]
    public void A_seed_replays_the_same_data()
    {
        var seed = FakeSeed.New();

        string Describe()
        {
            using var _ = Fake.Seeded(seed);
            return Fake.Person.FullName() + Fake.Internet.Email();
        }

        Describe().ShouldBe(Describe());
    }
}
