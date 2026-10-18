using System.Collections.Concurrent;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Patient
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public string City { get; set; } = "";
}

[FakeFor<Patient>]
public static partial class PatientFakes;

/// <summary><c>Fork</c> gives parallel work reproducible fakers of its own; <c>Keyed</c>, <c>GenerateFor</c> and <c>Pseudonymize</c> tie data to a key.</summary>
public sealed class ForkAndKeyedTests
{
    [Test]
    public void Parallel_work_in_a_seeded_scope_is_reproducible_with_a_fork_per_item()
    {
        string[] Run()
        {
            using var _ = Fake.Seeded(42);
            var names = new string[2_000];
            Parallel.For(0, names.Length, i =>
            {
                using var fork = Fake.Fork(i);
                names[i] = Fake.Person.FullName() + " " + Fake.One<Patient>().Email;
            });
            return names;
        }

        var first = Run();
        Run().ShouldBe(first);

        using (Fake.Seeded(42))
        {
            using var fork = Fake.Fork(7);
            (Fake.Person.FullName() + " " + Fake.One<Patient>().Email).ShouldBe(first[7]); // item 7 is the same on any thread
        }
    }

    [Test]
    public void A_fork_depends_on_the_seed_and_index_only()
    {
        var faker = new Faker(seed: 5);
        var before = faker.Fork(3).Person.FullName();
        for (var i = 0; i < 100; i++)
            faker.Person.FirstName(); // drawing from the parent doesn't move its forks

        faker.Fork(3).Person.FullName().ShouldBe(before);
        faker.Fork(4).Person.FullName().ShouldNotBe(before);
        new Faker(seed: 6).Fork(3).Person.FullName().ShouldNotBe(before);
    }

    [Test]
    public void A_fork_keeps_the_locale_and_dates()
    {
        var faker = new Faker("sv", seed: 1) { ReferenceDate = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero) };

        var fork = faker.Fork(1);

        fork.Locale.ShouldBe("sv");
        fork.ReferenceDate.ShouldBe(faker.ReferenceDate);
    }

    [Test]
    public void A_fork_of_an_unseeded_facade_is_not_seeded()
    {
        using var fork = Fake.Fork(1);

        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public void Forked_scopes_restore_the_previous_faker()
    {
        using var _ = Fake.Seeded(3);
        var outer = Fake.Faker;
        using (Fake.Fork(1))
            Fake.Faker.ShouldNotBeSameAs(outer);

        Fake.Faker.ShouldBeSameAs(outer);
    }

    [Test]
    [Arguments("", -4359066618775142608L)]
    [Arguments("customer:42", 8481663777693073963L)]
    [Arguments("åäö-€", 8338240418431350636L)]
    public void Key_seeds_are_pinned(string key, long expected)
    {
        // Values computed independently in Python (FNV-1a over UTF-16LE bytes, then SplitMix64): changing them would move every keyed value.
        SeededRandom.KeySeed(key).ShouldBe(expected);
    }

    [Test]
    public void A_key_gives_the_same_values_whatever_the_seed()
    {
        var name = new Faker(seed: 1).Keyed("customer:42").Person.FullName();

        new Faker(seed: 99).Keyed("customer:42").Person.FullName().ShouldBe(name);
        new Faker().Keyed("customer:42").Person.FullName().ShouldBe(name);
        Fake.Keyed("customer:42").Person.FullName().ShouldBe(name);
        Fake.Keyed("customer:43").Person.FullName().ShouldNotBe(name);
    }

    [Test]
    public void Keyed_dates_do_not_follow_the_clock()
    {
        new Faker().Keyed("k").ReferenceDate.ShouldBe(Faker.DefaultSeededReferenceDate);
    }

    [Test]
    public void GenerateFor_gives_one_object_per_key()
    {
        var generator = Fake.For<Patient>();
        var first = generator.GenerateFor("patient-1");
        generator.Generate(10); // other objects don't move it

        var again = Fake.For<Patient>().GenerateFor("patient-1");

        again.Email.ShouldBe(first.Email);
        again.BirthDate.ShouldBe(first.BirthDate);
        Fake.For<Patient>().GenerateFor("patient-2").Email.ShouldNotBe(first.Email);
        Fake.For<Patient>().Seed(1).GenerateFor("patient-1").Email.ShouldNotBe(first.Email); // a seed changes the whole mapping
    }

    [Test]
    public void GenerateFor_is_the_same_in_both_models()
    {
        var reflection = new ObjectGenerator<Patient>(new ReflectionModel<Patient>()).Seed(2);
        var generated = PatientFakes.Patient.Seed(2);

        foreach (var key in new[] { "a", "b", "c" })
        {
            var r = reflection.GenerateFor(key);
            var g = generated.GenerateFor(key);
            (g.FirstName, g.Email, g.BirthDate, g.City).ShouldBe((r.FirstName, r.Email, r.BirthDate, r.City));
        }
    }

    [Test]
    public void Pseudonymize_maps_the_same_real_value_to_the_same_stand_in()
    {
        var real = new List<Patient>
        {
            new() { Id = 1, FirstName = "Anna", Email = "anna@real.se" },
            new() { Id = 2, FirstName = "Bo", Email = "bo@real.se" },
            new() { Id = 3, FirstName = "Anna", Email = "anna@real.se" }, // the same person in another table
        };

        var masked = Fake.For<Patient>().Seed(7).Ignore(p => p.Id).Pseudonymize(real, p => p.Email);

        masked.Select(p => p.Id).ShouldBe([1, 2, 3]);
        masked.ShouldAllBe(p => !p.Email.EndsWith("@real.se", StringComparison.Ordinal));
        masked[2].Email.ShouldBe(masked[0].Email);
        masked[2].FirstName.ShouldBe(masked[0].FirstName);
        masked[1].Email.ShouldNotBe(masked[0].Email);

        var single = Fake.For<Patient>().Seed(7).Ignore(p => p.Id).Pseudonymize(new Patient { Id = 9, Email = "anna@real.se" }, p => p.Email);
        single.Email.ShouldBe(masked[0].Email);
    }

    [Test]
    public void Keys_must_not_be_null()
    {
        Should.Throw<ArgumentNullException>(() => Fake.Keyed(null!));
        Should.Throw<ArgumentNullException>(() => Fake.For<Patient>().GenerateFor(null!));
        Should.Throw<ArgumentNullException>(() => Fake.For<Patient>().Pseudonymize(new Patient(), _ => null!));
    }

    [Test]
    public void Keyed_generation_is_thread_safe()
    {
        var generator = Fake.For<Patient>().Seed(3);
        var results = new ConcurrentDictionary<int, string>();
        Parallel.For(0, 500, i => results[i] = generator.GenerateFor("k" + (i % 50)).Email);

        for (var i = 50; i < 500; i++)
            results[i].ShouldBe(results[i % 50]);
    }
}
