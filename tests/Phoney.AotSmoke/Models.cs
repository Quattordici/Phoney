using System.Text.Json.Serialization;
using Phoney.Generation;

namespace Phoney.AotSmoke;

public sealed class Person
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public Address Home { get; init; } = null!;
    public List<Pet> Pets { get; set; } = [];
    public Status Status { get; set; }
    public IVehicle Vehicle { get; set; } = null!;
    public IInsurance Insurance { get; set; } = null!;
    public IContact Contact { get; set; } = null!;
    public global::NodaTime.Instant CreatedAt { get; set; }
    public System.Collections.Immutable.ImmutableArray<string> Aliases { get; set; }
    public System.Collections.Frozen.FrozenDictionary<string, int> Visits { get; set; } = null!;
    public List<string> Badges { get; } = [];
}

[JsonDerivedType(typeof(Car))]
[JsonDerivedType(typeof(Bike))]
public interface IVehicle;

public sealed class Car : IVehicle
{
    public string LicensePlate { get; set; } = "";
}

public sealed class Bike : IVehicle
{
    public int Gears { get; set; }
}

public interface IInsurance;

public sealed record Policy(string AccountNumber) : IInsurance;

public interface IContact;

public sealed record Email(string Address) : IContact;

public sealed record Phone(string Number) : IContact;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed record Address(string Street, string City, string ZipCode, string Country);

public sealed record Pet(string PetName, int Age);

public enum Status
{
    Active,
    Suspended,
}

public sealed class Ticket
{
    [System.ComponentModel.DataAnnotations.Range(1, 8)]
    public int Seats { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(6, MinimumLength = 2)]
    public string Code { get; set; } = "";

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string Contact { get; set; } = "";
}

public sealed class Folder
{
    public string Title { get; set; } = "";
    public List<Document> Documents { get; set; } = [];
}

public sealed class Document
{
    public string Name { get; set; } = "";
    public Folder? Folder { get; set; }
}

public sealed class Crate
{
    public string Name { get; set; } = "";
}

public sealed class Reply<T>
{
    public T Payload { get; set; } = default!;
    public List<T> Items { get; set; } = [];
}

public sealed class Shipment
{
    public Reply<Crate> Reply { get; set; } = null!;
    public (int, string) Pair { get; set; }
    public KeyValuePair<string, int> Entry { get; set; }
    public System.Net.IPAddress Address { get; set; } = null!;
    public Version Version { get; set; } = null!;
    public System.Numerics.BigInteger Weight { get; set; }
    public Memory<byte> Buffer { get; set; }
    public int[,] Grid { get; set; } = null!;
    public List<string> Crew { get; set; } = [];
}

[FakeFor<Shipment>]
internal static partial class ShipmentFakes;

[FakeFor<Folder>, FakeFor<Person>, FakeFor<Email>, FakeFor<Phone>, FakeFor<Ticket>, FakeSubtype<IInsurance, Policy>]
internal static partial class Fakes;
