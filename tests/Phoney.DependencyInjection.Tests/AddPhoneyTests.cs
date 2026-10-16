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

public sealed class Basket
{
    public string? Note { get; set; }
    public List<string> Tags { get; set; } = [];
    public Basket? Inner { get; set; }
}

public sealed class Unconventional
{
    public string Foo { get; set; } = "";
}

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

        var generator = provider.GetRequiredService<ObjectGenerator<Customer>>();
        generator.ShouldBeSameAs(provider.GetRequiredService<ObjectGenerator<Customer>>());
        var customer = generator.Generate();
        customer.FirstName.ShouldBe("Ada");
        customer.CreatedAt.ShouldBeLessThanOrEqualTo(Now.UtcDateTime);
        customer.Email.ShouldBe(new ObjectGenerator<Customer>(ModelRegistry.For<Customer>()).Seed(5).TimeProvider(time).With(c => c.FirstName, "Ada").Generate().Email);
    }

    [Test]
    public void Generated_generators_can_be_registered_without_reflection()
    {
        using var provider = new ServiceCollection()
            .AddPhoney(o => o.Seed = 6)
            .AddGenerator(TestData.Customer, g => g.Ignore(c => c.Email))
            .Services.BuildServiceProvider();

        var customers = provider.GetRequiredService<ObjectGenerator<Customer>>().Generate(3);
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

    private static ServiceProvider Provider(Action<PhoneyOptions> configure, Func<ObjectGenerator<Basket>, ObjectGenerator<Basket>>? generator = null) =>
        new ServiceCollection().AddPhoney(configure).AddGenerator<Basket>(generator).Services.BuildServiceProvider();

    [Test]
    public void Generation_defaults_in_the_options_apply_to_registered_generators()
    {
        using var provider = Provider(o =>
        {
            o.Seed = 4;
            o.NullProbability = 1;
            o.MinCollectionSize = 4;
            o.MaxCollectionSize = 4;
            o.MaxDepth = 0;
        });

        var baskets = provider.GetRequiredService<ObjectGenerator<Basket>>().Generate(5);

        baskets.ShouldAllBe(b => b.Note == null && b.Tags.Count == 4 && b.Inner == null);
    }

    [Test]
    public void A_single_collection_size_bound_keeps_the_other_default_workable()
    {
        using var minOnly = Provider(o => o.MinCollectionSize = 6);
        using var maxOnly = Provider(o => o.MaxCollectionSize = 0);

        minOnly.GetRequiredService<ObjectGenerator<Basket>>().Generate(5).ShouldAllBe(b => b.Tags.Count >= 6);
        maxOnly.GetRequiredService<ObjectGenerator<Basket>>().Generate(5).ShouldAllBe(b => b.Tags.Count == 0);
    }

    [Test]
    public void Without_generation_options_the_generators_keep_their_defaults()
    {
        using var provider = Provider(o => o.Seed = 4);
        var configured = provider.GetRequiredService<ObjectGenerator<Basket>>().Generate(10);
        var plain = new ObjectGenerator<Basket>(ModelRegistry.For<Basket>()).Seed(4).Generate(10);

        configured.Select(b => (b.Note, b.Tags.Count)).ShouldBe(plain.Select(b => (b.Note, b.Tags.Count)));

        // The exact values of seed 4, so two generators that agree on a wrong value fail too.
        configured.Select(b => b.Tags.Count).ShouldBe([3, 2, 3, 3, 1, 1, 2, 2, 3, 3]);
        configured.Select(b => b.Note![..22]).ShouldBe(
        [
            "Nesciunt aestivus adsu", "Audio absorbeo asper d", "Placeat demitto mollit", "Dolorem eum cilicium s", "Adstringo verto sint a",
            "Argumentum sublime bla", "Armarium cognatus turb", "Laborum tero distincti", "Totidem delectus solut", "Calculus debilito calc",
        ]);
    }

    [Test]
    public void The_configure_delegate_of_a_generator_overrides_the_options()
    {
        using var provider = Provider(o => { o.NullProbability = 1; o.MinCollectionSize = 4; o.MaxCollectionSize = 4; },
            g => g.NullProbability(0).CollectionSize(2, 2));

        provider.GetRequiredService<ObjectGenerator<Basket>>().Generate(5).ShouldAllBe(b => b.Note != null && b.Tags.Count == 2);
    }

    [Test]
    public void Strict_in_the_options_makes_registered_generators_strict()
    {
        using var strict = new ServiceCollection().AddPhoney(o => o.Strict = true).AddGenerator<Unconventional>().Services.BuildServiceProvider();
        using var lenient = new ServiceCollection().AddPhoney().AddGenerator<Unconventional>().Services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => strict.GetRequiredService<ObjectGenerator<Unconventional>>().Generate());
        lenient.GetRequiredService<ObjectGenerator<Unconventional>>().Generate().Foo.ShouldNotBeNull();
    }

    [Test]
    public void Invalid_generation_options_fail_in_AddPhoney()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddPhoney(o => o.NullProbability = 1.5)).ParamName.ShouldBe("NullProbability");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddPhoney(o => o.NullProbability = double.NaN)).ParamName.ShouldBe("NullProbability");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddPhoney(o => o.MaxDepth = -1)).ParamName.ShouldBe("MaxDepth");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddPhoney(o => o.MinCollectionSize = -1)).ParamName.ShouldBe("MinCollectionSize");
        Should.Throw<ArgumentOutOfRangeException>(() => new ServiceCollection().AddPhoney(o => { o.MinCollectionSize = 5; o.MaxCollectionSize = 2; })).ParamName.ShouldBe("MaxCollectionSize");
    }
}
