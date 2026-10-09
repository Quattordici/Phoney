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
- Also works on the test class (constructor parameters) and on `required` properties.
- Custom conventions (`Fake.Conventions.Add(...)`) and source-generated models (`[FakeFor<T>]`) apply.
- Test data is created while TUnit discovers tests, so register custom conventions in a `[Before(TestDiscovery)]` hook; `[Before(Assembly)]` and later hooks run too late:

  ```csharp
  public static class TestSetup
  {
      [Before(TestDiscovery)]
      public static void RegisterConventions() =>
          Fake.Conventions.Add("Sku", f => f.Random.Replace("SKU-####"));
  }
  ```
