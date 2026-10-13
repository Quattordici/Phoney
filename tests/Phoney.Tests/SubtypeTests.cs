using System.Text.Json.Serialization;
using Phoney.Generation;

namespace Phoney.Tests;

// Reflection-only types: no [FakeFor] reaches them, so these tests exercise the reflection model.
public interface IShape;

public sealed class Circle : IShape
{
    public double Radius { get; set; }
    public string Color { get; set; } = "";
}

public sealed class Square : IShape
{
    public double Side { get; set; }
}

public interface IUnregistered;

[JsonDerivedType(typeof(Cat), "cat")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Pet
{
    public string Name { get; set; } = "";
}

public sealed class Cat : Pet
{
    public int Lives { get; set; }
}

public sealed class Dog : Pet
{
    public string Breed { get; set; } = "";
}

public class Vehicle
{
    public string Model { get; set; } = "";
}

public sealed class Truck : Vehicle
{
    public int Axles { get; set; }
}

public sealed class Drawing
{
    public string Title { get; set; } = "";
    public IShape Main { get; set; } = null!;
    public List<IShape> Shapes { get; set; } = [];
    public Pet? Pet { get; set; }
    public IUnregistered? Other { get; set; }
    public Vehicle? Vehicle { get; set; }
}

public interface IGlobalShape;

public sealed class Triangle : IGlobalShape
{
    public double Base { get; set; }
}

public sealed class Hexagon : IGlobalShape
{
    public double Side { get; set; }
}

public sealed class Board
{
    public IGlobalShape Shape { get; set; } = null!;
}

public sealed class SubtypeTests
{
    [Test]
    public void Interfaces_without_subtypes_stay_null()
    {
        var drawing = Fake.For<Drawing>().Seed(1).Generate();

        drawing.Main.ShouldBeNull();
        drawing.Other.ShouldBeNull();
        drawing.Shapes.ShouldAllBe(s => s == null);
        drawing.Vehicle.ShouldBeOfType<Vehicle>(); // a concrete base class is generated as itself
    }

    [Test]
    public void A_single_subtype_is_used_for_every_member_of_the_base_type()
    {
        var drawing = Fake.For<Drawing>().Seed(1).Subtype<IShape, Circle>().Generate();

        var circle = drawing.Main.ShouldBeOfType<Circle>();
        circle.Color.ShouldNotBeNullOrWhiteSpace();
        drawing.Shapes.ShouldNotBeEmpty();
        drawing.Shapes.ShouldAllBe(s => s is Circle);
    }

    [Test]
    public void One_of_several_subtypes_is_picked_reproducibly()
    {
        var generator = Fake.For<Drawing>().Subtype<IShape, Circle, Square>();

        var first = generator.Seed(3).Generate(40);
        var second = generator.Seed(3).Generate(40);

        first.Select(d => d.Main.GetType()).ShouldBe(second.Select(d => d.Main.GetType()));
        first.Select(d => d.Main).OfType<Circle>().ShouldNotBeEmpty();
        first.Select(d => d.Main).OfType<Square>().ShouldNotBeEmpty();
    }

    [Test]
    public void Concrete_base_classes_can_be_mapped_to_a_subtype()
    {
        Fake.For<Drawing>().Seed(1).Subtype<Vehicle, Truck>().NullProbability(0).Generate()
            .Vehicle.ShouldBeOfType<Truck>();
    }

    [Test]
    public void JsonDerivedType_lists_the_default_subtypes()
    {
        var pets = Fake.For<Drawing>().Seed(2).Generate(40).Select(d => d.Pet).ToList();

        pets.ShouldAllBe(p => p is Cat || p is Dog);
        pets.OfType<Cat>().ShouldNotBeEmpty();
        pets.OfType<Dog>().ShouldNotBeEmpty();
        pets.ShouldAllBe(p => p!.Name.Length > 0);
    }

    [Test]
    public void Generator_subtypes_override_JsonDerivedType()
    {
        Fake.For<Drawing>().Seed(2).Subtype<Pet, Dog>().Generate(10).ShouldAllBe(d => d.Pet is Dog);
    }

    [Test]
    [NotInParallel] // changes the app-wide subtypes
    public void Global_subtypes_apply_to_generators_compiled_afterwards_and_generators_override_them()
    {
        try
        {
            Fake.Subtypes.Add<IGlobalShape, Triangle>();
            Fake.For<Board>().Generate().Shape.ShouldBeOfType<Triangle>();

            Fake.Subtypes.Add<IGlobalShape, Hexagon>();
            var shapes = Fake.For<Board>().Seed(1).Generate(30).Select(b => b.Shape).ToList();
            shapes.OfType<Triangle>().ShouldNotBeEmpty();
            shapes.OfType<Hexagon>().ShouldNotBeEmpty();

            Fake.For<Board>().Subtype<IGlobalShape, Hexagon>().Generate(10).ShouldAllBe(b => b.Shape is Hexagon);
        }
        finally
        {
            Fake.Subtypes.Remove<IGlobalShape>();
        }

        Fake.For<Board>().Generate().Shape.ShouldBeNull();
    }

    [Test]
    public void A_member_can_use_a_generator_of_a_subtype()
    {
        var drawing = Fake.For<Drawing>().Seed(1).With(d => d.Main, Fake.For<Square>().With(s => s.Side, 2.5)).Generate();

        drawing.Main.ShouldBeOfType<Square>().Side.ShouldBe(2.5);
    }

    [Test]
    public void Populate_fills_an_existing_subtype_as_itself()
    {
        var drawing = Fake.For<Drawing>().Seed(4).Populate(new Drawing { Main = new Circle { Radius = 2 } });

        var circle = drawing.Main.ShouldBeOfType<Circle>();
        circle.Radius.ShouldBe(2);
        circle.Color.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Invalid_subtypes_are_rejected_with_a_helpful_message()
    {
        Should.Throw<ArgumentException>(() => Fake.For<Drawing>().Subtype<IShape>(typeof(string)))
            .Message.ShouldContain("is not a IShape");
        Should.Throw<ArgumentException>(() => Fake.For<Drawing>().Subtype<Pet>(typeof(Pet)))
            .Message.ShouldContain("concrete");
        Should.Throw<ArgumentException>(() => Fake.For<Drawing>().Subtype<IShape>());
        Should.Throw<ArgumentException>(() => Fake.Subtypes.Add<IShape>(typeof(IShape)));
    }

    [Test]
    public void Arguments_use_global_and_default_subtypes()
    {
        var row = new ArgumentGenerator([new FakeArgument("pet", typeof(Pet))]).Seed(1).Generate();

        row[0].ShouldBeAssignableTo<Pet>();
    }
}
