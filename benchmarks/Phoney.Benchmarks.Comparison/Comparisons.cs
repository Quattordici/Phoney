using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Phoney;
using Phoney.Generation;

// The categories are the rows of the comparison, the descriptions its columns; tools/summarize.py (see the README) turns the CSV reports into the table.

/// <summary>Single values. Only libraries with a value API take part: AutoFixture, AutoBogus and NBuilder make whole objects.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 15)]
public class ValueComparison
{
    private readonly Phoney.Faker _phoney = new("en", seed: 1);
    private readonly Bogus.Faker _bogus = new("en") { Random = new Bogus.Randomizer(1) };

    [BenchmarkCategory("First name"), Benchmark(Description = "Phoney")]
    public string PhoneyFirstName() => _phoney.Person.FirstName();

    [BenchmarkCategory("First name"), Benchmark(Description = "Bogus")]
    public string BogusFirstName() => _bogus.Name.FirstName();

    [BenchmarkCategory("First name"), Benchmark(Description = "Faker.Net")]
    public string FakerNetFirstName() => global::Faker.Name.First();

    [BenchmarkCategory("Full name"), Benchmark(Description = "Phoney")]
    public string PhoneyFullName() => _phoney.Person.FullName();

    [BenchmarkCategory("Full name"), Benchmark(Description = "Bogus")]
    public string BogusFullName() => _bogus.Name.FullName();

    [BenchmarkCategory("Full name"), Benchmark(Description = "Faker.Net")]
    public string FakerNetFullName() => global::Faker.Name.FullName();

    [BenchmarkCategory("Email"), Benchmark(Description = "Phoney")]
    public string PhoneyEmail() => _phoney.Internet.Email();

    [BenchmarkCategory("Email"), Benchmark(Description = "Bogus")]
    public string BogusEmail() => _bogus.Internet.Email();

    [BenchmarkCategory("Email"), Benchmark(Description = "Faker.Net")]
    public string FakerNetEmail() => global::Faker.Internet.Email();

    [BenchmarkCategory("Street address"), Benchmark(Description = "Phoney")]
    public string PhoneyStreet() => _phoney.Location.StreetAddress();

    [BenchmarkCategory("Street address"), Benchmark(Description = "Bogus")]
    public string BogusStreet() => _bogus.Address.StreetAddress();

    [BenchmarkCategory("Street address"), Benchmark(Description = "Faker.Net")]
    public string FakerNetStreet() => global::Faker.Address.StreetAddress();

    [BenchmarkCategory("Sentence"), Benchmark(Description = "Phoney")]
    public string PhoneySentence() => _phoney.Lorem.Sentence();

    [BenchmarkCategory("Sentence"), Benchmark(Description = "Bogus")]
    public string BogusSentence() => _bogus.Lorem.Sentence();

    [BenchmarkCategory("Sentence"), Benchmark(Description = "Faker.Net")]
    public string FakerNetSentence() => global::Faker.Lorem.Sentence();
}

/// <summary>1,000 objects with 11 scalar members.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 15)]
public class FlatComparison
{
    private Phoney.Generator<FlatCustomer> _reflection = null!;
    private Phoney.Generator<FlatCustomer> _generated = null!;
    private Bogus.Faker<FlatCustomer> _bogus = null!;
    private AutoFaker<FlatCustomer> _autoBogus = null!;
    private Fixture _fixture = null!;

    [Params(1_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reflection = Libraries.PhoneyFlatReflection();
        _generated = Libraries.PhoneyFlatGenerated();
        _bogus = Libraries.BogusFlat();
        _autoBogus = Libraries.AutoBogusFlat();
        _fixture = Libraries.AutoFixture();

        // One warm-up pass of each, so first-call costs (JIT, caches, locale loading) are not part of the steady state measured.
        _reflection.Generate(10);
        _generated.Generate(10);
        _bogus.Generate(10);
        _autoBogus.Generate(10);
        _fixture.CreateMany<FlatCustomer>(10).ToList();
        Libraries.NBuilderFlat(10);
    }

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "Phoney (reflection)")]
    public List<FlatCustomer> PhoneyReflection() => _reflection.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "Phoney (source generated)")]
    public List<FlatCustomer> PhoneyGenerated() => _generated.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "Bogus")]
    public List<FlatCustomer> Bogus() => _bogus.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "AutoBogus")]
    public List<FlatCustomer> AutoBogus() => _autoBogus.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "AutoFixture")]
    public List<FlatCustomer> AutoFixture() => _fixture.CreateMany<FlatCustomer>(Count).ToList();

    [BenchmarkCategory("Flat objects"), Benchmark(Description = "NBuilder")]
    public IList<FlatCustomer> NBuilder() => Libraries.NBuilderFlat(Count);
}

/// <summary>1,000 objects with a nested object, two orders each and two lines per order.</summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 1, warmupCount: 5, iterationCount: 15)]
public class GraphComparison
{
    private Phoney.Generator<Shopper> _reflection = null!;
    private Phoney.Generator<Shopper> _generated = null!;
    private Bogus.Faker<Shopper> _bogus = null!;
    private AutoFaker<Shopper> _autoBogus = null!;
    private Fixture _fixture = null!;

    [Params(1_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _reflection = Libraries.PhoneyGraphReflection();
        _generated = Libraries.PhoneyGraphGenerated();
        _bogus = Libraries.BogusGraph();
        _autoBogus = Libraries.AutoBogusGraph();
        _fixture = Libraries.AutoFixture();

        _reflection.Generate(10);
        _generated.Generate(10);
        _bogus.Generate(10);
        _autoBogus.Generate(10);
        _fixture.CreateMany<Shopper>(10).ToList();
        Libraries.NBuilderGraph(10);
    }

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "Phoney (reflection)")]
    public List<Shopper> PhoneyReflection() => _reflection.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "Phoney (source generated)")]
    public List<Shopper> PhoneyGenerated() => _generated.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "Bogus")]
    public List<Shopper> Bogus() => _bogus.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "AutoBogus")]
    public List<Shopper> AutoBogus() => _autoBogus.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "AutoFixture")]
    public List<Shopper> AutoFixture() => _fixture.CreateMany<Shopper>(Count).ToList();

    [BenchmarkCategory("Object graphs"), Benchmark(Description = "NBuilder (composed by hand)")]
    public IList<Shopper> NBuilder() => Libraries.NBuilderGraph(Count);
}
