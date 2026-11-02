using System.ComponentModel.DataAnnotations;
using Phoney.Modules;

// The model the README's examples are written against. Its own namespace keeps it apart from the test models and the guides' shop.
namespace Phoney.Tests.Docs.Readme;

public enum Gender { Female, Male }

public enum CustomerStatus { Prospect, Active, Closed }

public enum Tier { Standard, Silver, Gold }

public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public Gender Gender { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public int Age { get; set; }
    public string Country { get; set; } = "";
    public int LoyaltyPoints { get; set; }
    public CustomerStatus Status { get; set; }
    public Tier Tier { get; set; }
    public Address Address { get; set; } = new();
    public string? InternalNotes { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
    public Contact Contact { get; set; } = new();
}

public sealed class Contact
{
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Reference { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string Country { get; set; } = "";
    public int SalesRepId { get; set; }

    [Range(1, 10)]
    public int Quantity { get; set; }

    public decimal Price { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Address Billing { get; set; } = new();
    public Address Shipping { get; set; } = new();
    public IPayment? Payment { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string Sku { get; set; } = "";

    [Range(1, 10)]
    public int Quantity { get; set; }

    public decimal Price { get; set; }
    public Supplier? Supplier { get; set; }
}

public sealed class Supplier
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class Invoice
{
    public string PromoCode { get; set; } = "";
    public Address Billing { get; set; } = new();
    public Address Shipping { get; set; } = new();
    public IPayment? Payment { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public Dictionary<string, OrderLine> ByCode { get; set; } = [];
}

public interface IPayment
{
    decimal Amount { get; }
}

public sealed class Card : IPayment
{
    public decimal Amount { get; set; }
    public string Network { get; set; } = "";
}

public interface IShape;

public sealed record Circle(double Radius) : IShape;

public sealed record Square(double Side) : IShape;

public interface IAnimal
{
    string Name { get; }
}

public sealed class Cat : IAnimal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : IAnimal
{
    public string Name { get; set; } = "";
}

public sealed class Drawing
{
    public string Title { get; set; } = "";
    public IShape? Shape { get; set; }
    public IShape? Logo { get; set; }
    public IAnimal? Pet { get; set; }
}

public sealed class User
{
    public List<string> Roles { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public List<string> Badges { get; set; } = [];
}

public sealed record SalesRep(int Id, double Seniority);

public sealed class Person
{
    public double Height { get; set; }
    public int Iq { get; set; }
    public List<Reading> Lines { get; set; } = [];
}

public sealed class Reading
{
    public int Pulse { get; set; }
}

public sealed class Ticket
{
    public string Code { get; set; } = "";
    public string Queue { get; set; } = "";
}

/// <summary>The application code the README's checks call; the README compiles against it but never runs it.</summary>
public static class Pricing
{
    public static decimal Total(Order order) => order.Quantity * order.Price;

    public static decimal Total(OrderLine line, int discount) => line.Quantity * line.Price * (100 - discount) / 100;
}

public static class Signup
{
    public static IReadOnlyList<string> Validate(string email, int age) => email.Contains('@') && age >= 18 ? [] : ["invalid"];
}

public sealed class ShopApi
{
    public Task<decimal> Quote(Order order) => Task.FromResult(Pricing.Total(order));

    public Task Register(Customer customer) => Task.CompletedTask;
}

public sealed class OrderValidator
{
    public (bool IsValid, string[] Errors) Validate(Order order) =>
        Validator.TryValidateObject(order, new ValidationContext(order), null, validateAllProperties: true) ? (true, []) : (false, ["invalid"]);
}

/// <summary>A scheme of one's own, as in the README's national id example.</summary>
public sealed class MyEmployeeNumbers() : NationalIdScheme("ZZ", NationalIdKind.Person)
{
    public override string Generate(SeededRandom random, NationalIdRequest request) => random.ReplaceSymbols("E-#####");

    public override bool IsValid(string value) => value.Length == 7 && value.StartsWith("E-", StringComparison.Ordinal);
}
