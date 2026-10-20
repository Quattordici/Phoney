using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.NativeAot;
using Phoney;
using Phoney.Generation;

namespace Phoney.Benchmarks.Comparison;

/// <summary>
/// The job of every comparison: the same settings for each library. With the environment variable <c>PHONEY_BENCH_AOT=1</c> the job is built and run
/// with Native AOT instead of the JIT; the instruction set is pinned because the compiler's detection of the host's (<c>native</c>) fails on CPUs with AVX-512.
/// </summary>
public sealed class ComparisonConfig : ManualConfig
{
    public ComparisonConfig()
    {
        var job = Job.Default.WithStrategy(RunStrategy.Throughput).WithLaunchCount(1).WithWarmupCount(5).WithIterationCount(15);
        if (Environment.GetEnvironmentVariable("PHONEY_BENCH_AOT") == "1")
        {
            var toolchain = NativeAotToolchain.CreateBuilder().IlcInstructionSet("x86-64-v3").UseNuGet().TargetFrameworkMoniker("net10.0").ToToolchain();
            job = job.WithRuntime(NativeAotRuntime.Net10_0).WithToolchain(toolchain);
        }

        AddJob(job);
    }
}

/// <summary>
/// Checks in every benchmark's setup that the library really makes what is timed: a library that produces incomplete or placeholder-less objects (it happens in
/// the Native AOT build, where the reflection some of them rely on is missing) must fail its benchmark instead of being timed.
/// </summary>
internal static class Verify
{
    public static void Flat(IEnumerable<FlatCustomer> items)
    {
        var list = items.ToList();
        if (list.Count != 10 || list.Any(i => string.IsNullOrEmpty(i.FirstName) || string.IsNullOrEmpty(i.Email) || string.IsNullOrEmpty(i.City)) || list.Select(i => i.FirstName).Distinct().Count() < 5)
            throw new InvalidOperationException("The library did not make complete, varying FlatCustomer objects.");
    }

    public static void Graph(IEnumerable<Shopper> items)
    {
        var list = items.ToList();
        if (list.Count != 10 || list.Any(i => string.IsNullOrEmpty(i.FirstName) || i.Purchases is not { Count: 2 } || i.Purchases.Any(p => p.Lines is not { Count: 2 })) || list.Select(i => i.FirstName).Distinct().Count() < 5)
            throw new InvalidOperationException("The library did not make complete, varying Shopper objects (two purchases of two lines each).");
    }

    public static string Text(string value) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException("The library returned an empty value.") : value;
}

// The categories are the rows of the comparison, the descriptions its columns; the "summarize" command (Summary.cs) turns the CSV reports into the README table.

/// <summary>Single values. Only libraries with a value API take part: AutoFixture, AutoBogus and NBuilder make whole objects.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[Config(typeof(ComparisonConfig))]
public class ValueComparison
{
    private readonly Phoney.Faker _phoney = new("en", seed: 1);
    private readonly Bogus.Faker _bogus = new("en") { Random = new Bogus.Randomizer(1) };

    // Each library is checked before it is timed, alone, so one that can't run (Bogus or Faker.Net under Native AOT) fails only its own rows.
    [GlobalSetup(Targets = [nameof(PhoneyFirstName), nameof(PhoneyFullName), nameof(PhoneyEmail), nameof(PhoneyStreet), nameof(PhoneySentence)])]
    public void SetupPhoney() => Verify.Text(_phoney.Person.FullName());

    [GlobalSetup(Targets = [nameof(BogusFirstName), nameof(BogusFullName), nameof(BogusEmail), nameof(BogusStreet), nameof(BogusSentence)])]
    public void SetupBogus() => Verify.Text(_bogus.Name.FullName());

    [GlobalSetup(Targets = [nameof(FakerNetFirstName), nameof(FakerNetFullName), nameof(FakerNetEmail), nameof(FakerNetStreet), nameof(FakerNetSentence)])]
    public void SetupFakerNet() => Verify.Text(global::Faker.Name.FullName());

    [BenchmarkCategory("First name"), Benchmark(Description = LibraryNames.Phoney)]
    public string PhoneyFirstName() => _phoney.Person.FirstName();

    [BenchmarkCategory("First name"), Benchmark(Description = LibraryNames.Bogus)]
    public string BogusFirstName() => _bogus.Name.FirstName();

    [BenchmarkCategory("First name"), Benchmark(Description = LibraryNames.FakerNet)]
    public string FakerNetFirstName() => global::Faker.Name.First();

    [BenchmarkCategory("Full name"), Benchmark(Description = LibraryNames.Phoney)]
    public string PhoneyFullName() => _phoney.Person.FullName();

    [BenchmarkCategory("Full name"), Benchmark(Description = LibraryNames.Bogus)]
    public string BogusFullName() => _bogus.Name.FullName();

    [BenchmarkCategory("Full name"), Benchmark(Description = LibraryNames.FakerNet)]
    public string FakerNetFullName() => global::Faker.Name.FullName();

    [BenchmarkCategory("Email"), Benchmark(Description = LibraryNames.Phoney)]
    public string PhoneyEmail() => _phoney.Internet.Email();

    [BenchmarkCategory("Email"), Benchmark(Description = LibraryNames.Bogus)]
    public string BogusEmail() => _bogus.Internet.Email();

    [BenchmarkCategory("Email"), Benchmark(Description = LibraryNames.FakerNet)]
    public string FakerNetEmail() => global::Faker.Internet.Email();

    [BenchmarkCategory("Street address"), Benchmark(Description = LibraryNames.Phoney)]
    public string PhoneyStreet() => _phoney.Location.StreetAddress();

    [BenchmarkCategory("Street address"), Benchmark(Description = LibraryNames.Bogus)]
    public string BogusStreet() => _bogus.Address.StreetAddress();

    [BenchmarkCategory("Street address"), Benchmark(Description = LibraryNames.FakerNet)]
    public string FakerNetStreet() => global::Faker.Address.StreetAddress();

    [BenchmarkCategory("Sentence"), Benchmark(Description = LibraryNames.Phoney)]
    public string PhoneySentence() => _phoney.Lorem.Sentence();

    [BenchmarkCategory("Sentence"), Benchmark(Description = LibraryNames.Bogus)]
    public string BogusSentence() => _bogus.Lorem.Sentence();

    [BenchmarkCategory("Sentence"), Benchmark(Description = LibraryNames.FakerNet)]
    public string FakerNetSentence() => global::Faker.Lorem.Sentence();
}

/// <summary>1,000 objects with 11 scalar members.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[Config(typeof(ComparisonConfig))]
public class FlatComparison
{
    private Phoney.ObjectGenerator<FlatCustomer> _reflection = null!;
    private Phoney.ObjectGenerator<FlatCustomer> _generated = null!;
    private Bogus.Faker<FlatCustomer> _bogus = null!;
    private AutoFaker<FlatCustomer> _autoBogus = null!;
    private Fixture _fixture = null!;

    [Params(1_000)]
    public int Count { get; set; }

    // One setup per benchmark: it builds and warms up only its own library, so one that can't run (the reflection model under Native AOT) fails alone,
    // and first-call costs (JIT, caches, locale loading) are not part of the steady state measured.
    [GlobalSetup(Target = nameof(PhoneyReflection))]
    public void SetupReflection()
    {
        _reflection = Libraries.PhoneyFlatReflection();
        Verify.Flat(_reflection.Generate(10));
    }

    [GlobalSetup(Target = nameof(PhoneyGenerated))]
    public void SetupGenerated()
    {
        _generated = Libraries.PhoneyFlatGenerated();
        Verify.Flat(_generated.Generate(10));
    }

    [GlobalSetup(Target = nameof(Bogus))]
    public void SetupBogus()
    {
        _bogus = Libraries.BogusFlat();
        Verify.Flat(_bogus.Generate(10));
    }

    [GlobalSetup(Target = nameof(AutoBogus))]
    public void SetupAutoBogus()
    {
        _autoBogus = Libraries.AutoBogusFlat();
        Verify.Flat(_autoBogus.Generate(10));
    }

    [GlobalSetup(Target = nameof(AutoFixture))]
    public void SetupAutoFixture()
    {
        _fixture = Libraries.AutoFixture();
        Verify.Flat(_fixture.CreateMany<FlatCustomer>(10));
    }

    [GlobalSetup(Target = nameof(NBuilder))]
    public void SetupNBuilder() => Verify.Flat(Libraries.NBuilderFlat(10));

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.PhoneyReflection)]
    public List<FlatCustomer> PhoneyReflection() => _reflection.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.PhoneyGenerated)]
    public List<FlatCustomer> PhoneyGenerated() => _generated.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.Bogus)]
    public List<FlatCustomer> Bogus() => _bogus.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.AutoBogus)]
    public List<FlatCustomer> AutoBogus() => _autoBogus.Generate(Count);

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.AutoFixture)]
    public List<FlatCustomer> AutoFixture() => _fixture.CreateMany<FlatCustomer>(Count).ToList();

    [BenchmarkCategory("Flat objects"), Benchmark(Description = LibraryNames.NBuilder)]
    public IList<FlatCustomer> NBuilder() => Libraries.NBuilderFlat(Count);
}

/// <summary>1,000 objects with a nested object, two orders each and two lines per order.</summary>
[MemoryDiagnoser]
[CategoriesColumn]
[Config(typeof(ComparisonConfig))]
public class GraphComparison
{
    private Phoney.ObjectGenerator<Shopper> _reflection = null!;
    private Phoney.ObjectGenerator<Shopper> _generated = null!;
    private Bogus.Faker<Shopper> _bogus = null!;
    private AutoFaker<Shopper> _autoBogus = null!;
    private Fixture _fixture = null!;

    [Params(1_000)]
    public int Count { get; set; }

    // One setup per benchmark: it builds and warms up only its own library, so one that can't run (the reflection model under Native AOT) fails alone,
    // and first-call costs (JIT, caches, locale loading) are not part of the steady state measured.
    [GlobalSetup(Target = nameof(PhoneyReflection))]
    public void SetupReflection()
    {
        _reflection = Libraries.PhoneyGraphReflection();
        Verify.Graph(_reflection.Generate(10));
    }

    [GlobalSetup(Target = nameof(PhoneyGenerated))]
    public void SetupGenerated()
    {
        _generated = Libraries.PhoneyGraphGenerated();
        Verify.Graph(_generated.Generate(10));
    }

    [GlobalSetup(Target = nameof(Bogus))]
    public void SetupBogus()
    {
        _bogus = Libraries.BogusGraph();
        Verify.Graph(_bogus.Generate(10));
    }

    [GlobalSetup(Target = nameof(AutoBogus))]
    public void SetupAutoBogus()
    {
        _autoBogus = Libraries.AutoBogusGraph();
        Verify.Graph(_autoBogus.Generate(10));
    }

    [GlobalSetup(Target = nameof(AutoFixture))]
    public void SetupAutoFixture()
    {
        _fixture = Libraries.AutoFixture();
        Verify.Graph(_fixture.CreateMany<Shopper>(10));
    }

    [GlobalSetup(Target = nameof(NBuilder))]
    public void SetupNBuilder() => Verify.Graph(Libraries.NBuilderGraph(10));

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.PhoneyReflection)]
    public List<Shopper> PhoneyReflection() => _reflection.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.PhoneyGenerated)]
    public List<Shopper> PhoneyGenerated() => _generated.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.Bogus)]
    public List<Shopper> Bogus() => _bogus.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.AutoBogus)]
    public List<Shopper> AutoBogus() => _autoBogus.Generate(Count);

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.AutoFixture)]
    public List<Shopper> AutoFixture() => _fixture.CreateMany<Shopper>(Count).ToList();

    [BenchmarkCategory("Object graphs"), Benchmark(Description = LibraryNames.NBuilderByHand)]
    public IList<Shopper> NBuilder() => Libraries.NBuilderGraph(Count);
}
