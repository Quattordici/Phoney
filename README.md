# Phoney

[![NuGet](https://img.shields.io/nuget/v/Phoney.svg?label=NuGet)](https://www.nuget.org/packages/Phoney)
[![NuGet prerelease](https://img.shields.io/nuget/vpre/Phoney.svg?label=NuGet%20prerelease)](https://www.nuget.org/packages/Phoney)
[![NuGet downloads](https://img.shields.io/nuget/dt/Phoney.svg?label=Downloads)](https://www.nuget.org/packages/Phoney)
[![Phoney.TUnit](https://img.shields.io/nuget/vpre/Phoney.TUnit.svg?label=Phoney.TUnit)](https://www.nuget.org/packages/Phoney.TUnit)
[![Phoney.DependencyInjection](https://img.shields.io/nuget/vpre/Phoney.DependencyInjection.svg?label=Phoney.DependencyInjection)](https://www.nuget.org/packages/Phoney.DependencyInjection)
[![Phoney.EntityFrameworkCore](https://img.shields.io/nuget/vpre/Phoney.EntityFrameworkCore.svg?label=Phoney.EntityFrameworkCore)](https://www.nuget.org/packages/Phoney.EntityFrameworkCore)
[![Phoney.NodaTime](https://img.shields.io/nuget/vpre/Phoney.NodaTime.svg?label=Phoney.NodaTime)](https://www.nuget.org/packages/Phoney.NodaTime)

**Fast, easy and flexible fake data for .NET**, powered by the locale data of [faker.js](https://github.com/faker-js/faker).

- **76 locales** imported from faker.js (`@faker-js/faker` 10.6.0), kept up to date by an automated sync pipeline.
- **One line to a value or a fully populated object**: `Fake.Person.FullName()`, `Fake.Many<Customer>(100)`.
- **Convention-based population** of classes, records and structs: `FirstName`, `Email`, `BillingCity`, `Price`, `CreatedAt`… just work, and names, emails and avatars of one object belong to the same person.
- **Flexible**: fluent, immutable generators, rules that depend on other members, uniqueness, strict mode (also checked at compile time), custom conventions, custom locales, faker.js-style templates.
- **Interfaces and polymorphism**: `.Subtype<IShape, Circle, Square>()`, app-wide `Fake.Subtypes`, and `[JsonDerivedType]` honoured out of the box.
- **Testable time**: relative dates follow a `TimeProvider` such as `FakeTimeProvider`.
- **Fast**: lookups are array indexed and allocation free, templates are parsed once, and a **source generator** makes generation reflection free.
- **Deterministic**: same seed means same data, dates included, on every OS and .NET version and every day you run it; item *n* is the same whether you generate 1 or 1,000,000 items, sequentially or in parallel.
- **Trim and Native AOT safe** with the source generator.
- **Integrations**: `[FakeData]` for TUnit tests, `services.AddPhoney()` for dependency injection and ASP.NET Core, `HasFakeData` for EF Core seeding, and NodaTime types.

| Package | What it adds |
|---|---|
| `Phoney` | fakers, generators, the source generator and the Strict analyzer |
| `Phoney.TUnit` | [`[FakeData]` for TUnit](#test-data-in-tunit) |
| `Phoney.DependencyInjection` | [`services.AddPhoney()`, `IFaker`, injectable generators](#dependency-injection-and-aspnet-core) |
| `Phoney.EntityFrameworkCore` | [`HasFakeData` seed data](#ef-core-seeding) |
| `Phoney.NodaTime` | [NodaTime values and members](#nodatime) |

Targets .NET 8 and .NET 10.

```bash
dotnet add package Phoney
```

## Quick start

```csharp
using Phoney;

// Values
Fake.Person.FullName();                         // "Madisen Collins"
Fake.Internet.Email();                          // "Ryann_Beahan@yahoo.com"
Fake.Location.StreetAddress(useFullAddress: true);

// A whole, coherent person
PersonProfile p = Fake.Person.Profile();        // names, email, username, phone, birthdate + age, address, national id

// A specific locale and a seed
var faker = new Faker("sv", seed: 42);
faker.Person.FullName();                        // "Madeleine Öberg Nordström"
faker.Finance.Iban("SE", formatted: true);      // valid check digits

// Objects
Customer customer = Fake.One<Customer>();
List<Customer> customers = Fake.Many<Customer>(1_000);
```

## A whole person

`Person.Profile()` returns a `PersonProfile` whose parts belong together: the names match the sex, email and username are built from the names, the age matches the birthdate, and the national id (when the locale's country has a scheme) encodes birthdate and sex.

```csharp
var faker = new Faker("de", seed: 7);
PersonProfile p = faker.Person.Profile(Sex.Female, minAge: 25, maxAge: 40);
// p.FirstName, p.LastName, p.FullName, p.Email, p.Username, p.Phone, p.BirthDate, p.Age, p.JobTitle, p.NationalId
// p.Address.Street, p.Address.City, p.Address.ZipCode, p.Address.State, p.Address.CountryCode

AddressProfile address = faker.Location.Address(); // just an address, as parts
```

For your own types, [`Fake.For<T>()`](#objects) gives the same coherence: a `Customer`'s `FirstName`, `Email`, `Avatar` and `Gender` describe one person.

## National and tax IDs

Personal identity numbers, company registration numbers and VAT numbers come with **valid check digits** and use the locale's country unless you pass one:

```csharp
var faker = new Faker("sv", seed: 1);
faker.NationalId.Person();                                   // "19560703-6190" (personnummer)
faker.NationalId.Person("NO", birthDate: new DateOnly(1990, 5, 17), sex: Sex.Female, formatted: false);
faker.NationalId.Company();                                  // "214200-3165" (organisationsnummer)
faker.NationalId.Vat("GB");                                  // "GB 154 4706 67"
```

| | Person | Company | VAT |
|---|---|---|---|
| SE | personnummer | organisationsnummer | ✓ |
| NO | fødselsnummer | organisasjonsnummer | ✓ |
| DK | CPR | CVR | ✓ |
| FI | henkilötunnus | Y-tunnus | ✓ |
| US | SSN (format rules, no check digit) | EIN | |
| GB | NINO (format rules, no check digit) | | ✓ |
| BR | CPF | CNPJ | |
| PL | PESEL | REGON | ✓ (NIP) |
| NL | BSN | | |
| ES | DNI | | |
| FR | NIR | SIREN | ✓ |
| CA | SIN | | |

Where the number encodes a birth date and sex (SE, NO, DK, FI, PL, FR) it encodes the ones you pass, so a `PersonProfile` or a generated object has an id that agrees with its `BirthDate` and `Sex`. Members named `Personnummer`, `Ssn`, `NationalId`, `Cpf`, `Pesel`… (person), `OrgNr`, `Cnpj`, `Ein`… (company) and `VatNumber` get these values by convention. A locale whose country has no scheme leaves them empty; `NationalId` throws a `PhoneyDataUnavailableException`, so pass the country there.

Every scheme can also validate, and you can register your own:

```csharp
NationalIds.IsValid(NationalIdKind.Person, "SE", "19560703-6190");   // true
NationalIds.Supported(NationalIdKind.Vat);                           // ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]
NationalIds.Register(new MyEmployeeNumbers());                       // derive from NationalIdScheme
```

The numbers are structurally valid, not issued: they can coincide with real people's or companies' numbers, so use them for test data only. Danish CPR numbers satisfy the classic mod-11 rule that new numbers no longer follow, so old and new validators accept them; the validator checks the date and century digit only.

## Values

A `Faker` groups values by topic:

| Module | Examples |
|---|---|
| `Person` | `Profile()`, `FirstName(Sex.Female)`, `LastName()`, `FullName()`, `Sex()`, `JobTitle()`, `Bio()`, `Prefix()` |
| `Location` | `Address()`, `StreetAddress()`, `City()`, `ZipCode()`, `State()`, `Country()`, `CountryCode()`, `Latitude()`, `NearbyGpsCoordinate()` |
| `Internet` | `Email()`, `Username()`, `Url()`, `DomainName()`, `Ipv4(IPv4Network.PrivateC)`, `Ipv6()`, `Mac()`, `Password()`, `UserAgent()`, `Jwt()`, `Emoji()` |
| `Phone` | `Number(PhoneStyle.International)`, `Imei()` |
| `Company` / `Commerce` | `Name()`, `CatchPhrase()`, `ProductName()`, `Price()`, `Isbn()`, `Upc()` |
| `Finance` | `Iban()`, `Bic()`, `CreditCardNumber(CardIssuer.Visa)`, `Currency()`, `Amount()`, `RoutingNumber()`, `BitcoinAddress()` |
| `NationalId` | `Person()`, `Company()`, `Vat()`, e.g. a Swedish personnummer, a US SSN, a Brazilian CPF, valid check digits |
| `Date` | `Past()`, `Future()`, `Recent()`, `Soon()`, `Between(...)`, `Birthdate(18, 65)`, `Month()`, `Weekday()` |
| `Lorem` / `Word` | `Sentence()`, `Paragraphs()`, `Slug()`, `Noun(minLength: 5)` |
| `String` / `Number` | `Uuid()`, `UuidV7()`, `Ulid()`, `NanoId()`, `AlphaNumeric(10)`, `Int(1, 6)`, `Decimal(0, 100, 2)` |
| `Color` | `Human()`, `Hex()`, `Rgb()` (typed, with `ToCss()`/`ToHex()`), `Hsl()`, `Lab()` |
| `Image` | `Url(640, 480)`, `Avatar(Sex.Female)`, `DataUri()` (self-contained SVG) |
| `Helpers` | `FromRegExp("[A-Z]{3}-\\d{4}")`, `Slugify()`, `LuhnCheck()`, `Multiple(f => …, 5)`, `UniqueValues(…, 10)` |
| also | `Animal`, `Book`, `Music`, `Food`, `Hacker`, `Vehicle` (with valid VINs), `Airline`, `Science`, `Database`, `System`, `Git` |

Checksums are real: credit cards and IMEIs pass Luhn, IBANs pass MOD 97, ISBN, UPC, VIN and ABA routing numbers have valid check digits.

`Fake.*` is a thread-safe static facade over a per-thread `Faker`.

`faker.Random` is the seeded source of randomness (xoshiro256\*\*) for your own values: `Int`, `Long`, `Double`, `Decimal`, `Bool(0.2)`, `Element(list)`, `Elements(list, 3)`, `Weighted(("gold", 1), ("silver", 3))`, `Enum<T>()`, `Guid()`, `Replace("##??")`.

Image URLs come from replaceable templates, so generated data doesn't have to point at public placeholder services:

```csharp
ImageSources.UseHost("https://images.test");                 // …/photos/{seed}/{width}x{height}, …/avatars/{sex}/{size}/{index}.jpg
ImageSources.Photo = "https://cdn.example.com/{width}x{height}?s={seed}";
```

### Reproducible data

A seed fixes values *and* dates: seeded fakers and generators compute relative dates (past, recent, birthdates, ages) from `Faker.DefaultSeededReferenceDate` (2025-01-01 UTC) unless you set `ReferenceDate`, so seeded output doesn't change from one day to the next.

```csharp
var faker = new Faker("de", seed: 2024);                         // same values and dates on every run
var later = new Faker("de", seed: 2024) { ReferenceDate = DateTimeOffset.UtcNow }; // opt back into "now"

[Fact]
public void Creates_an_invoice()
{
    using var _ = Fake.Seeded(42);           // Fake.*, Fake.One/Many/For are reproducible in this test only
    var customer = Fake.One<Customer>();     // the same customer on every run, even with tests in parallel
}

Fake.Seed(42);                                // or seed the facade app-wide (every thread starts the same sequence)
```

`Generator<T>.Seed(n)` does the same for a generator. One exception: `Unique(...)` members combined with `GenerateParallel` depend on thread timing, so use sequential generation when such results must be stable.

### Controlling "now" with `TimeProvider`

Relative dates can follow a .NET `TimeProvider` instead of the system clock, so they move with the clock your tests already control (for example `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`):

```csharp
var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

var faker = new Faker("sv") { TimeProvider = time };            // faker.Date.Past() is before 2030-01-01
var orders = Fake.For<Order>().TimeProvider(time).Generate(10);   // CreatedAt, ages, timelines follow the clock
var rows = Fake.Arguments(method).TimeProvider(time).Generate(5); // argument rows too
Fake.TimeProvider = time;                                         // the static facade, Fake.For/One/Many and Fake.Arguments
time.Advance(TimeSpan.FromDays(30));                              // read on every use, so dates move along
```

The reference date is, in order: an explicit `ReferenceDate`, the `TimeProvider`, `DefaultSeededReferenceDate` for seeded fakers and generators, and otherwise the system clock.

## Templates

Templates use the same names as the API:

```csharp
faker.Parse("{{Person.FirstName}} ordered {{Commerce.ProductName}} from {{Company.Name}}");
faker.Parse("Ticket {{Helpers.FromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{String.Numeric(4)}}");
faker.Parse("{{Person.FirstName}} lives at {{Location.Address}}");
```

Names are case-insensitive, so faker.js templates (`{{person.firstName}}`, `{{number.int({"min":1,"max":9})}}`) work unchanged, and so do locale data paths and record fields (`{{airline.airport.iataCode}}`). Register your own functions with `TemplateFunctions.Register("Shop.Sku", (f, _) => f.Random.Replace("SKU-#####"))`.

## Objects

```csharp
var customers = Fake.For<Customer>()
    .Locale("sv")
    .Seed(42)                                                     // reproducible
    .With(x => x.LoyaltyPoints, f => f.Random.Int(0, 5_000))      // value from a factory
    .With(x => x.Status, CustomerStatus.Active)                    // fixed value
    .With(x => x.Email, (f, x) => f.Internet.Email(x.FirstName, x.LastName, provider: "example.se")) // depends on the object
    .With(x => x.Address, Fake.For<Address>().Locale("de"))        // nested generator
    .Unique(x => x.Email)
    .Ignore(x => x.InternalNotes)
    .Generate(100);                                                // also: Generate(), Stream(), GenerateParallel(n)
```

- **Supported shapes**: classes with a parameterless constructor, records and classes with constructor parameters (the constructor with most parameters is used), `init` and `required` members, public fields, structs, nested objects, interfaces and abstract classes (through [subtypes](#interfaces-and-subtypes)), `T[]`, `List<T>` and its interfaces, `HashSet<T>`, `Dictionary<K,V>`, enums, nullable types, `byte[]`, and `Guid`, `DateTime(Offset)`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri` plus every numeric type.
- **Cycles** (`Customer → Order → Customer`) stop with `null`. `MaxDepth(n)` (default 4) bounds nesting, `CollectionSize(min, max)` (default 1–3) sizes collections, `NullProbability(p)` (default 0) leaves nullable members null.
- **Generators are immutable**: every method returns a new generator, so you can share them and derive variants: `var admins = users.With(u => u.Role, "Admin");`.
- **Order**: constructor arguments, then members in declaration order, then rules that depend on the object (in the order added), then `AfterCreate` actions. `CreateWith(f => new Customer(...))` replaces construction.
- **Strict mode**: `.Strict()` throws when a scalar member has neither a rule, a convention nor a data annotation that defines it, so new members can't silently get meaningless data. The included analyzer reports the same at compile time (**PHONEY004**) for chains like `Fake.For<T>().Strict()…Generate()`, so a new member breaks the build instead of the test run. It can't see app-wide `Fake.Conventions` (registered at runtime): cover such members with `.Ignore(...)`/`.With(...)` or suppress the warning.

### Interfaces and subtypes

Members declared as an interface, an abstract class or a base class are generated as one of their subtypes:

```csharp
var drawings = Fake.For<Drawing>()
    .Subtype<IShape, Circle>()               // every IShape in the object graph is a Circle
    .Subtype<IAnimal, Cat, Dog>()            // one of Cat or Dog, picked at random (reproducible with a seed)
    .With(d => d.Logo, Fake.For<Square>())   // one member with its own generator of a subtype
    .Generate(10);

Fake.Subtypes.Add<IShape, Circle, Square>(); // app-wide, for generators created afterwards
Fake.Subtypes.Remove<IShape>();              // or Clear() for all
```

Up to four subtypes fit the type arguments; pass more as types: `.Subtype<IShape>(typeof(Circle), typeof(Square), …)`. A subtype must be concrete and assignable to the base type, otherwise you get an `ArgumentException` saying which.

- **`[JsonDerivedType]` works out of the box**: an interface or abstract class that lists its derived types for System.Text.Json uses them as its default subtypes.
- **Precedence**: the generator's `Subtype`, then `Fake.Subtypes`, then `[JsonDerivedType]`. An interface or abstract class with none of them stays `null`. A concrete base class is generated as itself unless you map it (`.Subtype<Vehicle, Truck>()`).
- `Populate` fills an existing object as its runtime type: a `Circle` in an `IShape` member keeps being a circle and gets its missing members.
- With the source generator, `[FakeSubtype<IShape, Circle>]` on the `[FakeFor]` class registers the subtype app-wide and generates its model; `[JsonDerivedType]` types get models automatically. In Native AOT apps every subtype needs a generated model (`[FakeFor<T>]`, `[FakeSubtype<,>]` or `[JsonDerivedType]`).

### Filling in existing objects

`Populate` fills in an object you already have, with the same conventions, rules and annotations as `Generate`:

```csharp
var order = Fake.Populate(new Order { CustomerId = 42 });         // CustomerId stays 42, everything else is filled
Fake.For<Customer>().Seed(1).With(x => x.Status, "VIP").Populate(existing);
generator.Populate(existing, PopulateMode.All);                   // regenerate every member
List<Customer> filled = generator.Populate(customers);            // many at once, each from its own place in the seeded sequence
```

- **`PopulateMode.MissingOnly`** (the default) fills members that hold their default (`null`, `0`, `false`, `Guid.Empty`, an empty string or collection) and keeps the others. A set nested object is populated member by member. `PopulateMode.All` replaces everything that is not ignored.
- Kept values shape the generated ones: with a set `FirstName`, `LastName`, `BirthDate` or sex/gender enum, the email, username, age and avatar are generated to match.
- Members you `Ignore` are never touched; rules that depend on the object and `AfterCreate` run afterwards; `CreateWith` is ignored because nothing is created. Records are filled through their `init` properties, and a struct is populated as a copy (use the return value).
- A `false` or `0` that you set on purpose counts as unset in `MissingOnly`: protect it with `.Ignore(x => x.IsActive)`.
- With a seed, `Populate(new T(), PopulateMode.All)` gives exactly what `Generate()` gives.

### Values that agree

Within one object, related members describe the same thing:

- **One person**: `FirstName`, `LastName`, `Email`, `Username`, `Avatar` and `Gender`/`Sex` enums belong together, and `Age` matches `BirthDate`/`DateOfBirth`.
- **One address**: `Street`, `City`, `ZipCode`, `State`, `Country` and `CountryCode` come from one address. For country locales (`de_AT`, `en_US`…) the country is the locale's own (`Österreich`, `AT`).
- **One timeline**: the first "created" date (`CreatedAt`, `RegisteredAt`, `StartDate`…) is when the object was created, "updated" dates (`UpdatedAt`, `LastLogin`…) fall between it and the reference date, and future dates (`ExpiresAt`, `DueDate`, `EndDate`) come after both.

Across objects, `WithOneOf` links graphs to data you generated already:

```csharp
var customers = Fake.Many<Customer>(10);
var orders = Fake.For<Order>()
    .WithOneOf(o => o.CustomerId, customers, c => c.Id)   // an existing customer's id
    .WithOneOf(o => o.Status, [OrderStatus.Paid, OrderStatus.Shipped])
    .Generate(100);
```

### Validation attributes

Generated objects satisfy their data annotations, so they pass `Validator.TryValidateObject`, EF Core and ASP.NET Core validation:

| Annotation | Effect |
|---|---|
| `[StringLength]`, `[MinLength]`, `[MaxLength]`, `[Length]` | string length; element count for collections |
| `[Range]` | numbers and dates within the range (`[Range(typeof(DateTime), "2020-01-01", "2020-12-31")]`) |
| `[RegularExpression]` | a value matching the pattern |
| `[EmailAddress]`, `[Phone]`, `[Url]`, `[CreditCard]`, `[Base64String]` | a value of that format, whatever the member is called |
| `[AllowedValues]`, `[DeniedValues]` | one of / none of the listed values |
| `[Required]` | never `null` (even with `NullProbability`) and never empty |

Annotations work the same with reflection and with the source generator, on properties, fields and record parameters (`[property: Range(1, 5)]`).

### Conventions

Member names (case and `_` ignored, trailing words matched, so `BillingCity` is a city and `HomeEmailAddress` an email) pick the generator, for example:

| Member | Value |
|---|---|
| `FirstName`, `LastName`, `FullName`, `Name` on a person-like type | names of one generated identity |
| `Email`, `Username`, `Avatar`, `Gender`/`Sex` enums | consistent with that identity |
| `Street`, `Address1`, `City`, `Zip`/`PostalCode`, `State`, `Country`, `CountryCode` | one address in the locale |
| `Phone`, `Mobile`, `Company`, `JobTitle`, `Department`, `ProductName`, `Description`, `Notes` | matching module |
| `Url`, `Website`, `Ip`, `Mac`, `UserAgent`, `Iban`, `Bic`, `CreditCard`, `Isbn`, `Vin`, `Sku`, `Token`, `Version` | matching module |
| `Id` (`int`/`long`) | 1, 2, 3… in generation order; `CustomerId` is a random reference |
| `Age` and `BirthDate`/`DateOfBirth` | matching each other |
| `Price`/`Amount`/`Total`, `Quantity`, `Rating`, `Year`, `Percentage`, `Latitude` | sensible ranges |
| `CreatedAt`, `UpdatedAt`, `ExpiresAt`/`DueDate` | an ordered timeline: past, recent, future |

`Name` and `Title` look at the declaring type (`Company.Name` is a company name, `Product.Name` a product, `Book.Title` a title). Anything else gets a value based on its type. Add your own:

```csharp
Fake.Conventions.Add<string>(name => name.EndsWith("Sku"), f => f.Random.Replace("SKU-#####")); // app-wide
Fake.For<Order>().WithConvention<decimal>(n => n == "Vat", f => 0.25m);                          // one generator
```

Both apply to every object in the graph, so `Order.Lines[0].Sku` is covered as well as `Order.Sku`. A generator's own conventions come before the app-wide ones, and explicit rules (`With`, `Ignore`…) come before both.

### NodaTime

The `Phoney.NodaTime` package adds NodaTime values and NodaTime members:

```bash
dotnet add package Phoney.NodaTime
```

```csharp
Instant created = faker.Noda().Instant.Past();
LocalDate birthday = faker.Noda().LocalDate.Birthdate();
var subscriptions = Fake.Many<Subscription>(10); // Instant CreatedAt, LocalDate BirthDate… follow the conventions
```

`Instant`, `OffsetDateTime`, `ZonedDateTime`, `LocalDateTime`, `LocalDate`, `LocalTime` and `Duration` members are generated like their built-in counterparts (`DateTimeOffset`, `DateTime`, `DateOnly`, `TimeOnly`, `TimeSpan`), so `CreatedAt`, `ExpiresAt`, `BirthDate` and `Age` agree; this works with reflection and with source-generated models. See the [package README](src/Phoney.NodaTime/README.md).

## Source generator and Native AOT

`Fake.For<T>()` uses reflection (with compiled delegates) unless a generated model exists. Declare the types you generate:

```csharp
[FakeFor<Customer>, FakeFor<Order>]
internal static partial class TestData;

var customers = TestData.Customer.Seed(1).Generate(100); // a Generator<Customer>, no reflection
```

The generator (included in the package) writes a model for each listed type and every type it contains, and registers them at startup, so `Fake.For<Customer>()` uses them as well. Generated and reflection models produce **identical data for the same seed**. With generated models Phoney is trim and Native AOT safe; reflection entry points are annotated (`RequiresUnreferencedCode`/`RequiresDynamicCode`) so the compiler tells you where they're used.

Subtypes of interfaces and abstract classes need models too: types listed with `[JsonDerivedType]` on the base type get them automatically, and `[FakeSubtype<IShape, Circle>]` on the same class generates a model for `Circle` and registers it as a subtype app-wide (see [Interfaces and subtypes](#interfaces-and-subtypes)). NodaTime members are generated without reflection when `Phoney.NodaTime` is referenced.

The package also reports these diagnostics:

| Id | Severity | Meaning |
|---|---|---|
| PHONEY001 | Error | the class with `[FakeFor]` must be `partial` |
| PHONEY002 | Warning | a type can't be generated (abstract, no public constructor, inaccessible); its members stay default |
| PHONEY003 | Error | `[FakeFor]` is inside a generic type |
| PHONEY004 | Warning | a `.Strict()` generator has scalar members without a rule, convention or annotation |

## Dependency injection and ASP.NET Core

The `Phoney.DependencyInjection` package registers fakers and generators in `IServiceCollection`:

```bash
dotnet add package Phoney.DependencyInjection
```

```csharp
builder.Services.AddPhoney(o => { o.Locale = "sv"; o.Seed = 42; })
    .AddGenerator<Customer>(g => g.With(c => c.Country, "SE"))  // singleton Generator<Customer>
    .AddGenerator(TestData.Order);                              // source-generated: no reflection, AOT safe

app.MapGet("/demo/customers", (Generator<Customer> customers) => customers.Generate(10));
app.MapGet("/demo/name", (IFaker faker) => faker.Person.FullName());
```

- `IFaker`/`Faker` are scoped, `IFakerFactory` creates fakers for other locales and seeds, and generators are singletons. `IFaker` itself is in the core package (implemented by `Faker`), for your own abstractions and test doubles.
- `PhoneyOptions`: `Locale`, `Seed`, `ReferenceDate` and `TimeProvider`.
- With `Seed`, every scope starts at the seed, so each request or test scope is reproducible.
- `ReferenceDate`, or a `TimeProvider` (from the options or registered in the container, e.g. `FakeTimeProvider`), sets "now".
- In integration tests, call `AddPhoney(o => o.Seed = 1)` again in `WebApplicationFactory.ConfigureTestServices`; it configures the same options.

## EF Core seeding

The `Phoney.EntityFrameworkCore` package turns generated data into `HasData` seed data:

```bash
dotnet add package Phoney.EntityFrameworkCore
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    var customers = modelBuilder.Entity<Customer>().HasFakeData(50);
    modelBuilder.Entity<Order>().HasFakeData(200, g => g.With(o => o.Status, OrderStatus.Paid));
}
```

- The data is the same on every model build (the seed is derived from the entity name), so migrations stay stable.
- Navigations and owned objects are left empty, strings respect `HasMaxLength`, a single integer key is numbered 1…n when values clash, and foreign keys are pointed at seeded principals (seed principals first, or link with `WithOneOf`).
- Call it after the entity's other configuration; it reads keys, maximum lengths and navigations from the model.

## Test data in TUnit

The `Phoney.TUnit` package adds `[FakeData]`, which fills [TUnit](https://tunit.dev) test parameters the way Phoney fills the members of an object:

```bash
dotnet add package Phoney.TUnit
```

```csharp
[Test, FakeData]
public async Task Places_order(Customer customer, string email, [Range(1, 10)] int quantity) { … }

[Test, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
public async Task Formats_address(string street, string city, string zipCode) { … }
```

- Parameters are filled by name and type, objects and collections recursively, and data annotations on parameters are honoured.
- The parameters of one test case agree like the members of one object: `firstName`, `email`, `birthDate` and `age` describe the same person, `street`, `city` and `zipCode` one address.
- **Reproducible by default**: the seed is derived from the test's class, method and parameters, so every run and machine gets the same values (and the same test names). Set `Seed = 42` for other values.
- Options: `Count`, `Seed`, `Locale`, `NullProbability` and `ReferenceDate`. It also works on the test class (constructor parameters) and on `required` properties.
- Interface and abstract parameters get one of their subtypes from `Fake.Subtypes` or `[JsonDerivedType]`.
- Test data is created while TUnit discovers tests, so register custom conventions (`Fake.Conventions.Add`) and subtypes (`Fake.Subtypes.Add`) in a `[Before(TestDiscovery)]` hook; `[Before(Assembly)]` and later hooks run too late.

The same machinery is available to other frameworks through `Fake.Arguments(method)`, which returns rows of values for a parameter list.

## Locales

```csharp
Locales.All;                      // 77 locales with code, title, endonym, script, fallback chain
new Faker("de-AT");               // de_AT → de → en → base
new Faker(CultureInfo.CurrentCulture); // "sv-SE" → sv, "sr-Latn-RS" → sr_RS_latin
Fake.Locale = "fr";               // default for Fake.* and generators
```

<details>
<summary>Supported locales</summary>

<!-- locales:start -->
76 locales from faker.js 10.6.0. Codes accept `-` or `_` and any casing (`de-AT`, `de_at`). .NET culture names resolve to the closest locale, including script-tagged ones (`sv-SE` → `sv`, `sr-Latn-RS` → `sr_RS_latin`, `zh-Hant-TW` → `zh_TW`, `ckb-IQ` → `ku_ckb`); a requested script is never substituted.

| Code | Language | Native name | Falls back to |
|---|---|---|---|
| `af_ZA` | Afrikaans (South Africa) | Afrikaans (Suid-Afrika) | en |
| `ar` | Arabic | اَلْعَرَبِيَّةُ | en |
| `az` | Azerbaijani | azərbaycan dili | en |
| `bn_BD` | Bengali (Bangladesh) | বাংলা (বাংলাদেশ) | en |
| `cs_CZ` | Czech (Czechia) | čeština (Česká republika) | en |
| `cy` | Welsh | Cymraeg | en |
| `da` | Danish | Dansk | en |
| `de` | German | Deutsch | en |
| `de_AT` | German (Austria) | Deutsch (Österreich) | de → en |
| `de_CH` | German (Switzerland) | Deutsch (Schweiz) | de → en |
| `dv` | Maldivian | ދިވެހި | en |
| `el` | Greek | Ελληνικά | en |
| `en` | English | English | — |
| `en_AU` | English (Australia) | English (Australia) | en |
| `en_AU_ocker` | English (Australia Ocker) | English (Australia) | en_AU → en |
| `en_BORK` | English (Bork) | English (Bork) | en |
| `en_CA` | English (Canada) | English (Canada) | en |
| `en_GB` | English (Great Britain) | English (Great Britain) | en |
| `en_GH` | English (Ghana) | English (Ghana) | en |
| `en_HK` | English (Hong Kong) | English (Hong Kong) | en |
| `en_IE` | English (Ireland) | English (Ireland) | en |
| `en_IN` | English (India) | English (India) | en |
| `en_NG` | English (Nigeria) | English (Nigeria) | en |
| `en_NP` | English (Nepal) | English (Nepal) | en |
| `en_US` | English (United States) | English (United States) | en |
| `en_ZA` | English (South Africa) | English (South Africa) | en |
| `eo` | Esperanto | Esperanto | en |
| `es` | Spanish | Español | en |
| `es_MX` | Spanish (Mexico) | Español (México) | es → en |
| `fa` | Farsi/Persian | فارسی | en |
| `fi` | Finnish | suomi | en |
| `fr` | French | Français | en |
| `fr_BE` | French (Belgium) | Français (Belgique) | fr → en |
| `fr_CA` | French (Canada) | Français (Canada) | fr → en |
| `fr_CH` | French (Switzerland) | Français (Suisse) | fr → en |
| `fr_LU` | French (Luxembourg) | Français (Luxembourg) | fr → en |
| `fr_SN` | French (Senegal) | Français (Sénégal) | fr → en |
| `he` | Hebrew | עברית | en |
| `hr` | Croatian | Hrvatski | en |
| `hu` | Hungarian | magyar | en |
| `hy` | Armenian | Հայերեն | en |
| `id_ID` | Indonesian (Indonesia) | Bahasa Indonesia (Indonesia) | en |
| `it` | Italian | Italiano | en |
| `ja` | Japanese | 日本語 | en |
| `ka_GE` | Georgian (Georgia) | ქართული (საქართველო) | en |
| `ko` | Korean | 한국어 | en |
| `ku_ckb` | Kurdish (Sorani) | کوردی (سۆرانی) | en |
| `ku_kmr_latin` | Kurdish (Kurmanji, Latin) | Kurdî (Kurmancî) | en |
| `lv` | Latvian | latviešu valoda | en |
| `mk` | Macedonian | македонски јазик | en |
| `mn_MN_cyrl` | Mongolian (Mongolia, Cyrillic) | Монгол (Монгол Улс, Кирилл) | en |
| `nb_NO` | Norwegian (Norway) | Norsk bokmål (Norge) | en |
| `ne` | Nepali | नेपाली | en |
| `nl` | Dutch | Nederlands | en |
| `nl_BE` | Dutch (Belgium) | Nederlands (België) | nl → en |
| `pl` | Polish | Polski | en |
| `pt_BR` | Portuguese (Brazil) | Português (Brasil) | en |
| `pt_PT` | Portuguese (Portugal) | Português (Portugal) | en |
| `ro` | Romanian | Română | en |
| `ro_MD` | Romanian (Moldova) | Română (Moldova) | ro → en |
| `ru` | Russian | Русский | en |
| `sk` | Slovak | slovenčina | en |
| `sl_SI` | Slovenian (Slovenia) | Slovensko (Slovenia) | en |
| `sr_RS_latin` | Serbian (Serbia, Latin) | srpski (Srbija, latinica) | en |
| `sv` | Swedish | Svenska | en |
| `ta_IN` | Tamil (India) | தமிழ் (இந்தியா) | en |
| `th` | Thai | ไทย | en |
| `tr` | Turkish | Türkçe | en |
| `uk` | Ukrainian | Українська | en |
| `ur` | Urdu | اردو | en |
| `uz_UZ_latin` | Uzbek (Uzbekistan, Latin) | O'zbekcha | en |
| `vi` | Vietnamese | Tiếng Việt | en |
| `yo_NG` | Yoruba (Nigeria) | Yoruba (Naijiria) | en |
| `zh_CN` | Chinese (China) | 中文 (中国) | en |
| `zh_TW` | Chinese (Taiwan) | 中文 (臺灣) | en |
| `zu_ZA` | Zulu (South Africa) | isiZulu (Iningizimu Afrika) | en |
<!-- locales:end -->

</details>

Fallback works like faker.js: each data group (`person.first_name`, `location.city_pattern`…) comes from the first locale in the chain that defines it; data a locale marks as not applicable (e.g. name prefixes in Azerbaijani) is never borrowed from another locale.

### Custom data

```csharp
// A new locale on top of an existing one
Locales.Register("sv_dalarna", l => l
    .FallbackTo("sv")                                  // sv → en → base
    .Cities("Mora", "Falun", "Rättvik")
    .FirstNames(Sex.Female, "Kerstin", "Margit")      // male names still come from sv
    .EmailDomains("dalarna.example")
    .PhoneFormats("0250-## ## ##"));

// Extra or replaced data in an existing locale
Locales.Extend("en", l => l.CompanyNames("{{Person.LastName}} & Sons", "Acme {{Commerce.Department}}"));
```

Typed methods cover the common data: `FirstNames`, `LastNames`, `JobTitles`, `Cities`, `Streets`, `States`, `Countries`, `ZipCodeFormats`, `PhoneFormats`, `EmailDomains`, `CompanyNames` and `LoremWords`. Each replaces only that data; everything else is inherited. For anything else, use a faker.js data path, or import a faker.js locale file:

```csharp
Locales.Extend("en", l => l
    .Set("commerce.department", "Toys", "Garden")
    .Set("person.nickname", "Ace", "Buddy"));          // new paths are allowed: faker.Pick("person.nickname")

Locales.Register("en_PIRATE", File.OpenRead("pirate.json"), "en");
```

## Performance

Locale data is compiled ahead of time into compact Brotli-compressed resources (about 1 MB for all locales), loaded lazily per locale. Fallback chains are resolved once at load into a flat array, so a lookup is one array index and picking a name returns a stored string without allocating. Templates are parsed once and cached.

Indicative numbers (Apple Silicon, .NET 10, BenchmarkDotNet short run):

| Operation | Time | Allocated |
|---|---:|---:|
| `Person.FirstName()` | 3.6 ns | 0 B |
| `Randomizer.Int(0, 1000)` | 2.2 ns | 0 B |
| `Helpers.FromRegExp("[A-Z]{3}-\\d{4}")` | 52 ns | 144 B |
| `Parse("{{person.firstName}} {{person.lastName}} lives in {{location.city}}")` | 174 ns | 418 B |
| `Person.FullName()` | 186 ns | 348 B |
| `Finance.Iban()` | 304 ns | 388 B |
| 1,000 customers with address and 1–3 orders | 1.8 ms | 2 MB |
| … with `GenerateParallel` | 0.96 ms | 2.8 MB |
| Loading the `en` locale (once per process) | 3.9 ms | 1.4 MB |

Run the benchmarks with:

```bash
dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*'
```

## How the faker.js data gets in

```
@faker-js/faker (npm, pinned)  →  tools/extract (Node)      →  data/raw/*.json
data/raw/*.json                →  tools/Phoney.DataCompiler  →  src/Phoney/Resources/Locales/*.bin.br
                                                               src/Phoney/Data/Generated/*.g.cs
                                                               data/report.md
```

The compiler flattens faker.js definitions into typed entries, builds a key catalog (so modules use integer keys), and **validates every template** against faker.js' method list and each locale's fallback chain; broken references fail the build. A test checks that every faker.js method referenced by locale data is implemented.

The **Sync faker.js data** workflow runs weekly: when npm has a newer `@faker-js/faker` it re-imports, recompiles and tests, then opens a pull request with the raw JSON diff and the data report, or an issue if the new data needs code changes. To update manually:

```bash
cd tools/extract && npm install --save-exact @faker-js/faker@latest && node export-locales.mjs ../../data/raw && cd ../..
dotnet run --project tools/Phoney.DataCompiler
dotnet test
```

## Building

```bash
dotnet build
dotnet test
dotnet publish tests/Phoney.AotSmoke -c Release   # Native AOT smoke test
```

## License

MIT. Locale data and parts of the generation logic come from [faker.js](https://github.com/faker-js/faker) (MIT); see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
