namespace Phoney.Tests;

// Reflection-only types: no [FakeFor] reaches them, so these tests exercise the reflection model.
public sealed class Trip
{
    public string Name { get; set; } = "";
    public Stopover Start { get; set; } = null!;
    public List<Stopover> Stops { get; set; } = [];
    public Dictionary<string, Stopover> ByName { get; set; } = [];
    public Segment? First { get; set; }
    public IShape? Marker { get; set; }
}

public sealed class Stopover
{
    public string Place { get; set; } = "";
    public int Night { get; set; }
}

public sealed class Segment
{
    public Stopover To { get; set; } = null!;
    public int Km { get; set; }
}

public sealed class Mentee
{
    public string Name { get; set; } = "";
    public Mentee? Mentor { get; set; }
}

/// <summary><c>Use</c> replaces the generator of a type everywhere it is nested in the generated objects.</summary>
public sealed class TypeGeneratorTests
{
    private static ObjectGenerator<Stopover> Oslo() => Fake.For<Stopover>().With(s => s.Place, "Oslo");

    [Test]
    public void Applies_to_members_collection_elements_and_dictionary_values()
    {
        var trips = Fake.For<Trip>().Seed(1).Use(Oslo()).Generate(10);

        trips.ShouldAllBe(t => t.Start.Place == "Oslo");
        trips.SelectMany(t => t.Stops).ShouldNotBeEmpty();
        trips.SelectMany(t => t.Stops).ShouldAllBe(s => s.Place == "Oslo");
        trips.SelectMany(t => t.ByName.Values).ShouldNotBeEmpty();
        trips.SelectMany(t => t.ByName.Values).ShouldAllBe(s => s.Place == "Oslo");
    }

    [Test]
    public void Applies_at_any_depth_of_the_graph()
    {
        var trips = Fake.For<Trip>().Seed(2).Use(Oslo()).Generate(10);

        trips.ShouldAllBe(t => t.First!.To.Place == "Oslo"); // Trip → Segment → Stopover
    }

    [Test]
    public void Objects_without_the_type_are_generated_as_before()
    {
        var plain = Fake.For<Trip>().Seed(3).Generate(5);
        var used = Fake.For<Trip>().Seed(3).Use(Oslo()).Generate(5);

        used.Select(t => t.Name).ShouldBe(plain.Select(t => t.Name)); // drawn before any Stopover
    }

    [Test]
    public void Is_reproducible_with_a_seed()
    {
        ObjectGenerator<Trip> Configure() =>
            Fake.For<Trip>().Seed(4).Use(Fake.For<Stopover>().With(s => s.Place, f => f.Location.City()));

        string Describe(Trip t) => $"{t.Name}|{t.Start.Place}|{t.Start.Night}|{string.Join(",", t.Stops.Select(s => s.Place + s.Night))}|{t.First!.To.Place}|{t.First.Km}";

        Configure().Generate(10).Select(Describe).ShouldBe(Configure().Generate(10).Select(Describe));
        Configure().GenerateParallel(10).Select(Describe).ShouldBe(Configure().Generate(10).Select(Describe));
    }

    [Test]
    public void Generators_stay_immutable()
    {
        var plain = Fake.For<Trip>().Seed(5);
        var used = plain.Use(Oslo());

        used.ShouldNotBeSameAs(plain);
        plain.Generate(20).ShouldAllBe(t => t.Start.Place != "Oslo");
        used.Generate(20).ShouldAllBe(t => t.Start.Place == "Oslo");
    }

    [Test]
    public void A_later_registration_for_the_same_type_replaces_the_earlier_one()
    {
#pragma warning disable PHONEY008 // replacing the earlier registration is what this test is about
        var trip = Fake.For<Trip>().Use(Oslo()).Use(Fake.For<Stopover>().With(s => s.Place, "Rome")).Generate();
#pragma warning restore PHONEY008

        trip.Start.Place.ShouldBe("Rome");
    }

    [Test]
    public void A_rule_for_the_member_wins_over_the_type_generator()
    {
        var trip = Fake.For<Trip>()
            .Use(Oslo())
            .With(t => t.Start, Fake.For<Stopover>().With(s => s.Place, "Bergen"))
            .Generate();

        trip.Start.Place.ShouldBe("Bergen");
        trip.Stops.ShouldAllBe(s => s.Place == "Oslo");
    }

    [Test]
    public void Ignored_members_stay_untouched()
    {
        var trip = Fake.For<Trip>().Use(Oslo()).Ignore(t => t.Start).Generate();

        trip.Start.ShouldBeNull();
        trip.Stops.ShouldAllBe(s => s.Place == "Oslo");
    }

    [Test]
    public void The_generator_brings_its_own_rules_and_settings()
    {
        var trip = Fake.For<Trip>().Seed(6).Use(Fake.For<Stopover>().Locale("sv").With(s => s.Night, f => f.Random.Int(100, 200))).Generate();

        trip.Start.Night.ShouldBeInRange(100, 200);
        trip.Stops.ShouldAllBe(s => s.Night >= 100 && s.Night <= 200);
    }

    [Test]
    public void A_nested_object_reaches_the_type_generators_of_the_whole_graph_and_the_innermost_wins()
    {
        var shared = Fake.For<Trip>().Use(Oslo()).Use(Fake.For<Segment>().With(s => s.Km, 5));
        var own = Fake.For<Trip>().Use(Oslo()).Use(Fake.For<Segment>().Use(Fake.For<Stopover>().With(s => s.Place, "Rome")));

        var trip = shared.Generate();
        trip.First!.Km.ShouldBe(5);
        trip.First.To.Place.ShouldBe("Oslo"); // from the outer Use, although the segment has its own generator

        var other = own.Generate();
        other.Start.Place.ShouldBe("Oslo");
        other.First!.To.Place.ShouldBe("Rome");
    }

    [Test]
    public void Cycles_still_stop_with_null()
    {
        var mentee = Fake.For<Mentee>().Use(Fake.For<Mentee>().With(m => m.Name, "Nested")).Generate();

        mentee.Mentor.ShouldBeNull(); // the root is a Mentee already
        mentee.Name.ShouldNotBe("Nested"); // Use never applies to the generated object itself
    }

    [Test]
    public void The_depth_limit_still_applies()
    {
        var trip = Fake.For<Trip>().MaxDepth(0).Use(Oslo()).Generate();

        trip.Start.ShouldBeNull();
    }

    [Test]
    public void An_interface_can_use_the_generator_of_a_subtype_without_a_subtype_registration()
    {
        var trips = Fake.For<Trip>().Seed(7).Use<IShape, Circle>(Fake.For<Circle>().With(c => c.Radius, 2.5)).Generate(10);

        trips.ShouldAllBe(t => t.Marker is Circle);
        trips.ShouldAllBe(t => ((Circle)t.Marker!).Radius == 2.5);
    }

    [Test]
    public void The_type_generator_wins_over_a_subtype_of_the_same_type()
    {
        var trip = Fake.For<Trip>()
            .Subtype<IShape, Square>()
            .Use<IShape, Circle>(Fake.For<Circle>())
            .Generate();

        trip.Marker.ShouldBeOfType<Circle>();
    }

    [Test]
    public void The_generator_of_a_subtype_applies_to_the_subtype_picked_for_a_base_type()
    {
        var trips = Fake.For<Trip>()
            .Seed(8)
            .Subtype<IShape, Circle, Square>()
            .Use(Fake.For<Circle>().With(c => c.Radius, 9))
            .Generate(40);

        var circles = trips.Select(t => t.Marker).OfType<Circle>().ToList();
        circles.ShouldNotBeEmpty();
        circles.ShouldAllBe(c => c.Radius == 9);
        trips.Select(t => t.Marker).OfType<Square>().ShouldNotBeEmpty();
    }

    [Test]
    public void Populate_creates_missing_nested_objects_with_the_type_generator()
    {
        var trip = Fake.For<Trip>().Seed(9).Use(Oslo()).Populate(new Trip { Name = "Kept" });

        trip.Name.ShouldBe("Kept");
        trip.Start.Place.ShouldBe("Oslo");
        trip.Stops.ShouldAllBe(s => s.Place == "Oslo");
    }

    [Test]
    public void Unique_members_of_the_type_generator_stay_unique_across_the_graph()
    {
        var trips = Fake.For<Trip>().Seed(10).CollectionSize(2, 2)
            .Use(Fake.For<Stopover>().With(s => s.Night, f => f.Random.Int(1, 100_000)).Unique(s => s.Night))
            .Generate(20);

        var nights = trips.SelectMany(t => t.Stops.Select(s => s.Night).Append(t.Start.Night)).ToList();
        nights.Distinct().Count().ShouldBe(nights.Count);
    }

    [Test]
    public void Missing_generators_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Fake.For<Trip>().Use<Stopover>(null!));
        Should.Throw<ArgumentNullException>(() => Fake.For<Trip>().Use<IShape, Circle>(null!));
    }

    [Test]
    public void Scalar_types_are_rejected_because_they_are_not_nested_objects()
    {
        // Scalars come from rules and conventions, never from a nested generator, so a registration would silently do nothing.
        Should.Throw<ArgumentException>(() => Fake.For<Trip>().Use(Fake.For<int>()))
            .Message.ShouldContain("not a nested object");
        Should.Throw<ArgumentException>(() => Fake.For<Trip>().Use(Fake.For<DateOnly>()))
            .Message.ShouldContain("WithConvention");
    }
}
