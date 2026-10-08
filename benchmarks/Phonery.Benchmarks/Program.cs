using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Phonery;
using Phonery.Data;
using Phonery.Generation;

// Run all:     dotnet run -c Release --project benchmarks/Phonery.Benchmarks -- --filter '*'
// Quick check: dotnet run -c Release --project benchmarks/Phonery.Benchmarks -- --filter '*' --job short
BenchmarkSwitcher.FromAssembly(typeof(ValueBenchmarks).Assembly).Run(args);

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

    [Benchmark(Description = "Randomizer.Int")]
    public int RandomInt() => _faker.Random.Int(0, 1000);
}

/// <summary>Object generation through the reflection model and the source-generated model.</summary>
[MemoryDiagnoser]
public class GenerationBenchmarks
{
    private Generator<Customer> _reflection = null!;
    private Generator<Customer> _generated = null!;

    [Params(1_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reflection = new Generator<Customer>(new ReflectionModel<Customer>()).Seed(1);
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

/// <summary>Cost of loading a locale's embedded data (done once per locale per process).</summary>
[MemoryDiagnoser]
public class LocaleLoadBenchmarks
{
    [Params("en", "de", "ja")]
    public string Locale { get; set; } = "en";

    [Benchmark(Description = "Read embedded locale")]
    public Entry?[]? Load() => LocaleResourceReader.ReadEmbedded(Locale);
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

[FakeFor<Customer>]
public static partial class BenchmarkFakes;
