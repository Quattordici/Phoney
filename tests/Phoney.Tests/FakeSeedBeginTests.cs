using System.Globalization;
using System.Text.RegularExpressions;

namespace Phoney.Tests;

/// <summary>
/// <c>FakeSeed.Begin</c> is the one seeded test run every test-framework adapter builds on: it seeds the facade, says how to replay a failure, and when
/// runs nest (assembly, class and method attributes) only the innermost one that saw the failure reports it. <c>PHONEY_SEED</c> is process-wide.
/// </summary>
[NotInParallel]
public sealed class FakeSeedBeginTests
{
    private const string Pin = "[FakeSeed({0})]";
    private static readonly Regex RandomSeed = new(@"random seed (\d+)", RegexOptions.None, TimeSpan.FromSeconds(5));

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

    private static string FirstNameFor(long seed) => new Faker(Fake.Locale, seed).Person.FirstName();

    private static List<string> Fail(FakeSeedRun run)
    {
        var report = new List<string>();
        run.Failed(report.Add);
        return report;
    }

    [Test]
    public void A_run_seeds_the_facade_until_it_is_disposed()
    {
        using (var run = FakeSeed.Begin(42, Pin))
        {
            run.Seed.ShouldBe(42);
            Fake.Person.FirstName().ShouldBe(FirstNameFor(42));
            FakeSeedRun.Current.ShouldBeSameAs(run);
        }

        Fake.IsSeeded.ShouldBeFalse();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [Test]
    public void A_pinned_seed_is_named_as_pinned_and_says_nothing_about_replaying()
    {
        using var run = FakeSeed.Begin(42, Pin);

        Fail(run).ShouldBe(["Phoney: this test ran with the pinned seed 42."]);
    }

    [Test]
    public void Without_a_seed_PHONEY_SEED_is_used_and_named() =>
        WithEnvironment("99", () =>
        {
            using var run = FakeSeed.Begin(pinHint: Pin);

            run.Seed.ShouldBe(99);
            Fail(run).ShouldBe(["Phoney: this test ran with seed 99 from PHONEY_SEED. Replay it with PHONEY_SEED=99."]);
        });

    [Test]
    public void A_pinned_seed_wins_over_PHONEY_SEED() =>
        WithEnvironment("99", () =>
        {
            using var run = FakeSeed.Begin(42, Pin);
            run.Seed.ShouldBe(42);
        });

    [Test]
    public void Otherwise_a_random_seed_is_reported_with_the_pin_that_replays_it() =>
        WithEnvironment(null, () =>
        {
            string seen;
            List<string> report;
            using (var run = FakeSeed.Begin(pinHint: Pin))
            {
                seen = Fake.Person.FirstName();
                report = Fail(run);
            }

            var line = report.ShouldHaveSingleItem();
            var seed = long.Parse(RandomSeed.Match(line).Groups[1].Value, CultureInfo.InvariantCulture);
            line.ShouldBe($"Phoney: this test ran with random seed {seed}. Replay it with [FakeSeed({seed})] or PHONEY_SEED={seed}.");
            FirstNameFor(seed).ShouldBe(seen);
        });

    [Test]
    public void Without_a_pin_hint_the_report_only_names_PHONEY_SEED() =>
        WithEnvironment(null, () =>
        {
            using var run = FakeSeed.Begin();

            Fail(run).ShouldHaveSingleItem().ShouldBe($"Phoney: this test ran with random seed {run.Seed}. Replay it with PHONEY_SEED={run.Seed}.");
        });

    [Test]
    public void Random_seeds_differ_between_runs() =>
        WithEnvironment(null, () =>
        {
            var seeds = Enumerable.Range(0, 20).Select(_ =>
            {
                using var run = FakeSeed.Begin();
                return run.Seed;
            }).ToList();

            seeds.Distinct().Count().ShouldBeGreaterThan(15);
        });

    [Test]
    public void End_reports_a_failure_and_ends_the_run()
    {
        var report = new List<string>();
        var outer = FakeSeed.Begin(1, Pin);
        FakeSeed.Begin(2, Pin);

        FakeSeedRun.Current!.End(failed: true, report.Add);
        FakeSeedRun.Current.ShouldBeSameAs(outer);
        FakeSeedRun.Current!.End(failed: true, report.Add);

        report.ShouldBe(["Phoney: this test ran with the pinned seed 2."]);
        FakeSeedRun.Current.ShouldBeNull();
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public void End_of_a_passing_test_reports_nothing()
    {
        var report = new List<string>();
        FakeSeed.Begin(1, Pin).End(failed: false, report.Add);

        report.ShouldBeEmpty();
        FakeSeedRun.Current.ShouldBeNull();
    }

    [Test]
    public void A_failure_is_reported_once()
    {
        using var run = FakeSeed.Begin(5, Pin);
        var report = new List<string>();

        run.Failed(report.Add);
        run.Failed(report.Add);

        report.Count.ShouldBe(1);
    }

    [Test]
    public void The_innermost_run_seeds_the_facade_and_is_current()
    {
        using var outer = FakeSeed.Begin(1, Pin);
        using (var inner = FakeSeed.Begin(2, Pin))
        {
            FakeSeedRun.Current.ShouldBeSameAs(inner);
            Fake.Person.FirstName().ShouldBe(FirstNameFor(2));
        }

        FakeSeedRun.Current.ShouldBeSameAs(outer);
    }

    [Test]
    public void When_an_inner_run_reported_the_failure_the_runs_around_it_stay_quiet()
    {
        var report = new List<string>();
        using var assembly = FakeSeed.Begin(1, Pin);
        using var type = FakeSeed.Begin(2, Pin);
        using (var method = FakeSeed.Begin(3, Pin))
            method.Failed(report.Add);

        type.Failed(report.Add);
        assembly.Failed(report.Add);

        report.ShouldBe(["Phoney: this test ran with the pinned seed 3."]);
    }

    [Test]
    public void An_inner_run_that_passed_leaves_the_report_to_the_run_around_it()
    {
        var report = new List<string>();
        using var outer = FakeSeed.Begin(1, Pin);
        using (FakeSeed.Begin(2, Pin))
        {
        }

        outer.Failed(report.Add);

        report.ShouldBe(["Phoney: this test ran with the pinned seed 1."]);
    }

    [Test]
    public void Runs_disposed_out_of_order_never_bring_back_one_that_ended()
    {
        var outer = FakeSeed.Begin(1, Pin);
        var inner = FakeSeed.Begin(2, Pin);

        outer.Dispose();
        FakeSeedRun.Current.ShouldBeSameAs(inner);
        inner.Dispose();

        FakeSeedRun.Current.ShouldBeNull();
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public async Task Parallel_flows_have_their_own_runs()
    {
        var names = await Task.WhenAll(Enumerable.Range(1, 8).Select(async seed =>
        {
            await Task.Yield();
            using var run = FakeSeed.Begin(seed, Pin);
            await Task.Delay(1);
            FakeSeedRun.Current.ShouldBeSameAs(run);
            return Fake.Person.FirstName();
        }));

        names.ShouldBe(Enumerable.Range(1, 8).Select(seed => FirstNameFor(seed)));
    }

    [Test]
    public void Arguments_are_validated_and_a_bad_PHONEY_SEED_is_reported()
    {
        Should.Throw<ArgumentException>(() => FakeSeed.Begin(1, "[FakeSeed]")).Message.ShouldContain("{0}");
        using (var run = FakeSeed.Begin(1, Pin))
            Should.Throw<ArgumentNullException>(() => run.Failed(null!)).ParamName.ShouldBe("report");
        WithEnvironment("nope", () => Should.Throw<FormatException>(() => FakeSeed.Begin()));
        FakeSeedRun.Current.ShouldBeNull();
    }
}
