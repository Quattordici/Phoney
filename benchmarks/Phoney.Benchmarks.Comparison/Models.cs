using Phoney;
using Phoney.Generation;

/// <summary>An object with scalar members only, the kind a name convention can fill without any configuration.</summary>
public sealed class FlatCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string Company { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
    public DateTime BirthDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>An object graph: a nested object and two levels of collections.</summary>
public sealed class Shopper
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public Address Home { get; set; } = new();
    public List<Purchase> Purchases { get; set; } = [];
}

public sealed class Address
{
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
    public string Country { get; set; } = null!;
}

public sealed class Purchase
{
    public Guid Id { get; set; }
    public decimal Total { get; set; }
    public DateTime PlacedAt { get; set; }
    public PurchaseStatus Status { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
}

public sealed class PurchaseLine
{
    public string Sku { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public enum PurchaseStatus
{
    Pending,
    Paid,
    Shipped,
}

[FakeFor<FlatCustomer>, FakeFor<Shopper>]
public static partial class ComparisonFakes;
