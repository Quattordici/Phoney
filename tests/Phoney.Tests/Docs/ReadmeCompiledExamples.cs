using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Time.Testing;

namespace Phoney.Tests.Docs;

// Inside the namespace, so the README's model wins over the test models of Phoney.Tests.
using Phoney.Modules;
using Phoney.Templates;
using Phoney.Tests.Docs.Readme;

/// <summary>
/// The README's examples that change app-wide state (facade seed, locale or clock, conventions, subtypes, generators, locales,
/// template functions), read files or call application code. They are compiled against the current API, so the README can't show
/// code that doesn't build, but never run: running them would change the data of tests running in parallel.
/// </summary>
internal static class ReadmeCompiledExamples
{
    internal static void ReproducibleData()
    {
        static void Save(string name, Order order) => Console.WriteLine($"{name}: {order.Id}");

        // snippet: readme-reproducible
        var faker = new Faker("de", seed: 2024);                         // same values and dates on every run
        var later = new Faker("de", seed: 2024) { ReferenceDate = DateTimeOffset.UtcNow }; // opt back into "now"

        using var _ = Fake.Seeded(42);           // Fake.*, Fake.One/Many/For are reproducible in this scope only, even with tests in parallel
        Fake.Seed(42);                           // or seed the facade app-wide (every thread starts the same sequence)

        Parallel.For(0, 1_000, i =>
        {
            using var fork = Fake.Fork(i);       // a faker of item i's own, from the seed and the index: the same whatever the thread timing
            Save(Fake.Person.FullName(), Fake.One<Order>());
        });
        // end snippet
    }

    internal static void TimeProviders(System.Reflection.MethodInfo method)
    {
        // snippet: readme-time-provider
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var faker = new Faker("sv") { TimeProvider = time };            // faker.Date.Past() is before 2030-01-01
        var orders = Fake.For<Order>().TimeProvider(time).Generate(10);   // CreatedAt, ages, timelines follow the clock
        var rows = Fake.Arguments(method).TimeProvider(time).Generate(5); // argument rows too
        Fake.TimeProvider = time;                                         // the static facade, Fake.For/One/Many and Fake.Arguments
        time.Advance(TimeSpan.FromDays(30));                              // read on every use, so dates move along
        // end snippet
    }

    internal static void NationalIdentityNumbers()
    {
        // snippet: readme-national-ids
        var faker = new Faker("sv", seed: 1);
        faker.NationalId.Person();                                   // "19761015-5736" (personnummer)
        faker.NationalId.Person("NO", birthDate: new DateOnly(1990, 5, 17), sex: Sex.Female, formatted: false);
        faker.NationalId.Company();                                  // "538599-9650" (organisationsnummer)
        faker.NationalId.Vat("GB");                                  // "GB 904 0044 78"

        NationalIds.IsValid(NationalIdKind.Person, "SE", "19761015-5736");   // true: every scheme also validates
        NationalIds.Supported(NationalIdKind.Vat);                           // ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]
        NationalIds.Register(new MyEmployeeNumbers());                       // your own, derived from NationalIdScheme
        // end snippet
    }

    internal static void ChoosingLocales()
    {
        // snippet: readme-locales
        var all = Locales.All;                    // 76 locales plus `base`, with code, title, endonym, script, fallback chain
        var austrian = new Faker("de-AT");        // de_AT → de → en → base
        var current = new Faker(CultureInfo.CurrentCulture); // "sv-SE" → sv, "sr-Latn-RS" → sr_RS_latin
        Fake.Locale = "fr";                       // default for Fake.* and generators
        // end snippet
    }

    internal static void CustomData()
    {
        // snippet: readme-custom-data
        // A new locale on top of an existing one
        Locales.Register("sv_dalarna", l => l
            .FallbackTo("sv")                                  // sv → en → base
            .Cities("Mora", "Falun", "Rättvik")
            .FirstNames(Sex.Female, "Kerstin", "Margit")      // male names still come from sv
            .EmailDomains("dalarna.example")
            .PhoneFormats("0250-## ## ##"));

        // Extra or replaced data in an existing locale, by typed method or faker.js data path
        Locales.Extend("en", l => l
            .CompanyNames("{{Person.LastName}} & Sons", "Acme {{Commerce.Department}}")
            .Set("commerce.department", "Toys", "Garden")
            .Set("person.nickname", "Ace", "Buddy"));          // new paths are allowed: faker.Pick("person.nickname")

        Locales.Register("en_PIRATE", File.OpenRead("pirate.json"), "en");   // a faker.js locale file
        // end snippet
    }

    internal static void Templates(Faker faker)
    {
        // snippet: readme-templates
        faker.Parse("{{Person.FirstName}} ordered {{Commerce.ProductName}} from {{Company.Name}}");
        faker.Parse("Ticket {{Helpers.FromRegExp([A-Z]{3}-[0-9]{4})}}, PIN {{String.Numeric(4)}}");
        TemplateFunctions.Register("Shop.Sku", (f, _) => f.Random.ReplaceSymbols("SKU-#####"));   // your own functions
        // end snippet
    }

    internal static void Subtypes()
    {
        // snippet: readme-subtypes
        var drawings = Fake.For<Drawing>()
            .Subtype<IShape, Circle>()               // every IShape in the object graph is a Circle
            .Subtype<IAnimal, Cat, Dog>()            // one of Cat or Dog, picked at random (reproducible with a seed)
            .With(d => d.Logo, Fake.For<Square>())   // one member with its own generator of a subtype
            .Generate(10);

        Fake.Subtypes.Add<IShape, Circle, Square>(); // app-wide, for generators created afterwards (also Remove<IShape>() and Clear())
        // end snippet
    }

    internal static void TypeWideGenerators()
    {
        // snippet: readme-type-generators
        var orders = Fake.For<Order>()
            .WithGeneratorFor(Fake.For<Address>().Locale("de").With(a => a.Country, "Germany"))   // Order.Billing, Order.Shipping, Customer.Home…
            .WithGeneratorFor<IPayment, Card>(Fake.For<Card>().With(c => c.Network, "Visa"))       // an interface, from a generator of a subtype
            .Generate(100);

        Fake.Generators.Add(Fake.For<Address>().Locale("sv"));   // app-wide, also for [FakeData] parameters (Remove<T>(), Clear())
        // end snippet
    }

    internal static void ConfiguredTypes()
    {
        // snippet: readme-configure
        // at startup: a module initializer, a test setup hook, Program.cs
        Fake.Generators.Configure<Customer>(g => g
            .With(c => c.Age, f => f.Number.Int(18, 65))
            .Unique(c => c.Email));

        var adult = Fake.One<Customer>();                       // Fake.One, Many, For, ForGenerated and Populate
        var order = Fake.One<Order>();                          // order.Customer, nested at any depth
        // test parameters ([FakeData] Customer customer) and AddGenerator<Customer>() in DI get it too
        var vip = Fake.For<Customer>().With(c => c.Tier, Tier.Gold).Generate(); // rules added here come after the configured ones
        // end snippet
    }

    internal static void Feeds()
    {
        // snippet: readme-feeds
        var feed = DataFeed.FromCsvFile("customers.csv");        // also FromCsv(text), FromJson(text), FromJsonFile(path)
        var customers = Fake.For<Customer>().WithFeed(feed).Generate(100);                         // rows 1, 2, 3… and round again
        var sample = Fake.For<Customer>().Seed(7).WithFeed(feed, FeedOrder.Random).Generate(100);  // a reproducible random row each

        Fake.For<Customer>().Seed(1).WriteCsvFile("customers.csv", 500);
        string json = Fake.For<Customer>().ToJson(10);                   // also WriteJsonFile(path, n), WriteJson(stream, n)
        string csv = Fake.For<Customer>().ToCsv(100, delimiter: ';', columns: ["Id", "Email", "Country"]);
        // end snippet
    }

    internal static void InvalidAndBoundaryData(OrderValidator validator)
    {
        // snippet: readme-invalid
        var order = Fake.For<Order>().Invalid(o => o.Quantity).Generate();            // 0 or 11 for [Range(1, 10)], everything else valid
        var edge = Fake.For<Order>().Boundary(o => o.Quantity).Generate();            // 1 or 10

        foreach (var c in Fake.For<Order>().Seed(1).InvalidCases())                   // every violation of every annotated member
            validator.Validate(c.Value).IsValid.ShouldBeFalse(c.ToString());          // "Quantity: above the maximum (10)"

        foreach (var c in Fake.For<Order>().Seed(1).BoundaryCases())                  // every value exactly on a limit, all valid
            validator.Validate(c.Value).IsValid.ShouldBeTrue(c.ToString());           // "Name: the maximum length (50)"
        // end snippet
    }

    internal static void Conventions()
    {
        // snippet: readme-conventions
        Fake.Conventions.Add<string>(name => name.EndsWith("Sku"), f => f.Random.ReplaceSymbols("SKU-#####")); // app-wide
        var withVat = Fake.For<Order>().WithConvention<decimal>(n => n == "Vat", f => 0.25m);           // one generator
        var dated = Fake.For<Order>().WithEveryMemberOfType<DateOnly>(f => DateOnly.FromDateTime(f.Date.Past().UtcDateTime)); // every DateOnly member, whatever its name
        // end snippet
    }

    internal static async Task PropertyChecks(ShopApi api)
    {
        // snippet: readme-check
        Fake.For<Customer>().CollectionSize(0, 6)
            .Check(c => c.Orders.Sum(o => o.Quantity * o.Price) < 5_000);          // returns false: fails

        Fake.For<Order>().Check(o => Pricing.Total(o).ShouldBeGreaterThanOrEqualTo(0));   // any assertion library
        await Fake.For<Order>().CheckAsync(async o => await Assert.That(await api.Quote(o)).IsPositive());
        // end snippet
    }

    internal static async Task ParameterChecks(ShopApi api)
    {
        // snippet: readme-check-parameters
        Fake.Check((OrderLine line, [Range(0, 1000)] int discount) => Pricing.Total(line, discount) >= 0);
        Fake.Check((string email, [Range(18, 99)] int age) => Signup.Validate(email, age).ShouldBeEmpty());
        await Fake.CheckAsync(async (Customer customer) => await api.Register(customer));
        // end snippet
    }
}
