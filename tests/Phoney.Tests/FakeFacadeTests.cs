using System.Reflection;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>The static facade is a thin layer over <see cref="Fake.Faker"/>: every module, the randomness and templates.</summary>
public sealed class FakeFacadeTests
{
    /// <summary>The facade's module and randomness properties, by name.</summary>
    public static IEnumerable<string> FacadeModules() =>
        typeof(Fake).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType.Namespace == typeof(PersonModule).Namespace || p.PropertyType == typeof(SeededRandom))
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);

    [Test]
    public void The_facade_has_every_module_of_a_faker()
    {
        var modules = typeof(Faker).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.Namespace == typeof(PersonModule).Namespace || p.PropertyType == typeof(SeededRandom))
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);

        FacadeModules().ShouldBe(modules);
    }

    [Test]
    [MethodDataSource(nameof(FacadeModules))]
    public void A_facade_module_is_the_module_of_the_scoped_faker(string name)
    {
        using var _ = Fake.Seeded(1, "sv");

        var facade = typeof(Fake).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        var faker = typeof(Faker).GetProperty(name)!.GetValue(Fake.Faker);

        facade.ShouldBeSameAs(faker);
        Fake.Faker.Locale.ShouldBe("sv");
    }

    [Test]
    public void Facade_values_are_the_values_of_a_faker_with_the_same_seed()
    {
        var expected = new Faker("de", seed: 77);

        using var _ = Fake.Seeded(77, "de");

        Fake.Animal.Dog().ShouldBe(expected.Animal.Dog());
        expected.Animal.Dog().ShouldBe("Catahoula Leopard Dog"); // and the values themselves are the ones this seed has always given
        Fake.Animal.Dog().ShouldBe("Catahoula Leopard Dog");
        Fake.Vehicle.Fuel().ShouldBe(expected.Vehicle.Fuel());
        Fake.Random.Int(0, 1000).ShouldBe(expected.Random.Int(0, 1000));
        Fake.Parse("{{person.firstName}} {{location.city}}").ShouldBe(expected.Parse("{{person.firstName}} {{location.city}}"));
        Fake.Parse("no template").ShouldBe("no template");
        Should.Throw<ArgumentNullException>(() => Fake.Parse(null!));
    }

    [Test]
    [NotInParallel] // changes the process-wide facade locale
    public void The_facade_locale_is_normalized_validated_and_used_by_new_fakers()
    {
        var previous = Fake.Locale;
        try
        {
            Fake.Locale = "sv-SE";

            Fake.Locale.ShouldBe("sv");
            Fake.Faker.Locale.ShouldBe("sv"); // the thread's faker is replaced
            Fake.For<Customer>().Seed(1).Generate().ShouldNotBeNull();
            Should.Throw<ArgumentException>(() => Fake.Locale = "xx").Message.ShouldContain("Available locales");
            Fake.Locale.ShouldBe("sv"); // a rejected value changes nothing
        }
        finally
        {
            Fake.Locale = previous;
        }

        Fake.Faker.Locale.ShouldBe(previous);
    }
}
