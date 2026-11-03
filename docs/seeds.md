# Choosing a seed

Phoney's data is random the way a shuffled deck is random: a seed decides the order, and the same seed deals the same cards on every OS, CPU, .NET version and day of the week. Most of the API is about *which* seed a piece of data comes from. Pick the row that matches what you want:

| You want | Use | The data is the same… |
|---|---|---|
| fixed data from one generator or faker | `.Seed(42)`, `new Faker("sv", seed: 42)` | on every run and machine |
| fixed data from `Fake.*` inside a block | `using var _ = Fake.Seeded(42);` | in that block, even with tests running in parallel |
| the whole process seeded | `Fake.Seed(42)` | on every thread, from the start of the sequence |
| parallel work that doesn't depend on thread timing | `Fake.Fork(i)`, `GenerateParallel(n)` | per index `i` |
| a stand-in for a real-world id | `Fake.Keyed(key)`, `GenerateFor(key)`, `Fake.ForKey(key)` | for that key, whatever ran before it |
| test parameters that never change | `[FakeData]` | for that test, derived from its name |
| new data every run, replayable when a test fails | `[FakeSeed]`, `FakeSeed.Run` | when you pin the seed the failure prints |
| a search for the input that breaks your code | `[FakeCheck]`, `.Check(...)` | when you pin the seed the failure prints, shrinking included |
| every test of a run moved to other data | `PHONEY_SEED=123 dotnet test` | for that value |

## Fixed data

A seed fixes values *and* dates:

<!-- snippet: seed-generator -->
```csharp
var first = Fake.For<Customer>().Seed(42).Generate(3);
var again = Fake.For<Customer>().Seed(42).Generate(3);   // the same three customers, dates included
```
<!-- end snippet -->

Each object gets its own seed, derived from the generator's seed and the object's position. So item *n* doesn't care how you ask for it: one by one, in a batch, streamed or in parallel.

<!-- snippet: seed-item-n -->
```csharp
var listed = Fake.For<Customer>().Seed(42).Generate(1_000);
var parallel = Fake.For<Customer>().Seed(42).GenerateParallel(1_000);
var streamed = Fake.For<Customer>().Seed(42).Stream().Take(1_000).ToList();
// listed[500], parallel[500] and streamed[500] are the same customer
```
<!-- end snippet -->

"Now" is part of the data. A seeded generator measures its past and future dates, birthdates and ages from `Faker.DefaultSeededReferenceDate` (2025-01-01 UTC) rather than the clock, so a snapshot test written today still passes next year:

<!-- snippet: seed-dates -->
```csharp
var customer = Fake.For<Customer>().Seed(1).Generate();
// customer.CreatedAt is before 2025-01-01 (Faker.DefaultSeededReferenceDate) on every day you run this

var current = Fake.For<Customer>().Seed(1).ReferenceDate(DateTimeOffset.UtcNow).Generate();   // opt back in to "now"
```
<!-- end snippet -->

To follow a clock your tests already control, give the faker, the generator or the facade a `TimeProvider` (see [Controlling "now"](../README.md#controlling-now-with-timeprovider)).

## The `Fake` facade

`Fake.Person.FullName()`, `Fake.One<T>()` and friends draw from a faker that belongs to the current thread. A `Fake.Seeded` scope replaces it for the code that runs inside the scope, tasks it starts included, and nowhere else:

<!-- snippet: seed-scope -->
```csharp
using var seeded = Fake.Seeded(7);          // until the end of the block: tests in parallel keep their own
var name = Fake.Person.FullName();          // the same name on every run
var customer = Fake.One<Customer>();        // Fake.One, Many, For and Populate too
```
<!-- end snippet -->

The analyzer warns (**PHONEY006**) when a `Fake.Seeded(...)` result is never disposed, because everything after it would stay seeded.

## Parallel work

Workers started inside a seeded scope share its faker, so which worker draws first would decide who gets which value. A fork gives each item a faker of its own, derived from the seed and the item's index, never from the order the threads run in:

<!-- snippet: seed-fork -->
```csharp
var names = new string[100];
using (Fake.Seeded(7))
{
    Parallel.For(0, names.Length, i =>
    {
        using var fork = Fake.Fork(i);      // item i's own faker, from the seed and i
        names[i] = Fake.Person.FullName();
    });
}
```
<!-- end snippet -->

`GenerateParallel(n)` forks for you, and `faker.Fork(i)` does the same for a `Faker` you hold. The one thing that does depend on timing is `Unique(...)` in a parallel run: which of two equal values counts as the duplicate is decided by which thread gets there first.

## Data tied to a key

A seed gives you a *sequence*; a key gives you *one answer*. The same key gives the same values in every process, on every machine, whatever was generated before:

<!-- snippet: seed-key -->
```csharp
var byGenerator = Fake.For<Customer>().GenerateFor("customer:42");
var name = Fake.Keyed("customer:42").Person.FullName();

Customer byFacade;
using (Fake.ForKey("customer:42"))
    byFacade = Fake.One<Customer>();            // the same customer as byGenerator, in any process, on any machine
```
<!-- end snippet -->

That is what masking production data needs (`Pseudonymize`, see the [cookbook](cookbook.md#stand-ins-for-production-data)) and what API mocks need (`Phoney.WireMock` answers `/customers/42` with `GenerateFor` of the path). Keys are reproducible, not secret: when a stand-in must not lead back to the real id, key on an HMAC of it.

## Random, but replayable

Fixed data finds the bugs you thought of. To let the suite wander through other data on every run and still debug what it finds, seed with a *new* seed each run and print it when the test fails. In a test framework that is `[FakeSeed]` (xUnit.net, NUnit, MSTest and TUnit; see [Replayable random data](../README.md#replayable-random-data)). Anywhere else, wrap the body:

<!-- snippet: seed-run -->
```csharp
FakeSeed.Run(() =>
{
    var orders = Fake.Many<Order>(20);      // other orders on every run
    Ship(orders);                           // when this throws, the seed goes to the report
}, report: output.Add);
```
<!-- end snippet -->

`report` is where the line goes, usually your framework's test output: `Phoney: this test ran with random seed 481516234. Replay it with FakeSeed.Run(..., seed: 481516234) or PHONEY_SEED=481516234.` Do either and the failing data comes back. The exception itself is rethrown unchanged.

`[FakeCheck]` and `.Check(...)` go one step further: they try a hundred inputs per run and shrink the one that fails to the simplest input that still fails. The [property checks](../README.md#property-checks) section has the details.

## How the seeds stack

When more than one of these is in play, the more specific one wins:

- **A generator's own `.Seed(n)` beats everything around it.** `Fake.Seeded`, `[FakeSeed]` and `Fake.ForKey` seed the facade, so a generator created with its own seed (or created earlier and kept in a `static` field) keeps its data.
- **Scopes nest; the innermost decides.** A `Fake.Fork(i)` inside `Fake.ForKey(key)` forks the keyed values; a `Fake.Seeded` inside it seeds as usual.
- **`PHONEY_SEED` moves what isn't pinned.** It changes every `[FakeData]` test without an explicit `Seed`, and is the seed of `[FakeSeed]`, `[FakeCheck]` and `.Check(...)` when they have none of their own. A pinned seed always wins.

## What moves seeded data

A seed promises the same data for the same *recipe*. These change the recipe:

- **How a member is made.** Every member draws from one stream in member order, so a rule that draws less (a constant instead of a convention) or more shifts the members after it. Options that draw nothing, such as `WithBackReferences()` and `WithParentKey`, leave the rest alone, and so does adding a rule for the last member.
- **The model.** New members, reordered members or a new base class change the draws. Read-only collections are drawn last, so adding one doesn't move the others.
- **The locale and its data.** A locale's names, cities and formats are what the stream picks from. Phoney keeps its draws stable across releases, but new faker.js data changes the values picked from it, so pin the package version if you store generated values in snapshots.

What doesn't move data: the operating system, CPU architecture, .NET version, ICU or invariant globalization, the time of day, how many items you generate, or whether a type uses the source-generated model or reflection.
