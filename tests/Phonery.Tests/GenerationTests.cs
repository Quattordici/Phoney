using Phonery.Generation;

namespace Phonery.Tests;

public enum Gender
{
    Female,
    Male,
}

public enum OrderStatus
{
    Pending,
    Shipped,
    Delivered,
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public Gender Gender { get; set; }
    public string Avatar { get; set; } = "";
    public DateOnly DateOfBirth { get; set; }
    public int Age { get; set; }
    public string? Phone { get; set; }
    public Address HomeAddress { get; set; } = null!;
    public List<Order> Orders { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public Guid ExternalId { get; set; }
}

public sealed record Address(string Street, string City, string ZipCode, string Country);

public sealed class Order
{
    public long Id { get; init; }
    public int CustomerId { get; init; }
    public decimal Total { get; init; }
    public OrderStatus Status { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public Customer? Customer { get; set; }
    public OrderLine[] Lines { get; init; } = [];
}

public sealed record OrderLine(string ProductName, int Quantity, decimal UnitPrice);

public struct Point
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public sealed class Unconventional
{
    public string Foo { get; set; } = "";
    public string Email { get; set; } = "";
    public int Bar { get; set; }
}

public sealed class GenerationTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_populates_by_convention()
    {
        var c = Fake.For<Customer>().Seed(1).ReferenceDate(Reference).Generate();

        c.Id.ShouldBe(1);
        c.FirstName.ShouldNotBeNullOrWhiteSpace();
        c.Email.ShouldContain("@");
        c.Avatar.ShouldStartWith("https://");
        c.Age.ShouldBeInRange(18, 80);
        c.DateOfBirth.ShouldBeLessThan(DateOnly.FromDateTime(Reference.UtcDateTime).AddYears(-17));
        c.CreatedAt.ShouldBeLessThanOrEqualTo(Reference.UtcDateTime);
        c.ExternalId.ShouldNotBe(Guid.Empty);
        c.HomeAddress.ShouldNotBeNull();
        c.HomeAddress.City.ShouldNotBeNullOrWhiteSpace();
        c.Orders.Count.ShouldBeInRange(1, 3);
        c.Tags.Count.ShouldBeInRange(1, 3);
    }

    [Fact]
    public void Identity_members_are_coherent()
    {
        foreach (var c in Fake.For<Customer>().Seed(2).Generate(50))
        {
            var local = c.Email[..c.Email.IndexOf('@')];
            // The email is derived from the same first/last name (transliterated, so compare loosely).
            (local.Contains(Ascii(c.FirstName), StringComparison.OrdinalIgnoreCase) ||
             local.Contains(Ascii(c.LastName), StringComparison.OrdinalIgnoreCase)).ShouldBeTrue($"{c.FirstName} {c.LastName} <{c.Email}>");
            c.Avatar.ShouldContain(c.Gender == Gender.Female ? "/female/" : "/male/");
        }

        static string Ascii(string s) => new(s.Normalize(System.Text.NormalizationForm.FormKD).Where(char.IsAsciiLetter).ToArray());
    }

    [Fact]
    public void Records_init_only_members_and_nested_arrays_are_populated()
    {
        var order = Fake.For<Order>().Seed(3).Generate();
        order.Id.ShouldBe(1);
        order.CustomerId.ShouldBeGreaterThan(0);
        order.Total.ShouldBeGreaterThan(0);
        order.Lines.Length.ShouldBeInRange(1, 3);
        order.Lines.ShouldAllBe(l => l.ProductName.Length > 0 && l.Quantity >= 1 && l.UnitPrice > 0);
        Enum.IsDefined(order.Status).ShouldBeTrue();
    }

    [Fact]
    public void Cycles_stop_instead_of_recursing_forever()
    {
        var customer = Fake.For<Customer>().Seed(4).Generate();
        customer.Orders.ShouldAllBe(o => o.Customer == null);
    }

    [Fact]
    public void Structs_are_supported() =>
        Fake.One<Point>().Latitude.ShouldBeInRange(-90, 90);

    [Fact]
    public void Seeded_generation_is_reproducible_and_order_independent()
    {
        var generator = Fake.For<Customer>().Seed(42).ReferenceDate(Reference);
        var sequential = generator.Generate(20);
        var again = Fake.For<Customer>().Seed(42).ReferenceDate(Reference).Generate(20);
        var parallel = Fake.For<Customer>().Seed(42).ReferenceDate(Reference).GenerateParallel(20);

        again.Select(Describe).ShouldBe(sequential.Select(Describe));
        parallel.Select(Describe).ShouldBe(sequential.Select(Describe));

        // Item n is the same whether generated alone or in a batch.
        var one = Fake.For<Customer>().Seed(42).ReferenceDate(Reference);
        Describe(one.Generate()).ShouldBe(Describe(sequential[0]));
        Describe(one.Generate()).ShouldBe(Describe(sequential[1]));

        static string Describe(Customer c) => $"{c.Id}|{c.FirstName}|{c.LastName}|{c.Email}|{c.HomeAddress.City}|{c.CreatedAt:O}|{c.Orders.Count}";
    }

    [Fact]
    public void Rules_override_conventions()
    {
        var customers = Fake.For<Customer>()
            .With(x => x.FirstName, "Ada")
            .With(x => x.Age, f => f.Random.Int(30, 31))
            .With(x => x.Email, (f, x) => $"{x.FirstName}.{x.LastName}@example.com".ToLowerInvariant())
            .Ignore(x => x.Orders)
            .Generate(10);

        customers.ShouldAllBe(c => c.FirstName == "Ada");
        customers.ShouldAllBe(c => c.Age == 30 || c.Age == 31);
        customers.ShouldAllBe(c => c.Email.StartsWith("ada.") && c.Email.EndsWith("@example.com"));
        customers.ShouldAllBe(c => c.Orders.Count == 0); // ignored: keeps its initializer
    }

    [Fact]
    public void Nested_generators_configure_nested_objects()
    {
        var customer = Fake.For<Customer>()
            .With(x => x.HomeAddress, Fake.For<Address>().With(a => a.Country, "Sweden"))
            .Generate();
        customer.HomeAddress.Country.ShouldBe("Sweden");
    }

    [Fact]
    public void Generators_are_immutable_and_derivable()
    {
        var baseGenerator = Fake.For<Customer>().With(x => x.IsActive, true);
        var inactive = baseGenerator.With(x => x.IsActive, false);

        baseGenerator.Generate(5).ShouldAllBe(c => c.IsActive);
        inactive.Generate(5).ShouldAllBe(c => !c.IsActive);
    }

    [Fact]
    public void Unique_members_never_repeat()
    {
        var values = Fake.For<Customer>()
            .With(x => x.Age, f => f.Random.Int(1, 50))
            .Unique(x => x.Age)
            .Generate(50)
            .Select(c => c.Age)
            .ToList();
        values.Distinct().Count().ShouldBe(50);

        Should.Throw<InvalidOperationException>(() =>
            Fake.For<Customer>().With(x => x.Age, f => f.Random.Int(1, 3)).Unique(x => x.Age, maxAttempts: 50).Generate(4));
    }

    [Fact]
    public void Strict_mode_reports_members_without_rules_or_conventions()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Fake.For<Unconventional>().Strict().Generate());
        ex.Message.ShouldContain("Foo");
        ex.Message.ShouldContain("Bar");
        ex.Message.ShouldNotContain("Email");

        Fake.For<Unconventional>().Strict().With(x => x.Foo, "x").Ignore(x => x.Bar).Generate().Foo.ShouldBe("x");
    }

    [Fact]
    public void Custom_conventions_apply_to_matching_members()
    {
        var item = Fake.For<Unconventional>()
            .WithConvention<string>(name => name == "Foo", f => "custom")
            .Generate();
        item.Foo.ShouldBe("custom");
    }

    [Fact]
    public void Collection_size_null_probability_and_locale_are_configurable()
    {
        var customers = Fake.For<Customer>()
            .CollectionSize(4, 4)
            .NullProbability(1)
            .Locale("sv")
            .Generate(5);

        customers.ShouldAllBe(c => c.Tags.Count == 4 && c.Phone == null);
    }

    [Fact]
    public void Unknown_members_in_rules_fail_clearly()
    {
        var ex = Should.Throw<ArgumentException>(() => Fake.For<Customer>().With(x => x.HomeAddress.City, "x"));
        ex.Message.ShouldContain("must select a member");
    }

    [Fact]
    public void After_create_and_create_with_hooks()
    {
        var c = Fake.For<Customer>()
            .CreateWith(_ => new Customer { FirstName = "Factory" })
            .With(x => x.LastName, "Rule")
            .AfterCreate((_, x) => x.Tags.Add("after"))
            .Generate();

        c.FirstName.ShouldBe("Factory");
        c.LastName.ShouldBe("Rule");
        c.Tags.ShouldBe(["after"]);
    }

    [Fact]
    public void Stream_is_lazy_and_ids_are_sequential() =>
        Fake.For<Customer>().Stream().Take(3).Select(c => c.Id).ShouldBe([1, 2, 3]);

    [Theory]
    [InlineData("FirstName", TypeCategory.String, ConventionKind.FirstName)]
    [InlineData("first_name", TypeCategory.String, ConventionKind.FirstName)]
    [InlineData("BillingCity", TypeCategory.String, ConventionKind.City)]
    [InlineData("HomeEmailAddress", TypeCategory.String, ConventionKind.Email)]
    [InlineData("Monkey", TypeCategory.String, ConventionKind.None)]
    [InlineData("CustomerId", TypeCategory.Int32, ConventionKind.ReferenceId)]
    [InlineData("Id", TypeCategory.Int64, ConventionKind.Id)]
    [InlineData("Age", TypeCategory.String, ConventionKind.None)]
    [InlineData("UpdatedAt", TypeCategory.DateTime, ConventionKind.RecentDate)]
    [InlineData("UnitPrice", TypeCategory.Decimal, ConventionKind.Price)]
    public void Convention_rules(string name, TypeCategory category, ConventionKind expected) =>
        ConventionRules.Match(name, category, "Thing").ShouldBe(expected);

    [Theory]
    [InlineData("Company", ConventionKind.CompanyName)]
    [InlineData("Product", ConventionKind.ProductName)]
    [InlineData("Customer", ConventionKind.FullName)]
    [InlineData("Category", ConventionKind.Department)]
    public void Name_depends_on_the_declaring_type(string typeName, ConventionKind expected) =>
        ConventionRules.Match("Name", TypeCategory.String, typeName).ShouldBe(expected);
}
