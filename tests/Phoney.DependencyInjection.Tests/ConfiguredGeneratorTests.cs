using Microsoft.Extensions.DependencyInjection;
using Phoney.Generation;

namespace Phoney.DependencyInjection.Tests;

// Only these tests generate Supplier: Fake.Generators is process-wide.
public sealed class Supplier
{
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Language { get; set; } = "";
}

[NotInParallel]
public sealed class ConfiguredGeneratorTests
{
    [Test]
    public void AddGenerator_starts_from_the_type_wide_configuration_and_configures_on_top()
    {
        try
        {
            Fake.Generators.Configure<Supplier>(g => g.With(s => s.Name, "Acme").With(s => s.Country, "SE"));
            using var provider = new ServiceCollection()
                .AddPhoney(o => o.Seed = 5)
                .AddGenerator<Supplier>(g => g.With(s => s.Country, "NO"))
                .Services.BuildServiceProvider();

            var supplier = provider.GetRequiredService<ObjectGenerator<Supplier>>().Generate();
            supplier.Name.ShouldBe("Acme");
            supplier.Country.ShouldBe("NO");
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void The_configured_locale_wins_over_the_options()
    {
        try
        {
            Fake.Generators.Configure<Supplier>(g => g.Locale("sv").With(s => s.Language, f => f.Locale));
            using var provider = new ServiceCollection().AddPhoney(o => o.Locale = "de").AddGenerator<Supplier>().Services.BuildServiceProvider();

            provider.GetRequiredService<ObjectGenerator<Supplier>>().Generate().Language.ShouldBe("sv");
        }
        finally
        {
            Fake.Generators.Clear();
        }
    }

    [Test]
    public void Fake_Generators_Apply_returns_the_generator_unchanged_without_a_configuration()
    {
        var plain = Fake.For<Supplier>();
        Fake.Generators.Apply(plain).ShouldBeSameAs(plain);
    }
}
