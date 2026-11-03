# Coming from Bogus

[Bogus](https://github.com/bchavez/Bogus) and Phoney share their roots: both draw on the locale data of faker.js, so names, cities and lorem look familiar. The difference is where the work goes. In Bogus you write a rule for every member; in Phoney you write a rule for the members whose values *matter to the test*, and conventions, data annotations and one shared identity per object fill in the rest.

## The same generator, twice

A typical Bogus faker:

```csharp
// Bogus
var lineFaker = new Faker<OrderLine>()
    .RuleFor(l => l.Sku, f => f.Random.Replace("???-#####").ToUpperInvariant())
    .RuleFor(l => l.Quantity, f => f.Random.Int(1, 10))
    .RuleFor(l => l.Price, f => f.Random.Decimal(0.01m, 1000m));

var orderFaker = new Faker<Order>()
    .UseSeed(42)
    .RuleFor(o => o.Id, f => f.IndexFaker + 1)
    .RuleFor(o => o.Reference, f => f.Random.Replace("ORD-#####"))
    .RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>())
    .RuleFor(o => o.CreatedAt, f => f.Date.PastOffset())
    .RuleFor(o => o.Lines, f => lineFaker.Generate(f.Random.Int(1, 3)))
    .FinishWith((f, o) => created.Add(o.Id));

var some = orderFaker.Generate(10);
```

In Phoney, with `[RegularExpression]` and `[Range]` on `OrderLine` (annotations your API validates anyway; see [`DocShop.cs`](../tests/Shared/DocShop.cs)):

<!-- snippet: bogus-translated -->
```csharp
var orders = Fake.For<Order>().Seed(42)
    .With(o => o.Reference, f => f.Random.ReplaceSymbols("ORD-#####"))
    .AfterCreate((f, o) => created.Add(o.Id));
// Id counts 1, 2, 3…, Status, CreatedAt and the lines come from conventions and annotations

var some = orders.Generate(10);
```
<!-- end snippet -->

One rule is left, for the one member whose format only you know.

## Rule by rule

| Bogus | Phoney |
|---|---|
| `new Faker<T>()` | `Fake.For<T>()` (reflection) or `Fake.ForGenerated<T>()` / a `[FakeFor<T>]` model (reflection-free, Native AOT) |
| `.RuleFor(x => x.M, f => …)` | `.With(x => x.M, f => …)`, or nothing when a convention already fits |
| `.RuleFor(x => x.M, (f, x) => …)` | `.With(x => x.M, (f, x) => …)`, run after the members the object is built from |
| `.RuleFor(x => x.M, "value")` | `.With(x => x.M, "value")` |
| `.RuleFor(x => x.Child, f => childFaker.Generate())` | `.With(x => x.Child, childGenerator)`, or `.WithGeneratorFor(childGenerator)` for every `Child` in the graph |
| `.RuleFor(x => x.Lines, f => lineFaker.Generate(3))` | `.CollectionSize(3, 3)`, and path rules such as `.With(x => x.Lines.Each().Sku, …)` for the elements |
| `.RuleForType(typeof(DateTime), f => …)` | `.WithEveryMemberOfType<DateTime>(f => …)` |
| `.Ignore(x => x.M)` | `.Ignore(x => x.M)` |
| `.StrictMode(true)` | `.Strict()`, which accepts conventions and annotations as coverage and is also checked at compile time (PHONEY004) |
| `.CustomInstantiator(f => new T(…))` | `.CreateWith(f => new T(…))`; constructors with parameters work without it |
| `.FinishWith((f, x) => …)` | `.AfterCreate((f, x) => …)` |
| `.RuleSet("vip", set => …)` and `Generate("default,vip")` | a derived generator: `var vip = customers.With(…)`, since generators are immutable |
| `.UseSeed(42)` | `.Seed(42)` |
| `Randomizer.Seed = new Random(42)` | `Fake.Seed(42)` app-wide, or `using var _ = Fake.Seeded(42);` for one block |
| `.UseDateTimeReference(date)` | `.ReferenceDate(date)` or `.TimeProvider(clock)` |
| `.Generate(n)`, `.GenerateLazy(n)`, `.GenerateForever()` | `.Generate(n)`, `.Stream().Take(n)`, `.Stream()`; also `.GenerateParallel(n)` |
| `.Populate(existing)` | `.Populate(existing)`, which keeps the members you already set |
| `f.IndexFaker`, `f.UniqueIndex` | the `Id` convention (1, 2, 3…), or `.Sequence(x => x.Code, i => $"C{i + 1:D4}")` |
| `f.PickRandom(list)` | `.WithOneOf(x => x.M, list)`, or `f.Random.Element(list)` inside a rule |
| `f.Random.WeightedRandom(values, weights)` | `.WithOneOf(x => x.M, values, weights)` or `(value, weight)` tuples |
| `f.Random.ListItems(list, n)` | `.WithSubsetOf(x => x.M, list, n)` |
| `f.Random.Replace("##??")` | `f.Random.ReplaceSymbols("##??")` |
| `f.Name.FirstName()`, `f.Address.City()`, `f.Phone.PhoneNumber()` | `f.Person.FirstName()`, `f.Location.City()`, `f.Phone.Number()` |
| `f.Company.CompanyName()` | `f.Company.Name()` |
| `f.Person` (a coherent person) | `f.Person.Profile()`, and inside an object the members already agree |
| `f.Parse("{{name.lastName}}, {{name.firstName}}")` | `f.Parse("{{person.lastName}}, {{person.firstName}}")`: current faker.js names, case-insensitive, or the .NET ones (`{{Person.LastName}}`) |

## Variants instead of rule sets

A Phoney generator never changes: each method returns a new one. That makes variants cheap and safe to share between tests:

<!-- snippet: bogus-rule-sets -->
```csharp
var orders = Fake.For<Order>().Seed(1);
var cancelled = orders.With(o => o.Status, OrderStatus.Cancelled);   // orders itself is unchanged
var bulk = orders.CollectionSize(20, 50);
```
<!-- end snippet -->

The analyzer warns (**PHONEY005**) when a result is thrown away, as in `orders.With(...);` on its own line, which in Bogus would have changed `orders`.

## Things that behave differently

- **Seeded dates don't follow the clock.** With a seed, "past" and "future" are measured from 2025-01-01 UTC, so seeded data is the same every day. Set `ReferenceDate` or a `TimeProvider` to measure from now. See [Choosing a seed](seeds.md#fixed-data).
- **Item *n* is fixed.** Each object has a seed of its own, so the 500th customer is the same whether you generate 500 or a million, in a list, a stream or in parallel.
- **The members of one object agree.** First name, last name, email, username, birthdate, age, sex, national id and address describe one person; a "created" date comes before "updated". Rules you give take part: `With(x => x.Age, 30)` gives a birthdate 30 years back.
- **Annotations are rules.** `[Range]`, `[StringLength]`, `[EmailAddress]`, `[RegularExpression]`, `[Required]` and their friends are honoured, so generated objects pass validation, and `InvalidCases()` breaks them one at a time on purpose.
- **Every type has a value.** Records, `init` and `required` members, constructors with parameters, nested objects, collections of every kind, interfaces through subtypes or `[JsonDerivedType]`. Nothing has to be registered before `Fake.One<T>()` works.

## Moving over gradually

The two libraries live side by side without trouble. A pragmatic order:

1. Replace the fakers of simple types with `Fake.For<T>()` and delete the rules that a convention now covers. Keep the ones that encode business formats.
2. Add `.Strict()` to the generators whose data matters, so a new member can't slip through unnoticed.
3. Move test parameters to `[FakeData]` and random-data tests to `[FakeSeed]`, which prints the seed of a failing run.
4. When you are ready for Native AOT or trimming, add `[FakeFor<T>]` for the types you generate. The analyzer (**PHONEY012**) points at the calls still using reflection.
