namespace Phoney.Tests;

/// <summary>Collections, interfaces and abstract classes asked for directly (<c>Fake.One&lt;List&lt;Customer&gt;&gt;()</c>) are produced like members of that type.</summary>
public sealed class RootTypeTests
{
    [Test]
    public void A_collection_asked_for_directly_has_generated_elements()
    {
        var customers = Fake.For<List<Customer>>().Seed(1).Generate();
        customers.Count.ShouldBeInRange(1, 3);
        customers.ShouldAllBe(c => c.Email.Contains('@'));

        Fake.For<Dictionary<string, int>>().Seed(1).Generate().ShouldNotBeEmpty();
        Fake.For<Customer[]>().Seed(1).Generate().ShouldNotBeEmpty();
        Fake.For<IReadOnlyList<Guid>>().Seed(1).Generate().ShouldAllBe(g => g != Guid.Empty);
    }

    [Test]
    public void A_collection_follows_the_generators_collection_size()
    {
        Fake.For<List<string>>().Seed(2).CollectionSize(5, 5).Generate().Count.ShouldBe(5);
    }

    [Test]
    public void A_collection_asked_for_directly_is_reproducible()
    {
        var first = Fake.For<List<Customer>>().Seed(3).Generate(4).Select(l => string.Join(",", l.Select(c => c.Email)));
        var again = Fake.For<List<Customer>>().Seed(3).Generate(4).Select(l => string.Join(",", l.Select(c => c.Email)));
        again.ShouldBe(first);
    }

    [Test]
    public void An_interface_asked_for_directly_is_one_of_its_subtypes()
    {
        var shapes = Fake.For<IShape>().Seed(4).Subtype<IShape, Circle, Square>().Generate(30);

        shapes.ShouldAllBe(s => s is Circle || s is Square);
        shapes.OfType<Circle>().ShouldNotBeEmpty();
        shapes.OfType<Square>().ShouldNotBeEmpty();
        shapes.OfType<Circle>().ShouldAllBe(c => c.Color.Length > 0);
    }

    [Test]
    public void An_abstract_class_asked_for_directly_uses_its_JsonDerivedType_subtypes()
    {
        var pets = Fake.For<Pet>().Seed(5).Generate(20);

        pets.ShouldAllBe(p => (p is Cat || p is Dog) && p.Name.Length > 0);
    }

    [Test]
    public void An_interface_without_subtypes_explains_how_to_register_one()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Fake.One<IUnregistered>());

        exception.Message.ShouldContain("IUnregistered");
        exception.Message.ShouldContain("Subtype<IUnregistered,");
        exception.Message.ShouldContain("Fake.Subtypes.Add<IUnregistered,");
        exception.Message.ShouldNotContain("public constructor");
    }
}
