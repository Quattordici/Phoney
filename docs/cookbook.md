# Cookbook

Short answers to concrete jobs. Every recipe uses the same small shop model (`Customer`, `Order`, `OrderLine`, `Product`, `Signup`; see [`DocShop.cs`](../tests/Shared/DocShop.cs)) and runs as a test, so you can paste it and change the names.

- [A database-ready aggregate](#a-database-ready-aggregate)
- [Data that refers to other data](#data-that-refers-to-other-data)
- [Testing a validator against every edge](#testing-a-validator-against-every-edge)
- [Finding the input that breaks your code](#finding-the-input-that-breaks-your-code)
- [One configuration for the whole test suite](#one-configuration-for-the-whole-test-suite)
- [A generator that fails when the model grows](#a-generator-that-fails-when-the-model-grows)
- [A fixture file you can edit by hand](#a-fixture-file-you-can-edit-by-hand)
- [Stand-ins for production data](#stand-ins-for-production-data)

## A database-ready aggregate

Generated graphs look real, but their foreign keys are random until you say where they come from. Say it once per relation, and close the navigation cycle while you are at it:

<!-- snippet: recipe-aggregate -->
```csharp
var customers = Fake.For<Customer>().Seed(1)
    .CollectionSize(1, 4)
    .WithParentKey(c => c.Orders.Each().CustomerId, c => c.Id)                    // order.CustomerId == customer.Id
    .WithParentKey(c => c.Orders.Each().Lines.Each().OrderId, (Order o) => o.Id)   // line.OrderId == its order's Id
    .WithBackReferences()                                                          // order.Customer is the customer itself
    .Generate(50);
```
<!-- end snippet -->

Neither option draws random numbers, so the names, emails and totals are exactly those of the same seed without them. Feed the result to EF Core, `DataExport` or an API mock and the joins hold. For `HasData` seeding, `Phoney.EntityFrameworkCore` reads keys and navigations from the EF model instead ([EF Core seeding](../README.md#ef-core-seeding)).

## Data that refers to other data

Generate the lookup data first, then pick from it. `WithOneOf` takes a list, a list and a selector, or values with weights:

<!-- snippet: recipe-lookups -->
```csharp
var products = Fake.For<Product>().Seed(2).Generate(20);

var orders = Fake.For<Order>().Seed(3)
    .WithOneOf(o => o.Lines.Each().Sku, products, p => p.Sku)          // every line sells a product that exists
    .WithOneOf(o => o.Status, (OrderStatus.Paid, 70), (OrderStatus.Shipped, 25), (OrderStatus.Cancelled, 5))
    .Generate(1_000);
```
<!-- end snippet -->

`o.Lines.Each().Sku` is a path rule: it reaches into every line of every order without a generator for `OrderLine`. Weights are relative, a weight of zero is never picked, and `WithSubsetOf` does the same for collections (two of these roles, never the same one twice).

When a member takes more than one rule, select it once with `Member`: `.Member(o => o.Status, m => m.OneOf((OrderStatus.Paid, 70), (OrderStatus.Shipped, 30)).Where(s => s != OrderStatus.Shipped || allowShipped))` gives the same orders as `WithOneOf` followed by `Where`, and the lambda only offers the rules the member's type can take ([One member, several rules](../README.md#one-member-several-rules)).

## Testing a validator against every edge

The data annotations that make generated objects valid also tell Phoney how to break them. One loop tests every way to break every annotated member, and another every value that sits exactly on a limit:

<!-- snippet: recipe-validator -->
```csharp
static bool IsValid(object value) =>
    Validator.TryValidateObject(value, new ValidationContext(value), null, validateAllProperties: true);

foreach (var broken in Fake.For<Signup>().Seed(1).InvalidCases())       // "Age: below the minimum (18)"…
    IsValid(broken.Value).ShouldBeFalse(broken.ToString());

foreach (var edge in Fake.For<Signup>().Seed(1).BoundaryCases())        // "Name: the maximum length (40)"…
    IsValid(edge.Value).ShouldBeTrue(edge.ToString());
```
<!-- end snippet -->

Each case is the valid object of that seed with one member changed, so a failure points at one rule. Swap `IsValid` for your FluentValidation validator or your API's model binding, or turn the cases into test cases of their own with your framework's data source (`MethodDataSource`, `MemberData`, `TestCaseSource`, `DynamicData`).

## Finding the input that breaks your code

When you can say what must *always* hold, let Phoney look for the exception:

<!-- snippet: recipe-property -->
```csharp
Fake.For<Order>().Seed(5).CollectionSize(0, 6)
    .Check(o => o.Total < 2_000);       // throws FakeCheckException<Order> with the simplest order of 2,000 or more
```
<!-- end snippet -->

The failing order is shrunk to the simplest one your generator can make that still fails (fewer lines, lower quantities, the first SKU), and it still obeys your rules and annotations. Leave out `Seed` to try other orders on every run; the failure prints the seed that replays it. In a test method, `[FakeCheck]` does the same for the test's parameters.

## One configuration for the whole test suite

When every test wants employees in a real department, say so once instead of in every test:

<!-- snippet: recipe-configure -->
```csharp
// once, at startup: a module initializer or a test hook that runs before discovery
Fake.Generators.Configure<Employee>(g => g
    .With(e => e.Department, f => f.Random.Element(["Sales", "Support", "Research"]))
    .Unique(e => e.Email));

var team = Fake.Many<Employee>(10);      // Fake.One, Many, nested members and [FakeData] parameters all use it
```
<!-- end snippet -->

The configuration applies wherever an `Employee` is made, nested in other objects too. Settings that aren't about one type (collection sizes, strict mode, back-references) go in `Fake.Defaults`. Both are process-wide, so set them before tests run rather than inside one.

## A generator that fails when the model grows

Conventions give a new member a plausible value, which is usually right. When it isn't, make the generator say so:

<!-- snippet: recipe-strict -->
```csharp
var signups = Fake.For<Signup>().Strict();   // fails (and PHONEY004 warns at compile time) once a member gets no rule, convention or annotation
```
<!-- end snippet -->

A member counts as covered when a rule, a name convention or a data annotation decides its value; `.Ignore(x => x.Member)` covers it explicitly. The IDE offers to add the `Ignore`s for you.

## A fixture file you can edit by hand

Generate a file once, tweak the rows that matter, check it in, and let the generator fill everything else around it:

<!-- snippet: recipe-fixture -->
```csharp
string csv = Fake.For<Customer>().Seed(1).ToCsv(100, columns: ["Id", "FirstName", "LastName", "Email"]);
// … edit it by hand, check it in, then fill those members from it while the rest is generated:
var customers = Fake.For<Customer>().WithFeed(DataFeed.FromCsv(csv)).Generate(100);
```
<!-- end snippet -->

Columns match members by name (`first_name` fills `FirstName`), a value that doesn't fit fails with its row and column when the feed is first used, and a rule for a member still wins over its column. `ToJson`, `WriteCsvFile` and `DataFeed.FromJsonFile` do the same with JSON and files.

## Stand-ins for production data

To copy production data into a test system without the personal details, replace each person with a fake one that is tied to something stable, such as the email. The same real person gets the same stand-in in every table and every run, so joins still work:

<!-- snippet: recipe-pseudonymize -->
```csharp
var masked = Fake.For<Customer>()
    .Ignore(c => c.Id)                                  // keep what isn't personal
    .Pseudonymize(production, c => c.Email);            // one real email, one fake person, in every table and every run
```
<!-- end snippet -->

The stand-in's names, email, address and birthdate agree with each other. Anyone who knows a real email can compute its stand-in, so when that matters, key on an HMAC of the email with a secret kept outside the data. [Choosing a seed](seeds.md#data-tied-to-a-key) has the other keyed tools.
