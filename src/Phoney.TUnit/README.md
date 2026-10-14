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
          Fake.Conventions.Add("Sku", f => f.Random.Replace("SKU-####"));
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
- The attribute installs the test's executor, so it can't be combined with another one such as `[STAThreadExecutor]`.
