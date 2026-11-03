using System.ComponentModel.DataAnnotations;

namespace Phoney.MSTest.Tests.Docs;

// Inside the namespace, so the docs' shop model wins over the test models of this project.
using Phoney.Docs.Shop;

// snippet: mstest-fake-data
[TestClass]
public sealed class OrderTests
{
    [TestMethod, FakeData]
    public void Places_order(Customer customer, string email, [Range(1, 10)] int quantity)
    {
        customer.Email.ShouldNotBeEmpty();     // customer is fully populated
        email.ShouldContain('@');              // email is an email address
        quantity.ShouldBeInRange(1, 10);
    }

    [TestMethod, FakeData(Count = 5, Locale = "sv")] // five test cases with Swedish data
    public void Formats_address(string street, string city, string zipCode)
    {
        street.ShouldNotBeEmpty();             // street, city and zipCode come from one address
        city.ShouldNotBeEmpty();
        zipCode.ShouldMatch("^[0-9]{5}$");     // Swedish postcodes have five digits
    }
}
// end snippet

/// <summary>The property check of the package README.</summary>
[TestClass]
public sealed class PricingChecks
{
    // snippet: mstest-fake-check
    [FakeCheck]
    public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =>
        Pricing.Total(order, discount).ShouldBeGreaterThanOrEqualTo(0);
    // end snippet
}
