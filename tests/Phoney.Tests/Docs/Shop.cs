using System.ComponentModel.DataAnnotations;

// The model the guides under docs/ are written against. Its own namespace keeps it apart from the test models.
namespace Phoney.Tests.Docs.Shop;

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Reference { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<OrderLine> Lines { get; set; } = [];

    public decimal Total => Lines.Sum(l => l.Quantity * l.Price);
}

public sealed class OrderLine
{
    public int OrderId { get; set; }

    [RegularExpression("^[A-Z]{3}-[0-9]{5}$")]
    public string Sku { get; set; } = "";

    [Range(1, 10)]
    public int Quantity { get; set; }

    [Range(0.01, 1000)]
    public decimal Price { get; set; }
}

public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

public sealed class Product
{
    [RegularExpression("^[A-Z]{3}-[0-9]{5}$")]
    public string Sku { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed class Signup
{
    [Required, StringLength(40, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Range(18, 120)]
    public int Age { get; set; }
}

/// <summary>Only the guide on configuring a type once uses it, because that configuration is app-wide.</summary>
public sealed class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Department { get; set; } = "";
}
