using Microsoft.Extensions.DependencyInjection;
using Phoney.Generation;

namespace Phoney.DependencyInjection.Tests.Docs;

// Inside the namespace, so the docs' shop model wins over the test models of this project.
using Phoney.Docs.Shop;

[FakeFor<Order>]
internal static partial class TestData;

// snippet: di-controller
public class DemoController(IFaker faker, ObjectGenerator<Customer> customers)
{
    public Customer Get() => customers.Generate();
    public string Name() => faker.Person.FullName();
}
// end snippet

/// <summary>The READMEs' registration, run against a service collection, and its uses.</summary>
public sealed class DependencyInjectionReadmeExamples
{
    // Stand-ins for ASP.NET Core's WebApplicationBuilder and WebApplication, so the examples run without a web host
    // (and the tests keep the floor of Microsoft.Extensions.DependencyInjection.Abstractions).
    private sealed class Builder
    {
        public ServiceCollection Services { get; } = [];
    }

    private sealed class App(IServiceProvider services)
    {
        private readonly Dictionary<string, Delegate> _routes = [];

        public void MapGet(string pattern, Delegate handler) => _routes[pattern] = handler;

        /// <summary>Calls a route's handler with its parameters from a new scope, like a request.</summary>
        public object? Get(string pattern)
        {
            using var scope = services.CreateScope();
            var handler = _routes[pattern];
            var arguments = handler.Method.GetParameters().Select(p => scope.ServiceProvider.GetRequiredService(p.ParameterType)).ToArray();
            return handler.DynamicInvoke(arguments);
        }
    }

    private static ServiceProvider Registered()
    {
        var builder = new Builder();

        // snippet: di-add-phoney
        builder.Services.AddPhoney(o =>
            {
                o.Locale = "sv";
                o.Seed = 42;                                            // every scope starts at the same seed
            })
            .AddGenerator<Customer>(g => g.With(c => c.Country, "SE"))  // a singleton ObjectGenerator<Customer>
            .AddGenerator(TestData.Order);                              // a [FakeFor<Order>] generator: no reflection, AOT safe
        // end snippet

        return builder.Services.BuildServiceProvider();
    }

    [Test]
    public void A_controller_gets_the_scoped_faker_and_the_generator()
    {
        using var services = Registered();
        using var scope = services.CreateScope();

        var controller = ActivatorUtilities.CreateInstance<DemoController>(scope.ServiceProvider);

        controller.Get().Country.ShouldBe("SE");
        controller.Name().ShouldNotBeEmpty();
        scope.ServiceProvider.GetRequiredService<ObjectGenerator<Order>>().Generate().Lines.ShouldNotBeNull();
    }

    [Test]
    public void Every_request_scope_starts_at_the_seed()
    {
        using var services = Registered();
        var app = new App(services);

        // snippet: di-minimal-api
        app.MapGet("/demo/customers", (ObjectGenerator<Customer> customers) => customers.Generate(10));
        app.MapGet("/demo/name", (IFaker faker) => faker.Person.FullName());
        // end snippet

        ((List<Customer>)app.Get("/demo/customers")!).ShouldAllBe(c => c.Country == "SE");
        app.Get("/demo/name").ShouldBe(app.Get("/demo/name"));
    }
}
