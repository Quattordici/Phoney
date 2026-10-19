using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Filling in objects that already exist, with the reflection model (<see cref="Customer"/>) and the source-generated one (<see cref="Member"/>).</summary>
public sealed class PopulateTests
{
    private static readonly DateTimeOffset Reference = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Customer> Customers => Fake.For<Customer>().Seed(7).ReferenceDate(Reference);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    [Test]
    public void Keeps_what_is_set_and_fills_the_rest()
    {
        var customer = new Customer { Id = 42, FirstName = "Anna", Tags = ["vip"], CreatedAt = new DateTime(2020, 2, 2, 0, 0, 0, DateTimeKind.Utc) };

        var result = Customers.Populate(customer);

        result.ShouldBeSameAs(customer);
        (customer.Id, customer.FirstName, customer.CreatedAt.Year).ShouldBe((42, "Anna", 2020));
        customer.Tags.ShouldBe(["vip"]);
        customer.LastName.ShouldNotBeNullOrEmpty();
        customer.Email.ShouldNotBeNullOrEmpty();
        customer.HomeAddress.ShouldNotBeNull();
        customer.Orders.ShouldNotBeEmpty();
        customer.ExternalId.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public void Generated_values_agree_with_the_values_that_are_kept()
    {
        var customer = Customers.Populate(new Customer { FirstName = "Anna", DateOfBirth = new DateOnly(1990, 5, 5) });

        customer.Email.ShouldContain("anna", Case.Insensitive);
        customer.Age.ShouldBe(34); // at the reference date 2025-01-01
    }

    [Test]
    public void All_replaces_every_member_that_is_not_ignored()
    {
        var customer = new Customer { Id = 42, FirstName = "Anna", Email = "keep@example.test" };

        Customers.Ignore(c => c.Email).Populate(customer, PopulateMode.All);

        customer.Id.ShouldBe(1);
        customer.FirstName.ShouldNotBe("Anna");
        customer.Email.ShouldBe("keep@example.test"); // ignored
    }

    [Test]
    public void Rules_decide_the_values_and_the_mode_decides_which_members()
    {
        var generator = Customers.With(c => c.FirstName, "Grace");

        generator.Populate(new Customer()).FirstName.ShouldBe("Grace");
        generator.Populate(new Customer { FirstName = "Anna" }).FirstName.ShouldBe("Anna");
        generator.Populate(new Customer { FirstName = "Anna" }, PopulateMode.All).FirstName.ShouldBe("Grace");
    }

    [Test]
    public void Ignored_members_are_not_touched()
    {
        var customer = Customers.Ignore(c => c.Email).Ignore(c => c.HomeAddress).Populate(new Customer());

        customer.Email.ShouldBe("");
        customer.HomeAddress.ShouldBeNull();
        customer.LastName.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void Dependent_rules_and_AfterCreate_run_afterwards_and_obey_the_mode()
    {
        var generator = Customers
            .With(c => c.Email, (f, c) => c.FirstName.ToLowerInvariant() + "@example.test")
            .AfterCreate((f, c) => c.Tags.Add("populated"));

        var empty = generator.Populate(new Customer { FirstName = "Anna" });
        empty.Email.ShouldBe("anna@example.test");
        empty.Tags.ShouldContain("populated");

        generator.Populate(new Customer { FirstName = "Anna", Email = "mine@example.test" }).Email.ShouldBe("mine@example.test");

        // All regenerates the first name as well, so the email follows the new name.
        var all = generator.Populate(new Customer { FirstName = "Anna", Email = "mine@example.test" }, PopulateMode.All);
        all.Email.ShouldBe(all.FirstName.ToLowerInvariant() + "@example.test");
    }

    [Test]
    public void A_set_nested_object_is_populated_member_by_member()
    {
        var order = new Order { Customer = new Customer { FirstName = "Bo" } };

        Fake.For<Order>().Seed(3).Populate(order);

        order.Customer!.FirstName.ShouldBe("Bo");
        order.Customer.LastName.ShouldNotBeNullOrEmpty();
        order.Id.ShouldNotBe(0);        // init-only member
        order.Total.ShouldNotBe(0m);
        order.Lines.ShouldNotBeEmpty(); // init-only collection
    }

    [Test]
    public void A_missing_nested_object_is_created()
    {
        var order = Fake.For<Order>().Seed(3).Populate(new Order());

        order.Customer.ShouldNotBeNull();
        order.Customer.HomeAddress.ShouldNotBeNull();
    }

    [Test]
    public void Positional_record_members_can_be_filled_in()
    {
        var address = Fake.For<Address>().Seed(2).Populate(new Address("Main Street 1", "", "", ""));

        address.Street.ShouldBe("Main Street 1");
        address.City.ShouldNotBeNullOrEmpty();
        address.ZipCode.ShouldNotBeNullOrEmpty();
        address.Country.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void A_struct_is_populated_as_a_copy()
    {
        var original = new Point { Latitude = 12.5 };

        var result = Fake.For<Point>().Seed(2).Populate(original);

        original.Longitude.ShouldBe(0);
        result.Latitude.ShouldBe(12.5);
        result.Longitude.ShouldNotBe(0);
    }

    [Test]
    public void Populating_an_empty_object_with_All_gives_what_generating_gives()
    {
        Json(Customers.Populate(new Customer(), PopulateMode.All)).ShouldBe(Json(Customers.Generate()));
        Json(Customers.Populate(new Customer())).ShouldBe(Json(Customers.Generate()));
        Json(TestFakes.Member.Seed(3).Populate(new Member { Username = "" }, PopulateMode.All)).ShouldBe(Json(TestFakes.Member.Seed(3).Generate()));
    }

    [Test]
    public void Populating_many_objects_follows_the_seeded_sequence()
    {
        var populated = Customers.Populate(Enumerable.Range(0, 4).Select(_ => new Customer()).ToList());

        Json(populated).ShouldBe(Json(Customers.Generate(4)));
    }

    [Test]
    public void The_same_seed_populates_the_same_values()
    {
        string Populate()
        {
            using var _ = Fake.Seeded(5);
            return Json(Fake.Populate(new Customer { FirstName = "Anna" }));
        }

        Populate().ShouldBe(Populate());
    }

    [Test]
    public void Generated_and_reflection_models_populate_identically()
    {
        Member Partial() => new()
        {
            Username = "",
            FirstName = "Anna",
            Balance = 12.5m,
            Tags = ["a"],
            Address = new MemberAddress("Main Street 1", "", ""),
        };

        var generated = TestFakes.Member.Seed(5).ReferenceDate(Reference).Populate(Partial());
        var reflected = new ObjectGenerator<Member>(new ReflectionModel<Member>()).Seed(5).ReferenceDate(Reference).Populate(Partial());

        Json(reflected).ShouldBe(Json(generated));
        generated.FirstName.ShouldBe("Anna");
        generated.Balance.ShouldBe(12.5m);
        generated.Tags.ShouldBe(["a"]);
        generated.Address.Street.ShouldBe("Main Street 1");
        generated.Address.City.ShouldNotBeNullOrEmpty();
        generated.Username.ShouldNotBeNullOrEmpty();
        generated.Email.ShouldContain("anna", Case.Insensitive);
    }

    [Test]
    public void Generated_models_populate_structs_with_init_members()
    {
        var generated = TestFakes.Coordinates.Seed(1).Populate(new Coordinates { Latitude = 1.5 });
        var reflected = new ObjectGenerator<Coordinates>(new ReflectionModel<Coordinates>()).Seed(1).Populate(new Coordinates { Latitude = 1.5 });

        generated.Latitude.ShouldBe(1.5);
        generated.Longitude.ShouldNotBe(0);
        Json(generated).ShouldBe(Json(reflected));
    }

    [Test]
    public void A_null_object_or_element_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => Customers.Populate((Customer)null!));
        Should.Throw<ArgumentException>(() => Customers.Populate(new List<Customer> { new(), null! }));
    }

    [Test]
    public void A_null_element_is_rejected_before_any_object_is_changed_or_the_sequence_moves()
    {
        var generator = Customers;
        var first = new Customer();

        Should.Throw<ArgumentException>(() => generator.Populate(new List<Customer> { first, null! }));

        Json(first).ShouldBe(Json(new Customer()));
        Json(generator.Populate(new Customer())).ShouldBe(Json(Customers.Populate(new Customer())));
    }

    [Test]
    public void Members_without_a_convention_are_filled_by_type()
    {
        var unconventional = Fake.For<Unconventional>().Seed(1).Populate(new Unconventional { Foo = "x" });

        unconventional.Foo.ShouldBe("x");
        unconventional.Email.ShouldContain("@");
        unconventional.Bar.ShouldNotBe(0);
    }
}
