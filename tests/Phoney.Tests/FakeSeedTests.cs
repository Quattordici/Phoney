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

    private static string FirstNameFor(long seed)
    {
        using var _ = Fake.Seeded(seed);
        return Fake.Person.FirstName();
    }

    [Test]
    public void Run_executes_the_body_on_the_given_seed_and_reports_nothing_when_it_passes()
    {
        var report = new List<string>();
        string? seen = null;

        FakeSeed.Run(() => seen = Fake.Person.FirstName(), report.Add, seed: 31);

        seen.ShouldBe(FirstNameFor(31));
        report.ShouldBeEmpty();
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public void Run_reports_the_seed_of_a_failure_and_rethrows_the_same_exception()
    {
        var report = new List<string>();
        var failure = new InvalidOperationException("boom");

        var thrown = Should.Throw<InvalidOperationException>(() => FakeSeed.Run(() => throw failure, report.Add, seed: 31));

        thrown.ShouldBeSameAs(failure);
        report.Count.ShouldBe(1);
        report[0].ShouldContain("31");
        report[0].ShouldContain("pinned");
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public void Run_without_a_seed_uses_PHONEY_SEED_and_says_how_to_replay_it() =>
        WithEnvironment("77", () =>
        {
            var report = new List<string>();
            string? seen = null;

            Should.Throw<Exception>(() => FakeSeed.Run(() =>
            {
                seen = Fake.Person.FirstName();
                throw new Exception("x");
            }, report.Add));

            seen.ShouldBe(FirstNameFor(77));
            report.ShouldHaveSingleItem().ShouldContain("PHONEY_SEED=77");
        });

    [Test]
    public void Run_without_a_seed_or_environment_picks_a_random_seed_that_the_report_gives_back() =>
        WithEnvironment(null, () =>
        {
            var report = new List<string>();
            string? seen = null;

            Should.Throw<Exception>(() => FakeSeed.Run(() =>
            {
                seen = Fake.Person.FirstName();
                throw new Exception("x");
            }, report.Add));

            var line = report.ShouldHaveSingleItem();
            line.ShouldContain("random seed");
            var seed = long.Parse(System.Text.RegularExpressions.Regex.Match(line, @"random seed (\d+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            seen.ShouldBe(FirstNameFor(seed));
            line.ShouldContain($"PHONEY_SEED={seed}");
        });

    [Test]
    public async Task RunAsync_keeps_the_seed_across_awaits_and_ends_the_scope()
    {
        var report = new List<string>();
        var names = new List<string>();

        await FakeSeed.RunAsync(async () =>
        {
            names.Add(Fake.Person.FirstName());
            await Task.Delay(5);
            names.Add(Fake.Person.FirstName());
        }, report.Add, seed: 8);

        using (Fake.Seeded(8))
            names.ShouldBe([Fake.Person.FirstName(), Fake.Person.FirstName()]);
        report.ShouldBeEmpty();
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public async Task RunAsync_reports_a_failure_and_rethrows_it()
    {
        var report = new List<string>();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => FakeSeed.RunAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("late");
        }, report.Add, seed: 12));

        thrown.Message.ShouldBe("late");
        report.ShouldHaveSingleItem().ShouldContain("12");
    }

    [Test]
    public void Run_validates_its_arguments()
    {
        Should.Throw<ArgumentNullException>(() => FakeSeed.Run(null!, _ => { })).ParamName.ShouldBe("body");
        Should.Throw<ArgumentNullException>(() => FakeSeed.Run(() => { }, null!)).ParamName.ShouldBe("report");
        Should.Throw<ArgumentNullException>(() => FakeSeed.RunAsync(null!, _ => { })).ParamName.ShouldBe("body");
        Should.Throw<ArgumentNullException>(() => FakeSeed.RunAsync(() => Task.CompletedTask, null!)).ParamName.ShouldBe("report");
    }

    [Test]
    public void Run_with_a_malformed_environment_fails_before_running_the_body() =>
        WithEnvironment("nope", () =>
        {
            var ran = false;
            Should.Throw<FormatException>(() => FakeSeed.Run(() => ran = true, _ => { }));
            ran.ShouldBeFalse();
        });
}
