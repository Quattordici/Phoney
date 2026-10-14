using System.Diagnostics;
using System.Globalization;
using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Running;
using FizzWare.NBuilder;

// Timed comparison:  dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*'
// Cold start:        dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold
// Check:             dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- check   (prints one object per library, to see that they make comparable data)
// Both print what the README's comparison tables are made from; tools/summarize-comparison.py joins the BenchmarkDotNet CSV reports into the matrix.
if (args is ["cold-child", var library])
{
    return ColdChild(library);
}

if (args is ["cold"])
{
    return ColdStart.Run();
}

if (args is ["check"])
{
    return Check.Run();
}

BenchmarkSwitcher.FromAssembly(typeof(ValueComparison).Assembly).Run(args);
return 0;

static int ColdChild(string library)
{
    // The time to the first object of a fresh process: runtime and library start-up are outside, loading, JIT, building rules and the first value are inside.
    var clock = Stopwatch.StartNew();
    object? first = library switch
    {
        "Phoney (reflection)" => Libraries.PhoneyFlatReflection().Generate(),
        "Phoney (source generated)" => Libraries.PhoneyFlatGenerated().Generate(),
        "Bogus" => Libraries.BogusFlat().Generate(),
        "AutoBogus" => Libraries.AutoBogusFlat().Generate(),
        "AutoFixture" => Libraries.AutoFixture().Create<FlatCustomer>(),
        "NBuilder" => Builder<FlatCustomer>.CreateNew().Build(),
        "Faker.Net" => global::Faker.Name.First(),
        _ => throw new ArgumentException("Unknown library " + library),
    };
    clock.Stop();
    GC.KeepAlive(first);
    Console.WriteLine(clock.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
    return 0;
}

internal static class ColdStart
{
    public static int Run()
    {
        var self = typeof(ColdStart).Assembly.Location;
        Console.WriteLine("| Library | Median (ms) | Fastest (ms) | Slowest (ms) |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var library in new[] { "Phoney (reflection)", "Phoney (source generated)", "Bogus", "AutoBogus", "AutoFixture", "NBuilder", "Faker.Net" })
        {
            var samples = new List<double>();
            for (var run = 0; run < 15; run++)
            {
                using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, [self, "cold-child", library]) { RedirectStandardOutput = true })!;
                samples.Add(double.Parse(process.StandardOutput.ReadToEnd().Trim(), CultureInfo.InvariantCulture));
                process.WaitForExit();
            }

            samples.Sort();
            Console.WriteLine($"| {library} | {samples[samples.Count / 2].ToString("F1", CultureInfo.InvariantCulture)} | {samples[0].ToString("F1", CultureInfo.InvariantCulture)} | {samples[^1].ToString("F1", CultureInfo.InvariantCulture)} |");
        }

        return 0;
    }
}

internal static class Check
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static int Run()
    {
        Show("Flat", "Phoney (reflection)", Libraries.PhoneyFlatReflection().Generate());
        Show("Flat", "Phoney (source generated)", Libraries.PhoneyFlatGenerated().Generate());
        Show("Flat", "Bogus", Libraries.BogusFlat().Generate());
        Show("Flat", "AutoBogus", Libraries.AutoBogusFlat().Generate());
        Show("Flat", "AutoFixture", Libraries.AutoFixture().Create<FlatCustomer>());
        Show("Flat", "NBuilder", Libraries.NBuilderFlat(2)[1]);
        Show("Graph", "Phoney (reflection)", Libraries.PhoneyGraphReflection().Generate());
        Show("Graph", "Phoney (source generated)", Libraries.PhoneyGraphGenerated().Generate());
        Show("Graph", "Bogus", Libraries.BogusGraph().Generate());
        Show("Graph", "AutoBogus", Libraries.AutoBogusGraph().Generate());
        Show("Graph", "AutoFixture", Libraries.AutoFixture().Create<Shopper>());
        Show("Graph", "NBuilder (by hand)", Libraries.NBuilderGraph(2)[1]);
        return 0;
    }

    private static void Show(string shape, string library, object value)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), Json);
        var shape2 = value is Shopper s ? $"  [purchases={s.Purchases.Count}, lines={string.Join("/", s.Purchases.Select(p => p.Lines.Count))}, home={(s.Home is null ? "null" : "set")}]" : "";
        Console.WriteLine($"{shape,-5} {library,-26}{shape2}\n      {(text.Length > 420 ? text[..420] + "…" : text)}");
    }
}
