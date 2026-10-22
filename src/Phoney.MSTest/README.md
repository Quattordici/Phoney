# Phoney.MSTest

[Phoney](https://github.com/Quattordici/Phoney) fake data for [MSTest](https://learn.microsoft.com/dotnet/core/testing/unit-testing-mstest-intro) tests. `[FakeData]` fills test parameters with realistic values, chosen by name and type like the members of a Phoney-generated object:

```csharp
using Phoney.MSTest;

[TestClass]
public class OrderTests
{
    [TestMethod, FakeData]
    public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity)
    {
        // customer is fully populated, email is an email address, quantity is between 1 and 10
    }

    [TestMethod, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
    public void Formats_address(string street, string city, string zipCode) { }
}
```

- **Values agree.** All parameters of a test case describe the same person, place and timeline: `firstName`, `email`, `birthDate` and `age` match, `street`, `city` and `zipCode` come from one address, and `createdAt` comes before `updatedAt`.
- **Data annotations are honoured** on parameters: `[Range]`, `[StringLength]`, `[EmailAddress]`, `[RegularExpression]`, `[AllowedValues]` and more.
- **Reproducible by default.** The seed is derived from the test's class, method and parameters, so every run and every machine gets the same values. Set `Seed = 42` for other values.
- **Options:** `Count`, `Seed`, `Locale`, `NullProbability` (for nullable parameters) and `ReferenceDate` (ISO 8601). Several `[FakeData]` attributes add their cases.
- **`PHONEY_SEED=123 dotnet test`** moves the seed of every `[FakeData]` test at once, to run the whole suite on other data; the same value replays it.
- **Replayable random data**: `FakeSeed.Run(() => { … }, TestContext.WriteLine)` (or `RunAsync`) runs a test body on a seeded `Fake` facade, new on every run, and reports the seed when it throws; `PHONEY_SEED=… dotnet test` or `FakeSeed.Run(…, seed: n)` replays it.
- Custom conventions (`Fake.Conventions.Add(...)`), subtypes (`Fake.Subtypes.Add<IShape, Circle>()`, `[JsonDerivedType]`) and source-generated models (`[FakeFor<T>]`) apply. Test data is created while MSTest discovers tests, so register them before that, for example in a module initializer.
- It fills method parameters. See the [main README](https://github.com/Quattordici/Phoney#test-data-in-xunit-nunit-mstest-and-tunit) for the full feature set.

## Property checks that shrink

`[FakeCheck]` (instead of `[TestMethod]`) runs a test once for each of 100 generated rows of its parameters. When the test fails for a row, the row is **shrunk** to the simplest one that still fails (fewer collection elements, lower numbers, the first list values), and the test fails with it:

```csharp
[FakeCheck]
public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =>
    Pricing.Total(order, discount).ShouldBeGreaterThanOrEqualTo(0);
// The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
// Counterexample:
//   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
// …
// Replay: PHONEY_SEED=812734 dotnet test, or [FakeCheck(Seed = 812734)].
```

- Parameters are filled like `[FakeData]` fills them (names, types, annotations; the values of a row agree), and shrinking only makes rows Phoney could have generated, so a counterexample is still realistic data.
- **New rows on every run**, so the suite explores more data over time. `[FakeCheck(Seed = 812734)]` or `PHONEY_SEED=812734 dotnet test` replays a failure and its shrinking exactly.
- **Options:** `Count` (default 100), `MaxShrinks` (default 1000; 0 reports the original row), `Seed`, `Locale`, `NullProbability` and `ReferenceDate`.
- MSTest lists one test and reports one result. The rows run as full MSTest invocations (a new instance, `[TestInitialize]` and `[TestCleanup]` per row).
- Outside a test method attribute, `Fake.Check((Order order, int discount) => …)` does the same for a lambda.
