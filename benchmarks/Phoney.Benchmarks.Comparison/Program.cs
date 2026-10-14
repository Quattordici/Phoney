using System.Diagnostics;
using System.Globalization;
using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Running;
using FizzWare.NBuilder;

// Timed comparison:  dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*'
// Cold start:        dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold
// Check:             dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- check   (prints one object per library, to see that they make comparable data)
// Summary:           dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- summarize <artifacts directory of the timed run>   (prints the README's comparison matrix)
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

if (args is ["summarize", var directory])
{
    return Summary.Run(directory);
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
        // Run through the `dotnet` host, the app's own executable otherwise.
        var host = Environment.ProcessPath!;
        string[] prefix = Path.GetFileNameWithoutExtension(host) == "dotnet" ? [typeof(ColdStart).Assembly.Location] : [];
        Console.WriteLine("| Library | Median (ms) | Fastest (ms) | Slowest (ms) |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var library in new[] { "Phoney (reflection)", "Phoney (source generated)", "Bogus", "AutoBogus", "AutoFixture", "NBuilder", "Faker.Net" })
        {
            var samples = new List<double>();
            for (var run = 0; run < 15; run++)
            {
                using var process = Process.Start(new ProcessStartInfo(host, [.. prefix, "cold-child", library]) { RedirectStandardOutput = true })!;
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

/// <summary>Joins the BenchmarkDotNet CSV reports of the timed run into the matrix of the README: one row per scenario, one column per library.</summary>
internal static class Summary
{
    private static readonly string[] Libraries = ["Phoney", "Phoney (reflection)", "Bogus", "AutoBogus", "AutoFixture", "NBuilder", "Faker.Net"];

    public static int Run(string directory)
    {
        var results = new Dictionary<(string Scenario, string Library), (double Nanoseconds, double Bytes)>();
        var scenarios = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(directory, "results"), "*-report.csv").Order(StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(file);
            var header = Split(lines[0]);
            int Column(string name) => Array.IndexOf(header, name);
            var (method, category, mean, allocated) = (Column("Method"), Column("Categories"), Column("Mean"), Column("Allocated"));
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
            {
                var fields = Split(line);
                var scenario = fields[category];
                var library = fields[method].Trim('\'');
                library = library switch
                {
                    "Phoney (source generated)" => "Phoney",
                    "NBuilder (composed by hand)" => "NBuilder",
                    _ => library,
                };
                if (!scenarios.Contains(scenario))
                    scenarios.Add(scenario);
                results[(scenario, library)] = (Nanoseconds(fields[mean]), Bytes(fields[allocated]));
            }
        }

        var order = new[] { "First name", "Full name", "Email", "Street address", "Sentence", "Flat objects", "Object graphs" };
        Console.WriteLine("| Scenario | " + string.Join(" | ", Libraries) + " |");
        Console.WriteLine("|---|" + string.Join("|", Libraries.Select(_ => "---:")) + "|");
        foreach (var scenario in order.Where(scenarios.Contains))
        {
            var fastest = Libraries.Where(l => results.ContainsKey((scenario, l))).Min(l => results[(scenario, l)].Nanoseconds);
            var cells = Libraries.Select(library =>
            {
                if (!results.TryGetValue((scenario, library), out var r))
                    return "–";
                var text = $"{Time(r.Nanoseconds)} / {Size(r.Bytes)}";
                return r.Nanoseconds == fastest ? $"**{text}**" : text;
            });
            Console.WriteLine($"| {(scenario is "Flat objects" or "Object graphs" ? scenario + " (×1,000)" : scenario)} | {string.Join(" | ", cells)} |");
        }

        return 0;
    }

    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                field.Append('"');
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return [.. fields];
    }

    private static double Nanoseconds(string text)
    {
        var parts = text.Replace(",", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var unit = parts[1] switch { "ns" => 1.0, "us" or "μs" or "µs" => 1e3, "ms" => 1e6, "s" => 1e9, _ => throw new FormatException(text) };
        return double.Parse(parts[0], CultureInfo.InvariantCulture) * unit;
    }

    private static double Bytes(string text)
    {
        var parts = text.Replace(",", "", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return 0;
        var unit = parts[1] switch { "B" => 1.0, "KB" => 1024.0, "MB" => 1024.0 * 1024, "GB" => 1024.0 * 1024 * 1024, _ => throw new FormatException(text) };
        return double.Parse(parts[0], CultureInfo.InvariantCulture) * unit;
    }

    private static string Time(double ns) => ns switch
    {
        < 1_000 => $"{ns.ToString(ns < 100 ? "0.0" : "0", CultureInfo.InvariantCulture)} ns",
        < 1_000_000 => $"{(ns / 1e3).ToString(ns < 1e4 ? "0.00" : ns < 1e5 ? "0.0" : "0", CultureInfo.InvariantCulture)} µs",
        < 1_000_000_000 => $"{(ns / 1e6).ToString(ns < 1e7 ? "0.00" : ns < 1e8 ? "0.0" : "0", CultureInfo.InvariantCulture)} ms",
        _ => $"{(ns / 1e9).ToString("0.00", CultureInfo.InvariantCulture)} s",
    };

    private static string Size(double bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString("0", CultureInfo.InvariantCulture)} B",
        < 1024 * 1024 => $"{(bytes / 1024).ToString(bytes < 10240 ? "0.0" : "0", CultureInfo.InvariantCulture)} KB",
        < 1024.0 * 1024 * 1024 => $"{(bytes / 1048576).ToString(bytes < 10485760 ? "0.0" : "0", CultureInfo.InvariantCulture)} MB",
        _ => $"{(bytes / 1073741824).ToString("0.00", CultureInfo.InvariantCulture)} GB",
    };
}
