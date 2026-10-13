using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Phoney.DependencyInjection;
using Phoney.Generation;

namespace Phoney.DependencyInjection.Tests;

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

[FakeFor<Customer>]
public static partial class TestData;

public sealed class AddPhoneyTests
{
    private static readonly DateTimeOffset Now = new(2033, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Registers_a_scoped_faker_and_a_singleton_factory()
    {
        using var provider = new ServiceCollection().AddPhoney().Services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var faker = scope.ServiceProvider.GetRequiredService<IFaker>();
        faker.ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<Faker>());
        faker.Person.FullName().ShouldNotBeNullOrWhiteSpace();

        using var other = provider.CreateScope();
        other.ServiceProvider.GetRequiredService<IFaker>().ShouldNotBeSameAs(faker);
        provider.GetRequiredService<IFakerFactory>().ShouldBeSameAs(provider.GetRequiredService<IFakerFactory>());
    }

    [Test]
    public void Options_set_locale_seed_and_reference_date_and_every_scope_starts_at_the_seed()
    {
        var reference = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var provider = new ServiceCollection()
            .AddPhoney(o =>
            {
                o.Locale = "sv";
                o.Seed = 42;
                o.ReferenceDate = reference;
            })
            .Services.BuildServiceProvider();

        string Sample()
        {
            using var scope = provider.CreateScope();
            var faker = scope.ServiceProvider.GetRequiredService<IFaker>();
            faker.Locale.ShouldBe("sv");
            faker.ReferenceDate.ShouldBe(reference);
            return faker.Person.FullName() + faker.Date.Past().ToString("O");
        }

        var expected = new Faker("sv", 42) { ReferenceDate = reference };
        Sample().ShouldBe(expected.Person.FullName() + expected.Date.Past().ToString("O"));
        Sample().ShouldBe(Sample());
    }

    [Test]
    public void The_factory_creates_fakers_for_other_locales_and_seeds()
    {
        using var provider = new ServiceCollection().AddPhoney(o => o.Seed = 1).Services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IFakerFactory>();

        factory.Create("de").Locale.ShouldBe("de");
        factory.Create(seed: 7).Person.FullName().ShouldBe(new Faker(seed: 7).Person.FullName());
        factory.Create().Person.FullName().ShouldBe(new Faker(seed: 1).Person.FullName());
    }

    [Test]
    public void A_registered_TimeProvider_drives_the_dates()
    {
        var time = new FakeTimeProvider(Now);
        using var provider = new ServiceCollection().AddSingleton<TimeProvider>(time).AddPhoney(o => o.Seed = 3).Services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var faker = scope.ServiceProvider.GetRequiredService<IFaker>();
        faker.ReferenceDate.ShouldBe(Now);
        time.Advance(TimeSpan.FromDays(2));
        faker.ReferenceDate.ShouldBe(Now.AddDays(2));
    }

    [Test]
    public void The_system_TimeProvider_does_not_override_the_seeded_reference_date()
    {
        using var provider = new ServiceCollection().AddSingleton(TimeProvider.System).AddPhoney(o => o.Seed = 3).Services.BuildServiceProvider();

        provider.CreateScope().ServiceProvider.GetRequiredService<IFaker>().ReferenceDate.ShouldBe(Faker.DefaultSeededReferenceDate);
    }

    [Test]
    public void Generators_are_singletons_configured_by_the_options()
    {
        var time = new FakeTimeProvider(Now);
        using var provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddPhoney(o => o.Seed = 5)
            .AddGenerator<Customer>(g => g.With(c => c.FirstName, "Ada"))
            .Services.BuildServiceProvider();

        var generator = provider.GetRequiredService<Generator<Customer>>();
        generator.ShouldBeSameAs(provider.GetRequiredService<Generator<Customer>>());
        var customer = generator.Generate();
        customer.FirstName.ShouldBe("Ada");
        customer.CreatedAt.ShouldBeLessThanOrEqualTo(Now.UtcDateTime);
        customer.Email.ShouldBe(new Generator<Customer>(ModelRegistry.For<Customer>()).Seed(5).TimeProvider(time).With(c => c.FirstName, "Ada").Generate().Email);
    }

    [Test]
    public void Generated_generators_can_be_registered_without_reflection()
    {
        using var provider = new ServiceCollection()
            .AddPhoney(o => o.Seed = 6)
            .AddGenerator(TestData.Customer, g => g.Ignore(c => c.Email))
            .Services.BuildServiceProvider();

        var customers = provider.GetRequiredService<Generator<Customer>>().Generate(3);
        customers.ShouldAllBe(c => c.Email == "");
        customers.Select(c => c.FirstName).ShouldBe(TestData.Customer.Seed(6).Generate(3).Select(c => c.FirstName));
    }

    [Test]
    public void AddPhoney_can_be_called_more_than_once()
    {
        var services = new ServiceCollection();
        services.AddPhoney(o => o.Locale = "de");
        services.AddPhoney(o => o.Seed = 9);
        using var provider = services.BuildServiceProvider();

        var faker = provider.CreateScope().ServiceProvider.GetRequiredService<IFaker>();
        faker.Locale.ShouldBe("de");
        faker.Person.FirstName().ShouldBe(new Faker("de", 9).Person.FirstName());
    }
}
