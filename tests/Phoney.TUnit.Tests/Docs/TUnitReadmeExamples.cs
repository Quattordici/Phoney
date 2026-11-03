using System.ComponentModel.DataAnnotations;

namespace Phoney.TUnit.Tests.Docs;

// Inside the namespace, so the docs' shop model wins over the test models of this project.
using Phoney.Docs.Shop;

// snippet: tunit-fake-data
public sealed class OrderTests
{
    [Test, FakeData]
    public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity)
    {
        customer.Email.ShouldNotBeEmpty();     // customer is fully populated
        email.ShouldContain('@');              // email is an email address
        quantity.ShouldBeInRange(1, 10);
    }

    [Test, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
    public void Formats_address(string street, string city, string zipCode)
    {
        street.ShouldNotBeEmpty();             // street, city and zipCode come from one address
        city.ShouldNotBeEmpty();
        zipCode.ShouldMatch("^[0-9]{5}$");     // Swedish postcodes have five digits
    }
}
// end snippet

/// <summary>
/// Test data is generated while tests are discovered, so conventions are registered in a hook that runs before
/// discovery; later hooks (such as <c>[Before(Assembly)]</c>) would be too late. <c>FakeDataTests.Custom_conventions_apply</c> checks it.
/// </summary>
// snippet: tunit-discovery-hook
public static class TestSetup
{
    [Before(TestDiscovery)]
    public static void RegisterConventions() =>
        Fake.Conventions.Add("Sku", f => f.Random.ReplaceSymbols("SKU-####"));
}
// end snippet

/// <summary>The seeded, property-check and data-source examples of the package README.</summary>
public sealed class TUnitReadmeExamples
{
    // snippet: tunit-fake-seed
    [Test, FakeSeed]
    public void Totals_add_up()
    {
        var orders = Fake.Many<Order>(100);   // new orders on every run, the same ones when replayed

        orders.Sum(o => o.Total).ShouldBe(orders.SelectMany(o => o.Lines).Sum(l => l.Quantity * l.Price));
    }
    // end snippet

    // snippet: tunit-fake-check
    [Test, FakeCheck]
    public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =>
        Pricing.Total(order, discount).ShouldBeGreaterThanOrEqualTo(0);
    // end snippet

    // snippet: tunit-invalid-cases
    public static IEnumerable<Func<ConstraintCase<OrderLine>>> Violations() =>
        Fake.For<OrderLine>().Seed(1).InvalidCases().Select(c => (Func<ConstraintCase<OrderLine>>)(() => c));

    [Test, MethodDataSource(nameof(Violations))]   // e.g. Validation_rejects(Quantity: above the maximum (10))
    public void Validation_rejects(ConstraintCase<OrderLine> violation) =>
        Validator.TryValidateObject(violation.Value, new(violation.Value), null, validateAllProperties: true)
            .ShouldBeFalse(violation.ToString());
    // end snippet
}
