using System.Collections.Concurrent;
using System.Reflection;
using Phoney.Generation;

namespace Phoney.Tests.Adversarial;

public abstract class Contraption
{
    public string Name { get; set; } = "";
}

public sealed class Gizmo : Contraption;

public sealed class Workshop
{
    public Contraption? Contraption { get; set; }
    public string Owner { get; set; } = "";
}

/// <summary>Many threads at once: racing first runs, a generator shared by every thread, scopes nested and disposed out of order, and registries changed mid-generation.</summary>
public sealed class ConcurrencyAbuseTests
{
    private const int Threads = 64;

    [Test]
    public void Racing_first_runs_of_one_generator_hand_out_each_item_of_its_sequence_once()
    {
        // A generator continues its sequence from call to call, so the threads share items 0 to 3199 between them, whoever compiles it first.
        var generator = HostileGraphFakes.Yin.Seed(5);
        var expected = HostileGraphFakes.Yin.Seed(5).Generate(Threads * 50).Select(y => EveryLocaleTests.Describe(y)).Order(StringComparer.Ordinal);
        var results = new ConcurrentBag<string>();

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            foreach (var yin in generator.Generate(50))
                results.Add(EveryLocaleTests.Describe(yin));
        });

        results.Order(StringComparer.Ordinal).ShouldBe(expected);
    }

    [Test]
    public void Unique_values_are_never_handed_out_twice_when_threads_share_a_generator()
    {
        var generator = Fake.For<LocaleProfile>().Unique(x => x.Email);
        var emails = new ConcurrentBag<string>();

        Parallel.For(0, Threads, _ =>
        {
            foreach (var profile in generator.Generate(20))
                emails.Add(profile.Email);
        });

        emails.Count.ShouldBe(Threads * 20);
        emails.Distinct(StringComparer.Ordinal).Count().ShouldBe(emails.Count);
    }

    [Test]
    public void Parallel_generation_gives_what_sequential_generation_gives()
    {
        EveryLocaleTests.Describe(HostileGraphFakes.Yin.Seed(9).GenerateParallel(5_000))
            .ShouldBe(EveryLocaleTests.Describe(HostileGraphFakes.Yin.Seed(9).Generate(5_000)));
    }

    [Test]
    public void Seeded_scopes_on_many_threads_do_not_see_each_other()
    {
        var expected = new Dictionary<int, string>();
        for (var seed = 0; seed < 16; seed++)
        {
            using (Fake.Seeded(seed))
                expected[seed] = Fake.Person.FullName() + Fake.One<Yin>().Name;
        }

        var results = new ConcurrentBag<(int Seed, string Value)>();
        Parallel.For(0, 400, i =>
        {
            var seed = i % 16;
            using (Fake.Seeded(seed))
                results.Add((seed, Fake.Person.FullName() + Fake.One<Yin>().Name));
        });

        results.ShouldAllBe(r => r.Value == expected[r.Seed]);
    }

    [Test]
    public void A_scope_disposed_twice_restores_once()
    {
        using var outer = Fake.Seeded(1);
        var outerFaker = Fake.Faker;
        var inner = Fake.Seeded(2);

        inner.Dispose();
        inner.Dispose();

        Fake.Faker.ShouldBeSameAs(outerFaker);
    }

    [Test]
    public void Scopes_disposed_out_of_order_never_bring_back_a_scope_that_ended()
    {
        var before = Fake.Faker;
        var outer = Fake.Seeded(1);
        var outerFaker = Fake.Faker;
        var inner = Fake.Seeded(2);
        var innerFaker = Fake.Faker;

        outer.Dispose(); // too early: the inner scope is still open, so it stays in charge
        Fake.Faker.ShouldBeSameAs(innerFaker);

        inner.Dispose(); // the outer scope ended already, so this goes back to before both
        Fake.Faker.ShouldNotBeSameAs(outerFaker);
        Fake.Faker.ShouldBeSameAs(before);
        Fake.IsSeeded.ShouldBe(before.IsSeeded);
    }

    [Test]
    public async Task A_task_that_outlives_its_scope_stops_using_the_ended_scope()
    {
        using var outer = Fake.Seeded(1);
        var outerFaker = Fake.Faker;
        var inner = Fake.Seeded(2);
        var innerFaker = Fake.Faker;
        var ended = new TaskCompletionSource();
        var child = Task.Run(async () =>
        {
            await ended.Task; // the task copied the inner scope when it started
            return (Fake.Faker, Fake.IsSeeded);
        });

        inner.Dispose();
        ended.SetResult();
        var (faker, seeded) = await child;

        faker.ShouldNotBeSameAs(innerFaker);
        faker.ShouldBeSameAs(outerFaker);
        seeded.ShouldBeTrue();
    }

    [Test]
    public void Forks_inside_a_seeded_scope_give_each_item_its_own_data_whatever_the_timing()
    {
        string[] Run()
        {
            var values = new string[200];
            using (Fake.Seeded(77))
            {
                Parallel.For(0, values.Length, i =>
                {
                    using (Fake.Fork(i))
                        values[i] = Fake.Person.FullName() + Fake.One<Yin>().Name;
                });
            }

            return values;
        }

        Run().ShouldBe(Run());
    }

    [Test]
    public void Generators_used_inside_rules_of_other_generators_keep_their_own_seeds()
    {
        static string Run()
        {
            var inner = HostileGraphFakes.Yang.Seed(3);
            var outer = HostileGraphFakes.Yin.Seed(4).With(x => x.Name, _ => inner.Generate().Name + Fake.One<Yang>().Name);
            using (Fake.Seeded(8))
                return EveryLocaleTests.Describe(outer.Generate(10));
        }

        Run().ShouldBe(Run());
    }
}

/// <summary>App-wide registries changed while other threads generate: no exception, and every object is one the old or the new registrations make.</summary>
[NotInParallel]
public sealed class RegistryRaceTests
{
    private static void Restoring<TRegistry>(Action test)
    {
        var field = typeof(TRegistry).GetField("s_all", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = field.GetValue(null);
        try
        {
            test();
        }
        finally
        {
            field.SetValue(null, saved);
        }
    }

    [Test]
    public void Subtypes_added_and_removed_during_generation_never_break_it() =>
        Restoring<FakeSubtypes>(() =>
        {
            using var stop = new CancellationTokenSource();
            var churn = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    Fake.Subtypes.Add<Contraption, Gizmo>();
                    Fake.Subtypes.Remove<Contraption>();
                }
            });
            try
            {
                Parallel.For(0, 2_000, _ => (Fake.One<Workshop>().Contraption is null or Gizmo).ShouldBeTrue());
            }
            finally
            {
                stop.Cancel();
                churn.Wait();
            }
        });

    [Test]
    public void Conventions_added_and_cleared_during_generation_never_break_it() =>
        Restoring<FakeConventions>(() =>
        {
            using var stop = new CancellationTokenSource();
            var churn = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    Fake.Conventions.Add<string>("Owner", _ => "convention");
                    Fake.Conventions.Clear();
                }
            });
            try
            {
                Parallel.For(0, 2_000, _ => Fake.One<Workshop>().Owner.ShouldNotBeNullOrWhiteSpace());
            }
            finally
            {
                stop.Cancel();
                churn.Wait();
            }
        });
}
