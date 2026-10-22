using System.ComponentModel.DataAnnotations;
using Phoney;
using Phoney.Generation;
using Phoney.Modules;
using Phoney.Samples;

// 1. Single values ------------------------------------------------------------------------------
Console.WriteLine("== Values");
Console.WriteLine(Fake.Person.FullName());
Console.WriteLine(Fake.Internet.Email());
Console.WriteLine(Fake.Location.StreetAddress(useFullAddress: true));

var person = new Faker("de", seed: 3).Person.Profile();
Console.WriteLine($"{person.FullName} ({person.Age}), {person.Email}, {person.Phone}, {person.Address}");

foreach (var locale in new[] { "en", "sv", "de_AT", "ja" })
{
    var f = new Faker(locale, seed: 42);
    Console.WriteLine($"{locale,-6} {f.Person.FullName()}, {f.Location.City()}, {f.Phone.Number()}");
}

// 2. Templates (faker.js syntax) -----------------------------------------------------------------
Console.WriteLine("\n== Templates");
var faker = new Faker(seed: 7);
Console.WriteLine(faker.Parse("{{Person.FirstName}} ordered {{Commerce.ProductName}} from {{Company.Name}}"));
Console.WriteLine(faker.Parse("Ticket {{Helpers.FromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{String.Numeric(4)}}"));

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
    .Cities("Mora", "Falun", "Rättvik")
    .FirstNames(Sex.Female, "Kerstin", "Margit")
    .EmailDomains("dalarna.example")
    .Set("person.nickname", "Kalle", "Lisa"));
var dalarna = new Faker("sv_dalarna");
var resident = dalarna.Person.Profile(Sex.Female);
Console.WriteLine($"{resident.FullName} \"{dalarna.Pick("person.nickname")}\" <{resident.Email}> from {resident.Address.City}");

// 7. Interfaces and subtypes ------------------------------------------------------------------
Console.WriteLine("\n== Subtypes");
foreach (var drawing in Fake.For<Drawing>().Seed(1).Subtype<IShape, Circle, Square>().Generate(3))
    Console.WriteLine($"{drawing.Title}: {drawing.Shape}");

// Every Circle in the drawing comes from one generator, whatever member or collection it sits in.
var big = Fake.For<Drawing>().Seed(1).WithGeneratorFor<IShape, Circle>(Fake.For<Circle>().With(c => c.Radius, 10)).Generate(2);
Console.WriteLine(string.Join(", ", big.Select(d => d.Shape)));

// 7b. Invalid and boundary data, from the data annotations ------------------------------------
Console.WriteLine("\n== Invalid and boundary data");
foreach (var c in Fake.For<Registration>().Seed(1).InvalidCases())
    Console.WriteLine($"invalid  {c}  =>  {c.Value.Guests} guests, \"{c.Value.Name}\", {c.Value.Email}");
foreach (var c in Fake.For<Registration>().Seed(1).BoundaryCases())
    Console.WriteLine($"boundary {c}  =>  {c.Value.Guests} guests, \"{c.Value.Name}\"");

// 7c. Rules for members of nested objects ------------------------------------------------------
Console.WriteLine("\n== Rules for nested members");
var shoppers = Fake.For<Customer>().Seed(2)
    .With(c => c.Address.City, "Uppsala")                  // this address only
    .With(c => c.Orders.Each().Status, OrderStatus.Shipped) // every order of the customer
    .Generate(2);
foreach (var shopper in shoppers)
    Console.WriteLine($"{shopper.FirstName} lives in {shopper.Address.City}; orders: {string.Join(", ", shopper.Orders.Select(o => o.Status))}");

// 7c2. Conditions and checked rules -------------------------------------------------------------
Console.WriteLine("\n== Conditions and checked rules");
foreach (var order in Fake.For<Order>().Seed(3).Where(o => o.Total > 100).Where(o => o.Status != OrderStatus.Pending).Generate(3))
    Console.WriteLine($"order {order.Id:N}: {order.Total:C} ({order.Status})");
try
{
    // Every customer has at least one order, so the Orders rule applies; nothing in a customer is a Shape, so the Use never does.
    Fake.For<Customer>().Seed(2)
        .With(c => c.Orders.Each().Status, OrderStatus.Shipped)
        .WithGeneratorFor(Fake.For<Circle>())
        .VerifyRules()
        .Generate(5);
}
catch (InvalidOperationException e)
{
    Console.WriteLine(e.Message.Split(Environment.NewLine)[0..2].Aggregate((a, b) => a + Environment.NewLine + b));
}

// 7d. Random but replayable --------------------------------------------------------------------
Console.WriteLine("\n== Replayable seeds");
var seed = FakeSeed.Pick();                      // PHONEY_SEED if set, else a new random seed
using (Fake.Seeded(seed))
    Console.WriteLine($"seed {seed}: {Fake.Person.FullName()}");
using (Fake.Seeded(seed))
    Console.WriteLine($"seed {seed} again: {Fake.Person.FullName()}");   // the same person

// 7e. Parallel work and stand-ins tied to a key -----------------------------------------------
Console.WriteLine("\n== Forks and keys");
using (Fake.Seeded(7))
{
    var names = new string[4];
    Parallel.For(0, names.Length, i =>
    {
        using var fork = Fake.Fork(i);           // each item its own faker: the same names whatever the thread timing
        names[i] = Fake.Person.FullName();
    });
    Console.WriteLine(string.Join(", ", names));
}

Console.WriteLine($"customer:42 is always {Fake.Keyed("customer:42").Person.FullName()}");
var real = new List<Customer> { new() { Id = 1, Email = "anna@example.com" }, new() { Id = 2, Email = "anna@example.com" } };
foreach (var masked in Fake.For<Customer>().Seed(99).Ignore(c => c.Id).Pseudonymize(real, c => c.Email))
    Console.WriteLine($"  #{masked.Id}: {masked.FirstName} {masked.LastName} <{masked.Email}>");   // the same stand-in for the same person

// 7f. Property checks that shrink -------------------------------------------------------------
Console.WriteLine("\n== Property check");
try
{
    // A hundred customers; the first whose orders reach 2,000 is shrunk to the simplest customer the generator can make that still does.
    SampleData.Customer.Seed(3).CollectionSize(0, 5).Check(c => c.Orders.Sum(o => o.Total) < 2_000);
}
catch (FakeCheckException<Customer> e)
{
    var shrunk = e.Counterexample;
    Console.WriteLine($"Failed for customer {e.Index + 1}, shrunk in {e.ShrinkSteps} steps to {shrunk.FirstName} {shrunk.LastName} with {shrunk.Orders.Count} order(s) totalling {shrunk.Orders.Sum(o => o.Total)}");
}

try
{
    // The parameters of a lambda are generated like test parameters ([FakeData]) and a failing set is shrunk the same way (seeded here so the output is stable).
    using var seeded = Fake.Seeded(4);
    Fake.Check((Order order, [Range(0, 100)] int discount) => order.Total * (100 - discount) / 100 > 1);
}
catch (FakeCheckException<object?[]> e)
{
    Console.WriteLine($"Fake.Check failed for row {e.Index + 1}; simplest failing discount: {e.Counterexample[1]}");
}

// 8. Time from a TimeProvider -----------------------------------------------------------------
Console.WriteLine("\n== TimeProvider");
var clock = new FixedClock(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
var future = new Faker("en", seed: 1) { TimeProvider = clock };
Console.WriteLine($"Relative to {clock.GetUtcNow():yyyy-MM-dd}: past {future.Date.Past():yyyy-MM-dd}, birthdate {future.Date.Birthdate()}");
