using Phoney;
using Phoney.Generation;
using Phoney.Modules;

// 1. Single values ------------------------------------------------------------------------------
Console.WriteLine("== Values");
Console.WriteLine(Fake.Person.FullName());
Console.WriteLine(Fake.Internet.Email());
Console.WriteLine(Fake.Location.StreetAddress(useFullAddress: true));

foreach (var locale in new[] { "en", "sv", "de_AT", "ja" })
{
    var f = new Faker(locale, seed: 42);
    Console.WriteLine($"{locale,-6} {f.Person.FullName()}, {f.Location.City()}, {f.Phone.Number()}");
}

// 2. Templates (faker.js syntax) -----------------------------------------------------------------
Console.WriteLine("\n== Templates");
var faker = new Faker(seed: 7);
Console.WriteLine(faker.Parse("{{person.firstName}} ordered {{commerce.productName}} from {{company.name}}"));
Console.WriteLine(faker.Parse("Ticket {{helpers.fromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{string.numeric(4)}}"));

// 3. Objects by convention ---------------------------------------------------------------------
Console.WriteLine("\n== Objects");
var customer = Fake.One<Customer>();
Console.WriteLine($"{customer.Id}: {customer.FirstName} {customer.LastName} <{customer.Email}> {customer.Gender}, born {customer.DateOfBirth}");
Console.WriteLine($"   {customer.Address.Street}, {customer.Address.ZipCode} {customer.Address.City}");
foreach (var order in customer.Orders)
    Console.WriteLine($"   order {order.Id:N}: {order.Total:C} ({order.Status})");

// 4. Rules ----------------------------------------------------------------------------------------
Console.WriteLine("\n== Rules");
var swedes = Fake.For<Customer>()
    .Locale("sv")
    .Seed(2024)
    .With(x => x.LoyaltyPoints, f => f.Random.Int(0, 5_000))
    .With(x => x.Email, (f, x) => f.Internet.Email(x.FirstName, x.LastName, provider: "example.se"))
    .Unique(x => x.Email)
    .Ignore(x => x.Orders);
foreach (var c in swedes.Generate(3))
    Console.WriteLine($"{c.FirstName} {c.LastName} <{c.Email}> {c.LoyaltyPoints} points");

// Generators are immutable: derive variants cheaply.
var vip = swedes.With(x => x.LoyaltyPoints, 10_000);
Console.WriteLine($"VIP: {vip.Generate().LoyaltyPoints} points");

// 5. Source-generated (reflection-free, Native AOT safe) ------------------------------------------
Console.WriteLine("\n== Source generated");
foreach (var c in SampleData.Customer.Seed(1).Generate(2))
    Console.WriteLine($"{c.Id}: {c.FirstName} {c.LastName}, {c.Address.City}");

// 6. Custom locale data -----------------------------------------------------------------------
Console.WriteLine("\n== Custom data");
Locales.Register("sv_dalarna", l => l
    .FallbackTo("sv")
    .Set("location.city_pattern", "{{location.city_name}}", "Mora", "Falun", "Rättvik")
    .Set("person.nickname", "Kalle", "Lisa"));
var dalarna = new Faker("sv_dalarna");
Console.WriteLine($"{dalarna.Person.FirstName()} \"{dalarna.Pick("person.nickname")}\" from {dalarna.Location.City()}");

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
