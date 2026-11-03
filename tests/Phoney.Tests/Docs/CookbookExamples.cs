using System.ComponentModel.DataAnnotations;

namespace Phoney.Tests.Docs;

// Inside the namespace, so the guides' model wins over the test models of Phoney.Tests.
using Phoney.Docs.Shop;

/// <summary>The recipes of docs/cookbook.md, run as tests.</summary>
public sealed class CookbookExamples
{
    [Test]
    public void An_aggregate_whose_keys_join_up()
    {
        // snippet: recipe-aggregate
        var customers = Fake.For<Customer>().Seed(1)
            .CollectionSize(1, 4)
            .WithParentKey(c => c.Orders.Each().CustomerId, c => c.Id)                    // order.CustomerId == customer.Id
            .WithParentKey(c => c.Orders.Each().Lines.Each().OrderId, (Order o) => o.Id)   // line.OrderId == its order's Id
            .WithBackReferences()                                                          // order.Customer is the customer itself
            .Generate(50);
        // end snippet

        foreach (var customer in customers)
        {
            foreach (var order in customer.Orders)
            {
                order.CustomerId.ShouldBe(customer.Id);
                order.Customer.ShouldBeSameAs(customer);
                order.Lines.ShouldAllBe(l => l.OrderId == order.Id);
            }
        }
    }

    [Test]
    public void Lookups_and_weighted_choices()
    {
        // snippet: recipe-lookups
        var products = Fake.For<Product>().Seed(2).Generate(20);

        var orders = Fake.For<Order>().Seed(3)
            .WithOneOf(o => o.Lines.Each().Sku, products, p => p.Sku)          // every line sells a product that exists
            .WithOneOf(o => o.Status, (OrderStatus.Paid, 70), (OrderStatus.Shipped, 25), (OrderStatus.Cancelled, 5))
            .Generate(1_000);
        // end snippet

        var skus = products.Select(p => p.Sku).ToHashSet();
        orders.SelectMany(o => o.Lines).ShouldAllBe(l => skus.Contains(l.Sku));
        orders.ShouldNotContain(o => o.Status == OrderStatus.Pending);
        orders.Count(o => o.Status == OrderStatus.Paid).ShouldBeInRange(600, 800);
    }

    [Test]
    public void A_validator_against_every_way_to_break_the_annotations()
    {
        // snippet: recipe-validator
        static bool IsValid(object value) =>
            Validator.TryValidateObject(value, new ValidationContext(value), null, validateAllProperties: true);

        foreach (var broken in Fake.For<Signup>().Seed(1).InvalidCases())       // "Age: below the minimum (18)"…
            IsValid(broken.Value).ShouldBeFalse(broken.ToString());

        foreach (var edge in Fake.For<Signup>().Seed(1).BoundaryCases())        // "Name: the maximum length (40)"…
            IsValid(edge.Value).ShouldBeTrue(edge.ToString());
        // end snippet

        Fake.For<Signup>().Seed(1).InvalidCases().Select(c => c.ToString()).ShouldContain("Age: below the minimum (18)");
        Fake.For<Signup>().Seed(1).BoundaryCases().Select(c => c.ToString()).ShouldContain("Name: the maximum length (40)");
    }

    [Test]
    public void A_property_check_that_shrinks()
    {
        var error = Should.Throw<FakeCheckException<Order>>(() =>
        {
            // snippet: recipe-property
            Fake.For<Order>().Seed(5).CollectionSize(0, 6)
                .Check(o => o.Total < 2_000);       // throws FakeCheckException<Order> with the simplest order of 2,000 or more
            // end snippet
        });

        error.Counterexample.Total.ShouldBeGreaterThanOrEqualTo(2_000);
        error.Counterexample.Lines.Count.ShouldBeLessThanOrEqualTo(error.Original.Lines.Count);
    }

    [Test]
    [NotInParallel(nameof(Employee))] // the configuration is app-wide
    public void A_type_configured_once_for_the_whole_suite()
    {
        try
        {
            // snippet: recipe-configure
            // once, at startup: a module initializer or a test hook that runs before discovery
            Fake.Generators.Configure<Employee>(g => g
                .With(e => e.Department, f => f.Random.Element(["Sales", "Support", "Research"]))
                .Unique(e => e.Email));

            var team = Fake.Many<Employee>(10);      // Fake.One, Many, nested members and [FakeData] parameters all use it
            // end snippet

            team.ShouldAllBe(e => e.Department == "Sales" || e.Department == "Support" || e.Department == "Research");
            team.Select(e => e.Email).ShouldBeUnique();
        }
        finally
        {
            Fake.Generators.Remove<Employee>();
        }
    }

    [Test]
    public void A_strict_generator()
    {
        // snippet: recipe-strict
        var signups = Fake.For<Signup>().Strict();   // fails (and PHONEY004 warns at compile time) once a member gets no rule, convention or annotation
        // end snippet

        signups.Generate(10).Count.ShouldBe(10);
    }

    [Test]
    public void A_fixture_file_read_back_as_a_feed()
    {
        // snippet: recipe-fixture
        string csv = Fake.For<Customer>().Seed(1).ToCsv(100, columns: ["Id", "FirstName", "LastName", "Email"]);
        // … edit it by hand, check it in, then fill those members from it while the rest is generated:
        var customers = Fake.For<Customer>().WithFeed(DataFeed.FromCsv(csv)).Generate(100);
        // end snippet

        var original = Fake.For<Customer>().Seed(1).Generate(100);
        customers.Select(c => c.Email).ShouldBe(original.Select(c => c.Email));
    }

    [Test]
    public void Stand_ins_for_production_data()
    {
        List<Customer> production =
        [
            new() { Id = 1, FirstName = "Anna", Email = "anna@example.com" },
            new() { Id = 2, FirstName = "Anna", Email = "anna@example.com" },
            new() { Id = 3, FirstName = "Björn", Email = "bjorn@example.com" },
        ];

        // snippet: recipe-pseudonymize
        var masked = Fake.For<Customer>()
            .Ignore(c => c.Id)                                  // keep what isn't personal
            .Pseudonymize(production, c => c.Email);            // one real email, one fake person, in every table and every run
        // end snippet

        masked.Select(c => c.Id).ShouldBe([1, 2, 3]);
        masked[1].Email.ShouldBe(masked[0].Email);
        masked[0].Email.ShouldNotBe("anna@example.com");
        masked[2].Email.ShouldNotBe(masked[0].Email);
    }
}
