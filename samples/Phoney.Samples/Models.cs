using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Samples;

public enum Gender
{
    Female,
    Male,
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public Gender Gender { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public int LoyaltyPoints { get; set; }
    public Address Address { get; set; } = null!;
    public List<Order> Orders { get; set; } = [];
}

public sealed record Address(string Street, string City, string ZipCode);

public sealed record Order(Guid Id, decimal Total, OrderStatus Status);

[FakeFor<Customer>]
internal static partial class SampleData;

public sealed class Registration
{
    [Range(1, 10)] public int Guests { get; set; }
    [StringLength(20, MinimumLength = 3)] public string Name { get; set; } = "";
    [EmailAddress] public string Email { get; set; } = "";
}

public interface IShape;

public sealed record Circle(double Radius) : IShape;

public sealed record Square(double Side) : IShape;

public sealed class Drawing
{
    public string Title { get; set; } = "";
    public IShape Shape { get; set; } = null!;
}

/// <summary>A clock that stands still, like <c>FakeTimeProvider</c> in tests.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
