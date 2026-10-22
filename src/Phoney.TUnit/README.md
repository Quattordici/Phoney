# Phoney.TUnit

[Phoney](https://github.com/Quattordici/Phoney) fake data for [TUnit](https://tunit.dev) tests. `[FakeData]` fills test parameters with realistic values, chosen by name and type like the members of a Phoney-generated object:

```csharp
using Phoney.TUnit;

public class OrderTests
{
    [Test, FakeData]
    public async Task Places_order(Customer customer, string email, [Range(1, 10)] int quantity)
    {
        // customer is fully populated, email is an email address, quantity is between 1 and 10
    }

    [Test, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
    public async Task Formats_address(string street, string city, string zipCode) { }
}
```

- **Values agree.** All parameters of a test case describe the same person, place and timeline: `firstName`, `email`, `birthDate` and `age` match, `street`, `city` and `zipCode` come from one address, and `createdAt` comes before `updatedAt`.
- **Data annotations are honoured** on parameters: `[Range]`, `[StringLength]`, `[EmailAddress]`, `[RegularExpression]`, `[AllowedValues]` and more.
- **Reproducible by default.** The seed is derived from the test's class, method and parameters, so every run and every machine gets the same values. Set `Seed = 42` for other values.
- **Options:** `Count`, `Seed`, `Locale`, `NullProbability` (for nullable parameters) and `ReferenceDate` (ISO 8601).
- **`PHONEY_SEED=123 dotnet test`** moves the seed of every `[FakeData]` test at once, to run the whole suite on other data; the same value replays it.
- Also works on the test class (constructor parameters) and on `required` properties.
- Custom conventions (`Fake.Conventions.Add(...)`), subtypes (`Fake.Subtypes.Add<IShape, Circle>()`, `[JsonDerivedType]`) and source-generated models (`[FakeFor<T>]`) apply.
- Test data is created while TUnit discovers tests, so register custom conventions and subtypes in a `[Before(TestDiscovery)]` hook; `[Before(Assembly)]` and later hooks run too late:

  ```csharp
  public static class TestSetup
  {
      [Before(TestDiscovery)]
      public static void RegisterConventions() =>
          Fake.Conventions.Add("Sku", f => f.Random.ReplaceSymbols("SKU-####"));
  }
  ```


## Random data you can replay

`[FakeSeed]` runs a test on a seeded `Fake` facade, so `Fake.One<T>()`, `Fake.Many<T>()`, `Fake.For<T>()` and `Fake.Person…` give different data on every run, and **when the test fails its seed is printed in the test output**:

```csharp
[Test, FakeSeed]
public async Task Totals_add_up()
{
    var orders = Fake.Many<Order>(100);
    …
}
// Phoney: this test ran with random seed 481516234. Replay it with [FakeSeed(481516234)] or PHONEY_SEED=481516234.
```

- **Replay** with `[FakeSeed(481516234)]` on the test, or run `PHONEY_SEED=481516234 dotnet test` to rerun a whole run. A pinned seed wins over `PHONEY_SEED`.
- On a method it wins over the class, and on a class over the assembly (`[assembly: FakeSeed]`). Parallel tests don't affect each other.
- It seeds the `Fake` facade of the test, not generators created earlier (a `static` field) or ones that call `.Seed(...)` themselves. Skipped tests print nothing.
- It works with other test executors (`[TestExecutor<T>]`): the executor the test already has runs inside the seeded scope.

## Property checks that shrink

`[FakeCheck]` runs a test once for each of 100 generated rows of its parameters. When the test fails for a row, the row is **shrunk** to the simplest one that still fails (fewer collection elements, lower numbers, the first list values), and the test fails with it:

```csharp
[Test, FakeCheck]
public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =>
    Pricing.Total(order, discount).ShouldBeGreaterThanOrEqualTo(0);
// FakeCheckException<Object[]>: The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
// Counterexample:
//   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
// …
// Replay: PHONEY_SEED=812734 dotnet test, or [FakeCheck(Seed = 812734)].
```

- Parameters are filled like `[FakeData]` fills them (names, types, annotations; the values of a row agree), and shrinking only makes rows Phoney could have generated, so a counterexample is still realistic data.
- **New rows on every run**, so the suite explores more data over time. `[FakeCheck(Seed = 812734)]` or `PHONEY_SEED=812734 dotnet test` replays a failure and its shrinking exactly.
- **Options:** `Count` (default 100), `MaxShrinks` (default 1000; 0 reports the original row), `Seed`, `Locale`, `NullProbability` and `ReferenceDate`.
- The rows run one after another on one instance of the test class, inside the executor the test already has (`[TestExecutor<T>]`) and inside `[FakeSeed]`. TUnit lists one test, named with a sample row.
- Outside a test method attribute, `Fake.Check((Order order, int discount) => …)` does the same for a lambda.

## Invalid and boundary data as test cases

`ObjectGenerator<T>.InvalidCases()` and `BoundaryCases()` (see the main README) make good TUnit data sources: every case is a test case named after what it is.

```csharp
public static IEnumerable<Func<ConstraintCase<Order>>> Violations() =>
    Fake.For<Order>().Seed(1).InvalidCases().Select(c => (Func<ConstraintCase<Order>>)(() => c));

[Test, MethodDataSource(nameof(Violations))]   // e.g. Validation_rejects(Quantity: above the maximum (10))
public void Validation_rejects(ConstraintCase<Order> violation) =>
    Validate(violation.Value).IsValid.ShouldBeFalse(violation.ToString());
```
