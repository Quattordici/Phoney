using BenchmarkDotNet.Attributes;
using Phoney;
using Phoney.Data;
using Phoney.Generation;

namespace Phoney.Benchmarks;

/// <summary>Single values: data lookups should not allocate beyond the returned string.</summary>
[MemoryDiagnoser]
public class ValueBenchmarks
{
    private readonly Faker _faker = new("en", seed: 1);

    [Benchmark(Description = "Person.FirstName")]
    public string FirstName() => _faker.Person.FirstName();

    [Benchmark(Description = "Person.FullName")]
    public string FullName() => _faker.Person.FullName();

    [Benchmark(Description = "Internet.Email")]
    public string Email() => _faker.Internet.Email();

    [Benchmark(Description = "Location.StreetAddress (template)")]
    public string StreetAddress() => _faker.Location.StreetAddress();

    [Benchmark(Description = "Parse(template)")]
    public string Parse() => _faker.Parse("{{person.firstName}} {{person.lastName}} lives in {{location.city}}");

    [Benchmark(Description = "Finance.Iban")]
    public string Iban() => _faker.Finance.Iban();

    [Benchmark(Description = "Helpers.FromRegExp")]
    public string FromRegExp() => _faker.Helpers.FromRegExp("[A-Z]{3}-\\d{4}");

    [Benchmark(Description = "String.AlphaNumeric(10)")]
    public string AlphaNumeric() => _faker.String.AlphaNumeric(10);

    [Benchmark(Description = "Vehicle.Vin")]
    public string Vin() => _faker.Vehicle.Vin();

    [Benchmark(Description = "Finance.CreditCardNumber")]
    public string CreditCardNumber() => _faker.Finance.CreditCardNumber();

    [Benchmark(Description = "Internet.Ipv4")]
    public string Ipv4() => _faker.Internet.Ipv4();

    [Benchmark(Description = "Internet.Ipv6")]
    public string Ipv6() => _faker.Internet.Ipv6();

    [Benchmark(Description = "Internet.UserAgent (template with arguments)")]
    public string UserAgent() => _faker.Internet.UserAgent();

    [Benchmark(Description = "Internet.Jwt")]
    public string Jwt() => _faker.Internet.Jwt();

    [Benchmark(Description = "SeededRandom.Int")]
    public int RandomInt() => _faker.Random.Int(0, 1000);

    private static readonly double[] CumulativeWeights = [70, 95, 100];

    [Benchmark(Description = "SeededRandom.WeightedIndex (3 weights)")]
    public int RandomWeightedIndex() => _faker.Random.WeightedIndex(CumulativeWeights);

    [Benchmark(Description = "SeededRandom.Normal")]
    public double RandomNormal() => _faker.Random.Normal(175, 8);

    [Benchmark(Description = "Number.Normal<int> (limits, rounded)")]
    public int NumberNormal() => _faker.Number.Normal<int>(100, 15, min: 40, max: 160);
}

/// <summary>Object generation through the reflection model and the source-generated model.</summary>
[MemoryDiagnoser]
public class GenerationBenchmarks
{
    private ObjectGenerator<Customer> _reflection = null!;
    private ObjectGenerator<Customer> _generated = null!;

    [Params(1_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reflection = new ObjectGenerator<Customer>(new ReflectionModel<Customer>()).Seed(1);
        _generated = BenchmarkFakes.Customer.Seed(1);
        _reflection.Generate(10);
        _generated.Generate(10);
    }

    [Benchmark(Baseline = true, Description = "Reflection model")]
    public List<Customer> Reflection() => _reflection.Generate(Count);

    [Benchmark(Description = "Source-generated model")]
    public List<Customer> Generated() => _generated.Generate(Count);

    [Benchmark(Description = "Source-generated, parallel")]
    public Customer[] GeneratedParallel() => _generated.GenerateParallel(Count);
}

/// <summary>One object through the static facade, which builds a generator per call.</summary>
[MemoryDiagnoser]
public class FacadeBenchmarks
{
    [Benchmark(Baseline = true, Description = "Cached generator, Generate()")]
    public Customer Cached() => CachedGenerator.Generate();

    [Benchmark(Description = "Fake.ForGenerated<T>().Generate()")]
    public Customer One() => Fake.ForGenerated<Customer>().Generate();

    [Benchmark(Description = "Fake.ForGenerated<T>().Generate() in Fake.Seeded")]
    public Customer Seeded()
    {
        using var _ = Fake.Seeded(1);
        return Fake.ForGenerated<Customer>().Generate();
    }

    [Benchmark(Description = "Fake.ForGenerated<T>().Populate(x)")]
    public Customer Populate() => Fake.ForGenerated<Customer>().Populate(new Customer());

    private static readonly ObjectGenerator<Customer> CachedGenerator = BenchmarkFakes.Customer;
}

/// <summary>Collections of every kind through the reflection model (sets, lists, arrays, dictionaries) against the generated model.</summary>
[MemoryDiagnoser]
public class CollectionBenchmarks
{
    private ObjectGenerator<Basket> _reflection = null!;
    private ObjectGenerator<Basket> _generated = null!;

    [Params(1_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reflection = new ObjectGenerator<Basket>(new ReflectionModel<Basket>()).Seed(1).CollectionSize(5, 10);
        _generated = BenchmarkFakes.Basket.Seed(1).CollectionSize(5, 10);
        _reflection.Generate(10);
        _generated.Generate(10);
    }

    [Benchmark(Baseline = true, Description = "Collections, reflection model")]
    public List<Basket> Reflection() => _reflection.Generate(Count);

    [Benchmark(Description = "Collections, source-generated model")]
    public List<Basket> Generated() => _generated.Generate(Count);
}

/// <summary>Picking a random enum value.</summary>
[MemoryDiagnoser]
public class EnumBenchmarks
{
    private readonly SeededRandom _random = new(1);

    [Benchmark(Description = "SeededRandom.Enum")]
    public DayOfWeek Enum() => _random.Enum<DayOfWeek>();
}

/// <summary>Cost of loading a locale's embedded data (done once per locale per process).</summary>
[MemoryDiagnoser]
public class LocaleLoadBenchmarks
{
    [Params("en", "de", "ja")]
    public string Locale { get; set; } = "en";

    [Benchmark(Description = "Open embedded locale (decodes nothing)")]
    public int Open() => LocaleResource.Open(Locale)?.DecodedEntries ?? -1;

    [Benchmark(Description = "Open and decode every entry")]
    public Entry?[]? OpenAndDecodeAll() => LocaleResource.Open(Locale)?.DecodeAll();
}

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public Address Address { get; set; } = null!;
    public List<Order> Orders { get; set; } = [];
}

public sealed record Address(string Street, string City, string ZipCode, string Country);

public sealed record Order(Guid Id, decimal Total, DateTime CreatedAt);

public sealed class Basket
{
    public List<string> Items { get; set; } = [];
    public HashSet<string> Tags { get; set; } = [];
    public string[] Codes { get; set; } = [];
    public Dictionary<string, int> Quantities { get; set; } = [];
}

[FakeFor<Customer>, FakeFor<Basket>]
public static partial class BenchmarkFakes;
