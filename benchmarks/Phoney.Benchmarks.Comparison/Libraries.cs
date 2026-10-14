using AutoBogus;
using AutoFixture;
using FizzWare.NBuilder;
using Phoney;
using Phoney.Generation;

/// <summary>
/// How each library is set up to make the same objects, shared by the timed benchmarks and the cold start measurement. Libraries that can't
/// infer values from member names (Bogus) get the rules a user would write; the others get the defaults a user would start with.
/// </summary>
internal static class Libraries
{
    public const int Seed = 1;

    // Phoney: no configuration, member names and types decide.
    public static Phoney.Generator<FlatCustomer> PhoneyFlatReflection() => new Phoney.Generator<FlatCustomer>(new ReflectionModel<FlatCustomer>()).Seed(Seed);

    public static Phoney.Generator<FlatCustomer> PhoneyFlatGenerated() => ComparisonFakes.FlatCustomer.Seed(Seed);

    public static Phoney.Generator<Shopper> PhoneyGraphReflection() => new Phoney.Generator<Shopper>(new ReflectionModel<Shopper>()).Seed(Seed).CollectionSize(2, 2);

    public static Phoney.Generator<Shopper> PhoneyGraphGenerated() => ComparisonFakes.Shopper.Seed(Seed).CollectionSize(2, 2);

    // Bogus: explicit rules per member, coherent where Phoney is (the email is built from the names).
    public static Bogus.Faker<FlatCustomer> BogusFlat() => new Bogus.Faker<FlatCustomer>("en").UseSeed(Seed)
        .RuleFor(c => c.Id, f => f.IndexFaker)
        .RuleFor(c => c.FirstName, f => f.Name.FirstName())
        .RuleFor(c => c.LastName, f => f.Name.LastName())
        .RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.FirstName, c.LastName))
        .RuleFor(c => c.Phone, f => f.Phone.PhoneNumber())
        .RuleFor(c => c.Company, f => f.Company.CompanyName())
        .RuleFor(c => c.City, f => f.Address.City())
        .RuleFor(c => c.ZipCode, f => f.Address.ZipCode())
        .RuleFor(c => c.BirthDate, f => f.Date.Past(60))
        .RuleFor(c => c.CreatedAt, f => f.Date.Recent(365))
        .RuleFor(c => c.Balance, f => f.Finance.Amount());

    public static Bogus.Faker<Shopper> BogusGraph()
    {
        var line = new Bogus.Faker<PurchaseLine>("en").UseSeed(Seed)
            .RuleFor(l => l.Sku, f => f.Commerce.Ean13())
            .RuleFor(l => l.Description, f => f.Commerce.ProductName())
            .RuleFor(l => l.Quantity, f => f.Random.Int(1, 10))
            .RuleFor(l => l.UnitPrice, f => f.Finance.Amount());
        var purchase = new Bogus.Faker<Purchase>("en").UseSeed(Seed)
            .RuleFor(p => p.Id, f => f.Random.Guid())
            .RuleFor(p => p.Total, f => f.Finance.Amount())
            .RuleFor(p => p.PlacedAt, f => f.Date.Recent(365))
            .RuleFor(p => p.Status, f => f.PickRandom<PurchaseStatus>())
            .RuleFor(p => p.Lines, f => line.Generate(2));
        var address = new Bogus.Faker<Address>("en").UseSeed(Seed)
            .RuleFor(a => a.Street, f => f.Address.StreetAddress())
            .RuleFor(a => a.City, f => f.Address.City())
            .RuleFor(a => a.ZipCode, f => f.Address.ZipCode())
            .RuleFor(a => a.Country, f => f.Address.Country());
        return new Bogus.Faker<Shopper>("en").UseSeed(Seed)
            .RuleFor(s => s.Id, f => f.IndexFaker)
            .RuleFor(s => s.FirstName, f => f.Name.FirstName())
            .RuleFor(s => s.LastName, f => f.Name.LastName())
            .RuleFor(s => s.Email, (f, s) => f.Internet.Email(s.FirstName, s.LastName))
            .RuleFor(s => s.Home, f => address.Generate())
            .RuleFor(s => s.Purchases, f => purchase.Generate(2));
    }

    // AutoBogus: fills every member by type, no rules.
    public static AutoFaker<FlatCustomer> AutoBogusFlat()
    {
        var faker = new AutoFaker<FlatCustomer>();
        faker.UseSeed(Seed);
        return faker;
    }

    public static AutoFaker<Shopper> AutoBogusGraph()
    {
        AutoFaker.Configure(builder => builder.WithRepeatCount(2));
        var faker = new AutoFaker<Shopper>();
        faker.UseSeed(Seed);
        return faker;
    }

    // AutoFixture: anonymous values by type, two elements per collection.
    public static Fixture AutoFixture()
    {
        var fixture = new Fixture();
        fixture.RepeatCount = 2;
        return fixture;
    }

    // NBuilder: sequential values for the simple members; nested objects and lists have to be composed by hand.
    public static IList<FlatCustomer> NBuilderFlat(int count) => Builder<FlatCustomer>.CreateListOfSize(count).Build();

    public static IList<Shopper> NBuilderGraph(int count) =>
        Builder<Shopper>.CreateListOfSize(count).All()
            .Do(s =>
            {
                s.Home = Builder<Address>.CreateNew().Build();
                s.Purchases = [.. Builder<Purchase>.CreateListOfSize(2).All()
                    .Do(p => p.Lines = [.. Builder<PurchaseLine>.CreateListOfSize(2).Build()])
                    .Build()];
            })
            .Build();
}
