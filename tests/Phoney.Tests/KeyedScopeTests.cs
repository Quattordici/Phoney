using Phoney.Generation;

namespace Phoney.Tests;

// A type only these tests configure: Fake.Generators is process-wide.
public sealed class Badge
{
    public string Code { get; set; } = "";
}

/// <summary>
/// A key is a seed source like a seed or a fork: <c>Fake.ForKey</c> opens a scope where values come from the key and every type's objects are the
/// key's own, the first one being the <c>GenerateFor</c> stand-in; <c>ObjectGenerator.Keyed</c> is the same route on a generator.
/// </summary>
public sealed class KeyedScopeTests
{
    private const string Key = "customer:42";

    private static string Describe(Patient p) => $"{p.FirstName}|{p.LastName}|{p.Email}|{p.BirthDate:O}|{p.City}";

    [Test]
    public void A_keyed_generator_starts_with_the_GenerateFor_object_in_both_models()
    {
        var reflection = new ObjectGenerator<Patient>(new ReflectionModel<Patient>());

        Describe(PatientFakes.Patient.Keyed(Key).Generate()).ShouldBe(Describe(PatientFakes.Patient.GenerateFor(Key)));
        Describe(reflection.Keyed(Key).Generate()).ShouldBe(Describe(reflection.GenerateFor(Key)));
        Describe(PatientFakes.Patient.Keyed(Key).Generate()).ShouldBe(Describe(reflection.Keyed(Key).Generate()));
    }

    [Test]
    public void A_keyed_generator_continues_with_the_key_s_own_stream()
    {
        var first = PatientFakes.Patient.Keyed(Key).Generate(4).Select(Describe).ToList();

        first[0].ShouldBe(Describe(PatientFakes.Patient.GenerateFor(Key)));
        first.Distinct().Count().ShouldBe(4);
        PatientFakes.Patient.Keyed(Key).Generate(4).Select(Describe).ShouldBe(first);
        new ObjectGenerator<Patient>(new ReflectionModel<Patient>()).Keyed(Key).Generate(4).Select(Describe).ShouldBe(first);
        PatientFakes.Patient.Keyed("customer:43").Generate(4).Select(Describe).ShouldNotContain(first[0]);
    }

    [Test]
    public void A_keyed_generator_is_a_sequence_like_any_other()
    {
        var generator = PatientFakes.Patient.Keyed(Key);
        var stream = PatientFakes.Patient.Keyed(Key).Generate(3).Select(Describe).ToList();

        new[] { generator.Generate(), generator.Generate(), generator.Generate() }.Select(Describe).ShouldBe(stream);
    }

    [Test]
    public void The_generator_s_seed_is_mixed_into_the_key_as_for_GenerateFor()
    {
        var seeded = PatientFakes.Patient.Seed(7);

        Describe(seeded.Keyed(Key).Generate()).ShouldBe(Describe(seeded.GenerateFor(Key)));
        Describe(PatientFakes.Patient.Keyed(Key).Seed(7).Generate()).ShouldBe(Describe(seeded.GenerateFor(Key)));
        Describe(seeded.Keyed(Key).Generate()).ShouldNotBe(Describe(PatientFakes.Patient.Keyed(Key).Generate()));
    }

    [Test]
    public void Counts_and_parallel_generation_follow_the_key()
    {
        var counts = Enumerable.Range(0, 5).Select(_ => PatientFakes.Patient.Keyed(Key).Generate(1, 50).Count).Distinct();
        counts.Count().ShouldBe(1);

        PatientFakes.Patient.Keyed(Key).GenerateParallel(6).Select(Describe).ShouldBe(PatientFakes.Patient.Keyed(Key).Generate(6).Select(Describe));
    }

    [Test]
    public void Populate_on_a_keyed_generator_follows_the_key()
    {
        var kept = new Patient { Id = 5, FirstName = "Ada" };

        var a = PatientFakes.Patient.Keyed(Key).Populate(kept);
        var b = PatientFakes.Patient.Keyed(Key).Populate(new Patient { Id = 5, FirstName = "Ada" });

        (a.FirstName, a.Email).ShouldBe(("Ada", b.Email));
    }

    [Test]
    public void Keys_and_keyed_generators_are_checked()
    {
        Should.Throw<ArgumentNullException>(() => PatientFakes.Patient.Keyed(null!)).ParamName.ShouldBe("key");
        Should.Throw<ArgumentNullException>(() => Fake.ForKey(null!)).ParamName.ShouldBe("key");
        Should.Throw<InvalidOperationException>(() => PatientFakes.Patient.Keyed(Key).Unique(p => p.Email).Generate()).Message.ShouldContain("Unique");
        Should.Throw<ArgumentException>(() => PatientFakes.Patient.WithGeneratorFor(new ObjectGenerator<Address>(new ReflectionModel<Address>()).Keyed(Key)))
            .Message.ShouldContain("Keyed");
    }

    [Test]
    public void Inside_a_keyed_scope_values_come_from_the_key()
    {
        string name;
        using (Fake.ForKey(Key))
        {
            Fake.IsSeeded.ShouldBeTrue();
            name = Fake.Person.FullName();
        }

        name.ShouldBe(Fake.Keyed(Key).Person.FullName());
        Fake.IsSeeded.ShouldBeFalse();
    }

    [Test]
    public void Inside_a_keyed_scope_an_object_is_the_key_s_stand_in()
    {
        var standIn = Describe(Fake.For<Patient>().GenerateFor(Key));

        using var _ = Fake.ForKey(Key);
        Describe(Fake.One<Patient>()).ShouldBe(standIn);
        Fake.Person.FirstName(); // values drawn in between don't move it
        Describe(Fake.One<Patient>()).ShouldBe(standIn);
        Describe(Fake.ForGenerated<Patient>().Generate()).ShouldBe(standIn);
        Fake.Many<Patient>(3).Select(Describe).ShouldBe(PatientFakes.Patient.Keyed(Key).Generate(3).Select(Describe));
        Fake.Populate(new Patient { Id = 9 }).Email.ShouldBe(PatientFakes.Patient.Keyed(Key).Populate(new Patient { Id = 9 }).Email);
    }

    [Test]
    public void A_keyed_scope_can_have_its_own_locale()
    {
        using var _ = Fake.ForKey(Key, "sv");

        Fake.Faker.Locale.ShouldBe("sv");
        Describe(Fake.One<Patient>()).ShouldBe(Describe(PatientFakes.Patient.Locale("sv").GenerateFor(Key)));
    }

    [Test]
    public void The_innermost_scope_decides()
    {
        var standIn = Describe(PatientFakes.Patient.GenerateFor(Key));
        string seeded;
        using (Fake.Seeded(1))
            seeded = Describe(Fake.One<Patient>());

        using (Fake.ForKey(Key))
        {
            using (Fake.Seeded(1))
                Describe(Fake.One<Patient>()).ShouldBe(seeded);
            using (Fake.Fork(3))
            {
                Fake.Person.FullName().ShouldBe(Fake.Keyed(Key).Fork(3).Person.FullName());
                Describe(Fake.One<Patient>()).ShouldNotBe(standIn);
            }

            Describe(Fake.One<Patient>()).ShouldBe(standIn);
        }

        using (Fake.Seeded(1))
        using (Fake.ForKey(Key))
            Describe(Fake.One<Patient>()).ShouldBe(standIn);
    }

    [Test]
    public void Parameters_in_a_keyed_scope_are_reproducible()
    {
        object?[] Row()
        {
            using var _ = Fake.ForKey(Key);
            return Fake.Arguments([new FakeArgument("email", typeof(string)), new FakeArgument("age", typeof(int))]).Row(0);
        }

        Row().ShouldBe(Row());
    }
}

/// <summary>A type configured with <c>Unique</c> can't be generated in a keyed scope, for the reason <c>GenerateFor</c> refuses it.</summary>
[NotInParallel]
public sealed class KeyedScopeUniqueTests
{
    [Test]
    public void A_configured_Unique_rule_is_refused_with_the_GenerateFor_message()
    {
        try
        {
            Fake.Generators.Configure<Badge>(g => g.Unique(b => b.Code));
            using var _ = Fake.ForKey("k");

            Should.Throw<InvalidOperationException>(() => Fake.One<Badge>()).Message.ShouldContain("Unique can't be combined with");
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }
}
