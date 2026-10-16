# Phoney.DependencyInjection

[Phoney](https://github.com/Quattordici/Phoney) fake data in `Microsoft.Extensions.DependencyInjection` and ASP.NET Core.

```csharp
builder.Services.AddPhoney(o =>
    {
        o.Locale = "sv";
        o.Seed = 42;                                            // every scope starts at the same seed
    })
    .AddGenerator<Customer>(g => g.With(c => c.Country, "SE"))  // a singleton ObjectGenerator<Customer>
    .AddGenerator(TestData.Order);                              // a [FakeFor<Order>] generator: no reflection, AOT safe

public class DemoController(IFaker faker, ObjectGenerator<Customer> customers)
{
    public Customer Get() => customers.Generate();
    public string Name() => faker.Person.FullName();
}
```

- **`IFaker` / `Faker`** are scoped (a faker is not thread-safe); **`IFakerFactory`** creates fakers for other locales or seeds; generators are singletons (they are immutable and thread-safe).
- **Seeded**: with `Seed`, each scope's faker starts at the seed, so every request or test scope gets the same values.
- **Time**: `ReferenceDate` fixes "now"; otherwise `o.TimeProvider`, or a `TimeProvider` registered in the container (such as `FakeTimeProvider` in tests), drives relative dates. `TimeProvider.System` is ignored, so seeded dates stay reproducible.
- **Generation defaults**: `NullProbability`, `MinCollectionSize`/`MaxCollectionSize`, `MaxDepth` and `Strict` in the options set those defaults on every registered generator (unset ones keep the generator's own defaults); a generator's configure delegate runs afterwards and overrides them. Out-of-range values throw from `AddPhoney`.
- **Tests**: replace the options in `WebApplicationFactory.ConfigureTestServices(s => s.AddPhoney(o => o.Seed = 1))`; `AddPhoney` can be called again and configures the same options.
