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
- Custom conventions (`Fake.Conventions.Add(...)`), subtypes (`Fake.Subtypes.Add<IShape, Circle>()`, `[JsonDerivedType]`) and source-generated models (`[FakeFor<T>]`) apply. Test data is created while MSTest discovers tests, so register them before that, for example in a module initializer.
- It fills method parameters. See the [main README](https://github.com/Quattordici/Phoney#test-data-in-xunit-nunit-and-mstest) for the full feature set.
