namespace Phoney.Tests.Docs;

// Inside the namespace, so the README's model wins over the test models of Phoney.Tests.
using Phoney.Modules;
using Phoney.Tests.Docs.Readme;

/// <summary>
/// The README's examples that touch no app-wide state, run as tests. Those that register conventions, subtypes, locales or
/// facade settings for the whole process are in <see cref="ReadmeCompiledExamples"/>: compiled, never run, so they can't change other tests' data.
/// </summary>
public sealed class ReadmeExamples
{
    [Test]
    public void Quick_start()
    {
        // snippet: readme-quick-start
        // Values
        Fake.Person.FullName();                         // "Madisen Collins"
        Fake.Internet.Email();                          // "Ryann_Beahan@yahoo.com"
        Fake.Location.StreetAddress(useFullAddress: true);

        // A whole, coherent person: names, email, username, phone, birthdate and age, address, national id
        PersonProfile p = Fake.Person.Profile();

        // A specific locale and a seed
        var faker = new Faker("sv", seed: 42);
        faker.Person.FullName();                        // "Madeleine Öberg Nordström"
        faker.Finance.Iban("SE", formatted: true);      // valid check digits

        // Objects
        Customer customer = Fake.One<Customer>();
        List<Customer> customers = Fake.Many<Customer>(1_000);
        List<Customer> someCustomers = Fake.Many<Customer>(3, 10);   // a count from 3 to 10, chosen from the seed
        // end snippet

        p.Age.ShouldBeGreaterThan(0);
        var again = new Faker("sv", seed: 42);
        again.Person.FullName().ShouldBe("Madeleine Öberg Nordström");
        customers.Count.ShouldBe(1_000);
        someCustomers.Count.ShouldBeInRange(3, 10);
        customer.Email.ShouldNotBeEmpty();
    }

    /// <summary>The values the README's national id example (compiled only, it registers a scheme) shows in its comments.</summary>
    [Test]
    public void Seeded_national_ids()
    {
        var faker = new Faker("sv", seed: 1);
        faker.NationalId.Person().ShouldBe("19761015-5736");
        faker.NationalId.Person("NO", birthDate: new DateOnly(1990, 5, 17), sex: Sex.Female, formatted: false);
        faker.NationalId.Company().ShouldBe("538599-9650");
        faker.NationalId.Vat("GB").ShouldBe("GB 904 0044 78");

        NationalIds.IsValid(NationalIdKind.Person, "SE", "19761015-5736").ShouldBeTrue();
        NationalIds.Supported(NationalIdKind.Vat).ShouldBe(["DK", "FI", "FR", "GB", "NO", "PL", "SE"]);
    }

    [Test]
    public void Objects()
    {
        // snippet: readme-objects
        var customers = Fake.For<Customer>()
            .Locale("sv")
            .Seed(42)                                                     // reproducible
            .With(x => x.LoyaltyPoints, f => f.Random.Int(0, 5_000))      // value from a factory
            .With(x => x.Status, CustomerStatus.Active)                    // fixed value
            .With(x => x.Email, (f, x) => f.Internet.Email(x.FirstName, x.LastName, provider: "example.se")) // depends on the object
            .With(x => x.Address, Fake.For<Address>().Locale("de"))        // nested generator
            .Unique(x => x.Email)                                          // never the same email twice
            .Ignore(x => x.InternalNotes)
            .Generate(100);                                                // also: Generate(), Stream(), GenerateParallel(n)
        // end snippet

        customers.ShouldAllBe(c => c.Status == CustomerStatus.Active && c.InternalNotes == null && c.Email.EndsWith("@example.se"));
        customers.Select(c => c.Email).ShouldBeUnique();
    }

    [Test]
    public void One_of_and_subsets()
    {
        var customers = Fake.For<Customer>().Seed(1).Generate(10);
        string[] countries = ["Sweden", "Norway", "Denmark"];
        double[] populations = [10.5, 5.5, 5.9];
        SalesRep[] reps = [new(1, 2), new(2, 8)];
        string[] roles = ["admin", "editor", "viewer"];
        string[] tags = ["new", "vip", "churned", "trial"];
        string[] badges = ["bronze", "silver", "gold", "platinum"];

        // snippet: readme-one-of
        var orders = Fake.For<Order>()
            .WithOneOf(o => o.CustomerId, customers, c => c.Id)                                                        // an existing customer's id
            .WithOneOf(o => o.Status, (OrderStatus.Paid, 70), (OrderStatus.Shipped, 25), (OrderStatus.Cancelled, 5))   // 70 %, 25 %, 5 %
            .WithOneOf(o => o.Country, countries, populations)                                                         // values and weights
            .WithOneOf(o => o.SalesRepId, reps, r => r.Id, r => r.Seniority)                                             // existing objects, weighted
            .Generate(1000);

        var users = Fake.For<User>().Seed(1)
            .WithSubsetOf(u => u.Roles, roles, 2)                                // two of the roles, never the same one twice
            .WithSubsetOf(u => u.Tags, tags, (1, 3))                             // one to three; arrays, sets and list interfaces work
            .WithSubsetOf(u => u.Badges, badges, new[] { 80.0, 15, 4, 1 }, 2)   // heavier values are more likely to be in it
            .Generate(100);
        // end snippet

        orders.ShouldAllBe(o => customers.Any(c => c.Id == o.CustomerId) && countries.Contains(o.Country) && o.Status != OrderStatus.Pending);
        users.ShouldAllBe(u => u.Roles.Count == 2 && u.Tags.Length >= 1 && u.Tags.Length <= 3 && u.Badges.Count == 2);
    }

    [Test]
    public void Rules_for_nested_members()
    {
        // snippet: readme-path-rules
        var invoices = Fake.For<Invoice>()
            .With(i => i.Billing.City, "Oslo")                          // this address only: Shipping is another position
            .With(i => i.Billing.Contact.Phone, f => f.Random.ReplaceSymbols("+47 ## ## ## ##"))
            .With(i => i.Lines.Each().Sku, f => f.Commerce.Ean13())     // every element of a list, array or set
            .With(i => i.ByCode.Values.Each().Quantity, 1)              // every value of a dictionary
            .Ignore(i => i.Lines.Each().Supplier!.Email)
            .Invalid(i => i.Lines.Each().Quantity)                      // see "Invalid and boundary data"
            .Generate(10);
        // end snippet

        invoices.ShouldAllBe(i => i.Billing.City == "Oslo" && i.Billing.Contact.Phone.StartsWith("+47 "));
        invoices.SelectMany(i => i.ByCode.Values).ShouldAllBe(l => l.Quantity == 1);
        invoices.SelectMany(i => i.Lines).ShouldAllBe(l => l.Sku.Length == 13 && (l.Quantity < 1 || l.Quantity > 10));
    }

    [Test]
    public void Back_references()
    {
        // snippet: readme-back-references
        var orders = Fake.For<Order>().WithBackReferences().Generate(10);
        var parent = orders[0].Lines[0].Order;   // the very same order (ReferenceEquals), not null
        // end snippet

        parent.ShouldBeSameAs(orders[0]);
    }

    [Test]
    public void Parent_keys()
    {
        // snippet: readme-parent-keys
        var customers = Fake.For<Customer>()
            .WithParentKey(c => c.Orders.Each().CustomerId, c => c.Id)
            .WithParentKey(c => c.Orders.Each().Lines.Each().OrderId, (Order o) => o.Id)   // the nearest Order above each line
            .Generate(100);
        // end snippet

        customers.ShouldAllBe(c => c.Orders.All(o => o.CustomerId == c.Id && o.Lines.All(l => l.OrderId == o.Id)));
    }

    [Test]
    public void Conditions()
    {
        // snippet: readme-where
        var bigOrders = Fake.For<Order>().Seed(1)
            .Where(o => o.Total > 100)                                   // the whole object is generated again until it holds
            .Where(o => o.Status != OrderStatus.Cancelled)               // several conditions must all hold
            .Where(o => o.Reference, r => !r.StartsWith("TEST"))         // only Reference is drawn again
            .Generate(50);
        // end snippet

        bigOrders.ShouldAllBe(o => o.Total > 100 && o.Status != OrderStatus.Cancelled && !o.Reference.StartsWith("TEST"));
    }

    [Test]
    public void Verified_rules()
    {
        // snippet: readme-verify-rules
        var invoices = Fake.For<Invoice>().Seed(1)
            .With(i => i.Billing.City, "Oslo")
            .Subtype<IPayment, Card>()
            .WithConvention<string>(n => n.EndsWith("Code"), f => f.Random.ReplaceSymbols("??-###"))
            .VerifyRules()
            .Generate(20);      // a rule that applied to none of the 20 invoices throws an InvalidOperationException naming it
        // end snippet

        invoices.ShouldAllBe(i => i.Payment is Card && i.PromoCode.Length == 6);
    }

    [Test]
    public void Normal_distribution()
    {
        var faker = new Faker(seed: 1);

        // snippet: readme-normal
        var people = Fake.For<Person>().Seed(1)
            .WithNormalDistribution(p => p.Height, mean: 175, standardDeviation: 8)             // 184.81, 171.03, 184.82…
            .WithNormalDistribution(p => p.Iq, 100, 15)                                         // whole numbers for int members
            .WithNormalDistribution(p => p.Lines.Each().Pulse, 70, 8, min: 40, max: 120)        // a value outside the limits is drawn again
            .Generate(1000);

        var iq = faker.Number.Normal<int>(100, 15);          // also Normal(), faker.Random.Normal() and {{number.normal(50, 5)}}
        // end snippet

        people.Take(3).Select(p => p.Height).ShouldBe([184.81, 171.03, 184.82]);
        people.Average(p => p.Height).ShouldBeInRange(173, 177);
        people.SelectMany(p => p.Lines).ShouldAllBe(r => r.Pulse >= 40 && r.Pulse <= 120);
        iq.ShouldBeInRange(0, 200);
    }

    [Test]
    public void Numbering_and_cycling()
    {
        // snippet: readme-sequence
        var tickets = Fake.For<Ticket>().Seed(1)
            .Sequence(t => t.Code, i => $"T{i + 1:D4}")                       // T0001, T0002, T0003…
            .Cycle(t => t.Queue, ["north", "south", "east"])                  // north, south, east, north…
            .Generate(100);
        // end snippet

        tickets.Take(4).Select(t => (t.Code, t.Queue)).ShouldBe([("T0001", "north"), ("T0002", "south"), ("T0003", "east"), ("T0004", "north")]);
    }

    [Test]
    public void Populating_existing_objects()
    {
        var generator = Fake.For<Customer>().Seed(1);
        var existing = new Customer { FirstName = "Anna" };
        List<Customer> customers = [new(), new() { Email = "kept@example.com" }];

        // snippet: readme-populate
        var order = Fake.Populate(new Order { CustomerId = 42 });         // CustomerId stays 42, everything else is filled
        generator.Populate(existing, PopulateMode.All);                   // regenerate every member that isn't ignored
        List<Customer> filled = generator.Populate(customers);            // many at once, each from its own place in the seeded sequence
        // end snippet

        order.CustomerId.ShouldBe(42);
        order.Reference.ShouldNotBeEmpty();
        existing.FirstName.ShouldNotBe("Anna");
        filled[1].Email.ShouldBe("kept@example.com");
    }

    [Test]
    public void Data_tied_to_a_key()
    {
        var user = new Customer { Email = "anna@example.com" };
        List<Customer> customers = [new() { Id = 1, Email = "anna@example.com" }, new() { Id = 2, Email = "anna@example.com" }];
        // snippet: readme-keyed
        string name = Fake.Keyed("customer:42").Person.FullName();     // always the same name for customer 42
        Faker faker = new Faker("sv").Keyed(user.Email);                // from any faker
        Customer c = Fake.For<Customer>().GenerateFor("customer:42");   // the whole object for a key
        List<Order> orders = Fake.For<Order>().Keyed("customer:42").Generate(3);   // the key's own sequence; the first is GenerateFor's

        using (Fake.ForKey("customer:42"))                               // a scope, like Fake.Seeded and Fake.Fork
        {
            Customer same = Fake.One<Customer>();                        // the same stand-in as GenerateFor, however often you ask
            List<Order> his = Fake.Many<Order>(3);                       // the same as Keyed("customer:42").Generate(3)
            string sameName = Fake.Person.FullName();                    // the same as Fake.Keyed("customer:42").Person.FullName()
        }

        List<Customer> masked = Fake.For<Customer>()
            .Ignore(x => x.Id)                                           // keep what isn't personal
            .Pseudonymize(customers, x => x.Email);                      // the same real email gets the same fake person in every table
        // end snippet

        faker.ShouldNotBeNull();
        using (Fake.ForKey("customer:42"))
        {
            Fake.One<Customer>().Email.ShouldBe(c.Email);
            Fake.Many<Order>(3).Select(o => o.Reference).ShouldBe(orders.Select(o => o.Reference));
            Fake.Person.FullName().ShouldBe(name);
        }
        masked[1].Email.ShouldBe(masked[0].Email);
    }
}
