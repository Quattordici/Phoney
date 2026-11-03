namespace Phoney.Tests.Docs;

// Inside the namespace, so the README's model wins over the test models of Phoney.Tests.
using Phoney.Generation;
using Phoney.Tests.Docs.Readme;

// snippet: readme-fake-for
[FakeFor<Customer>, FakeFor<Order>]
internal static partial class TestData;
// end snippet

// snippet: readme-type-argument
public abstract class TestDataBuilder<[FakeForTypeArgument] T> where T : class
{
    public T Build() => Fake.ForGenerated<T>().Generate();
}

internal sealed class CustomerBuilder : TestDataBuilder<Customer>; // Customer gets a model
// end snippet

/// <summary>The README's source generator examples: the declarations above are compiled by the generator, these use them.</summary>
public sealed class ReadmeGeneratedExamples
{
    [Test]
    public void Generated_models()
    {
        // snippet: readme-fake-for-use
        var customers = TestData.Customer.Seed(1).Generate(100);                         // an ObjectGenerator<Customer>, no reflection
        var customer = Fake.ForGenerated<Customer>().Populate(new Customer { Id = 42 }); // the Fake facade, trim and AOT safe
        // end snippet

        customers.Count.ShouldBe(100);
        customer.Id.ShouldBe(42);
        customer.Email.ShouldNotBeEmpty();
    }

    [Test]
    public void A_builder_gets_a_model_for_its_type_argument()
    {
        new CustomerBuilder().Build().Email.ShouldNotBeEmpty();
        Should.NotThrow(() => Fake.ForGenerated<Customer>());
    }
}
