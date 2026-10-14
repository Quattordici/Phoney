using System.Diagnostics;
using System.Globalization;
using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Running;
using FizzWare.NBuilder;

// Timed comparison:  dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*'
// Cold start:        dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold [library...]   (all libraries, or just the ones named)
// Cold start AOT:    dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold-aot <published benchmarks/Phoney.Benchmarks.ColdAot executable>
// Cold phases:       dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold-phases        (where Phoney's first call spends its time; set DOTNET_* variables to try runtime settings)
// Check:             dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- check   (prints one object per library, to see that they make comparable data)
// Summary:           dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- summarize <artifacts directory of the timed run>   (prints the README's comparison matrix)
if (args is ["cold-child", var library])
{
    return ColdChild(library);
}

if (args is ["cold", ..var only])
{
    return ColdStart.Run(only);
}

if (args is ["cold-aot", var binary])
{
    return ColdStart.RunExecutable("Phoney (Native AOT, source generated)", binary);
}

if (args is ["cold-phases-child"])
{
    return ColdPhases.Child();
}

if (args is ["cold-phases"])
{
    return ColdPhases.Run();
}

if (args is ["sanity"])
{
    return Sanity.Run();
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
    public static int Run(string[] only)
    {
        // Run through the `dotnet` host, the app's own executable otherwise.
        var host = Environment.ProcessPath!;
        string[] prefix = Path.GetFileNameWithoutExtension(host) == "dotnet" ? [typeof(ColdStart).Assembly.Location] : [];
        Console.WriteLine("| Library | Median (ms) | Fastest (ms) | Slowest (ms) |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var library in new[] { "Phoney (reflection)", "Phoney (source generated)", "Bogus", "AutoBogus", "AutoFixture", "NBuilder", "Faker.Net" }.Where(l => only.Length == 0 || only.Contains(l)))
        {
            Print(library, Measure(host, [.. prefix, "cold-child", library]));
        }

        return 0;
    }

    /// <summary>Measures an executable that prints the milliseconds to its first object (the Native AOT build).</summary>
    public static int RunExecutable(string label, string path)
    {
        Console.WriteLine("| Library | Median (ms) | Fastest (ms) | Slowest (ms) |");
        Console.WriteLine("|---|---:|---:|---:|");
        Print(label, Measure(Path.GetFullPath(path), []));
        return 0;
    }

    private static List<double> Measure(string executable, string[] arguments)
    {
        var samples = new List<double>();
        for (var run = 0; run < 15; run++)
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) { RedirectStandardOutput = true })!;
            samples.Add(double.Parse(process.StandardOutput.ReadToEnd().Trim(), CultureInfo.InvariantCulture));
            process.WaitForExit();
        }

        samples.Sort();
        return samples;
    }

    private static void Print(string library, List<double> samples) =>
        Console.WriteLine($"| {library} | {samples[samples.Count / 2].ToString("F1", CultureInfo.InvariantCulture)} | {samples[0].ToString("F1", CultureInfo.InvariantCulture)} | {samples[^1].ToString("F1", CultureInfo.InvariantCulture)} |");
}

/// <summary>Times the steps of Phoney's first call one after the other in fresh processes, to see which of them the start-up goes to.</summary>
internal static class ColdPhases
{
    public static int Child()
    {
        var clock = Stopwatch.StartNew();
        var marks = new List<(string Name, double Ms, double JitMs, int Methods)>();
        var (jit, methods) = (System.Runtime.JitInfo.GetCompilationTime(), System.Runtime.JitInfo.GetCompiledMethodCount());
        void Mark(string name)
        {
            var (jitNow, methodsNow) = (System.Runtime.JitInfo.GetCompilationTime(), System.Runtime.JitInfo.GetCompiledMethodCount());
            marks.Add((name, clock.Elapsed.TotalMilliseconds, (jitNow - jit).TotalMilliseconds, (int)(methodsNow - methods)));
            (jit, methods) = (jitNow, methodsNow);
            clock.Restart();
        }

        GC.KeepAlive(Phoney.Data.DataKeys.Paths.Length);
        Mark("(probe) DataKeys.Paths");
        GC.KeepAlive(Phoney.Data.DataKeys.GroupOf.Length);
        Mark("(probe) DataKeys.GroupOf");
        GC.KeepAlive(new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal));
        Mark("(probe) new ConcurrentDictionary<string,int>");
        GC.KeepAlive(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });
        Mark("(probe) new Dictionary<string,int>");
        var probeCatalog = new Dictionary<string, int>(Phoney.Data.DataKeys.Count, StringComparer.Ordinal);
        for (var id = 0; id < Phoney.Data.DataKeys.Count; id++)
            probeCatalog.Add(Phoney.Data.DataKeys.Paths[id], id);
        Mark("(probe) fill catalog dictionary");
        var probeGroups = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Phoney.Data.DataKeys.Count; i++)
        {
            var path = Phoney.Data.DataKeys.Paths[i];
            var first = path.IndexOf('.');
            var second = first < 0 ? -1 : path.IndexOf('.', first + 1);
            probeGroups.TryAdd(second < 0 ? path : path[..second], Phoney.Data.DataKeys.GroupOf[i]);
        }

        Mark("(probe) fill group dictionary");
        GC.KeepAlive(Phoney.Data.LocaleCatalog.Embedded);
        Mark("(probe) LocaleCatalog.Embedded");
        GC.KeepAlive(Phoney.Data.KeyRegistry.Count);
        Mark("(probe) KeyRegistry");

        using (var resource = typeof(Phoney.Faker).Assembly.GetManifestResourceStream("Phoney.Locales.en.bin.br")!)
        using (var brotli = new System.IO.Compression.BrotliStream(resource, System.IO.Compression.CompressionMode.Decompress))
        {
            brotli.CopyTo(Stream.Null);
        }

        Mark("locale en: decompress only");
        _ = Phoney.Data.LocaleStore.Get("en");
        Mark("locale en: load + flatten (decompress warm)");
        var faker = new Phoney.Faker();
        Mark("new Faker()");
        _ = faker.Person.FirstName();
        Mark("Person.FirstName()");
        _ = faker.Person.FullName();
        Mark("Person.FullName() (first template)");
        _ = new System.Text.RegularExpressions.Regex("[^A-Za-z0-9._+-]+").Replace("a b", "");
        Mark("(probe) first Regex");
        _ = "abc".Normalize(System.Text.NormalizationForm.FormKD);
        Mark("(probe) string.Normalize");
        GC.KeepAlive(Phoney.Modules.CharMappings.Count);
        Mark("(probe) CharMappings.Map");
        _ = faker.Internet.Email();
        Mark("Internet.Email()");
        var generator = Libraries.PhoneyFlatGenerated();
        Mark("source generated generator (model)");
        _ = generator.Generate();
        Mark("first Generate() (compile + object)");
        _ = generator.Generate();
        Mark("second Generate()");
        Console.WriteLine(string.Join(';', marks.Select(m => m.Name + "=" + m.Ms.ToString("F2", CultureInfo.InvariantCulture) + "," + m.JitMs.ToString("F2", CultureInfo.InvariantCulture) + "," + m.Methods)));
        return 0;
    }

    public static int Run()
    {
        var host = Environment.ProcessPath!;
        string[] prefix = Path.GetFileNameWithoutExtension(host) == "dotnet" ? [typeof(ColdPhases).Assembly.Location] : [];
        var samples = new Dictionary<string, List<double>>();
        var jitSamples = new Dictionary<string, List<double>>();
        var methodSamples = new Dictionary<string, List<double>>();
        var order = new List<string>();
        for (var run = 0; run < 15; run++)
        {
            using var process = Process.Start(new ProcessStartInfo(host, [.. prefix, "cold-phases-child"]) { RedirectStandardOutput = true })!;
            foreach (var part in process.StandardOutput.ReadToEnd().Trim().Split(';'))
            {
                var name = part[..part.LastIndexOf('=')];
                var numbers = part[(part.LastIndexOf('=') + 1)..].Split(',').Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
                if (!samples.ContainsKey(name))
                {
                    samples[name] = [];
                    jitSamples[name] = [];
                    methodSamples[name] = [];
                    order.Add(name);
                }

                samples[name].Add(numbers[0]);
                jitSamples[name].Add(numbers[1]);
                methodSamples[name].Add(numbers[2]);
            }

            process.WaitForExit();
        }

        static string Median(List<double> values, string format)
        {
            values.Sort();
            return values[values.Count / 2].ToString(format, CultureInfo.InvariantCulture);
        }

        Console.WriteLine("| Step | Median (ms) | of which JIT (ms) | Methods JIT-compiled |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var name in order)
            Console.WriteLine($"| {name} | {Median(samples[name], "F2")} | {Median(jitSamples[name], "F2")} | {Median(methodSamples[name], "F0")} |");

        return 0;
    }
}

/// <summary>Serialises the objects that <c>check</c> prints without reflection, so it works in the Native AOT build too.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(FlatCustomer))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Shopper))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CheckJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Makes 1,000 objects per library and checks they are real: how many differ, and whether the members that should be filled are. Run it in the Native AOT build before trusting a time there.</summary>
internal static class Sanity
{
    public static int Run()
    {
        Flat("Phoney (source generated)", () => Libraries.PhoneyFlatGenerated().Generate(1000));
        Flat("Bogus", () => Libraries.BogusFlat().Generate(1000));
        Flat("AutoBogus", () => Libraries.AutoBogusFlat().Generate(1000));
        Flat("AutoFixture", () => Libraries.AutoFixture().CreateMany<FlatCustomer>(1000).ToList());
        Flat("NBuilder", () => Libraries.NBuilderFlat(1000));
        Graph("Phoney (source generated)", () => Libraries.PhoneyGraphGenerated().Generate(1000));
        Graph("Bogus", () => Libraries.BogusGraph().Generate(1000));
        Graph("AutoBogus", () => Libraries.AutoBogusGraph().Generate(1000));
        Graph("AutoFixture", () => Libraries.AutoFixture().CreateMany<Shopper>(1000).ToList());
        Graph("NBuilder (by hand)", () => Libraries.NBuilderGraph(1000));
        return 0;
    }

    private static void Flat(string library, Func<IList<FlatCustomer>> make) => Report("Flat ", library, () =>
    {
        var items = make();
        return (items.Count, items.Select(i => i.FirstName).Distinct().Count(), items.Count(i => string.IsNullOrEmpty(i.Email) || string.IsNullOrEmpty(i.City)));
    });

    private static void Graph(string library, Func<IList<Shopper>> make) => Report("Graph", library, () =>
    {
        var items = make();
        return (items.Count, items.Select(i => i.FirstName).Distinct().Count(), items.Count(i => i.Purchases.Count != 2 || i.Purchases.Any(p => p.Lines.Count != 2)));
    });

    private static void Report(string shape, string library, Func<(int Count, int Distinct, int Incomplete)> run)
    {
        try
        {
            run(); // warm up
            var clock = Stopwatch.StartNew();
            var (count, distinct, incomplete) = run();
            clock.Stop();
            Console.WriteLine($"{shape} {library,-28} {count,5} objects, {distinct,4} distinct first names, {incomplete,4} incomplete, {clock.Elapsed.TotalMilliseconds,9:F2} ms");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{shape} {library,-28} FAILED: {exception.GetType().Name}: {exception.Message.Split('\n')[0][..Math.Min(110, exception.Message.Split('\n')[0].Length)]}");
        }
    }
}

internal static class Check
{
    public static int Run()
    {
        Try("Flat", "Phoney (reflection)", () => Libraries.PhoneyFlatReflection().Generate());
        Try("Flat", "Phoney (source generated)", () => Libraries.PhoneyFlatGenerated().Generate());
        Try("Flat", "Bogus", () => Libraries.BogusFlat().Generate());
        Try("Flat", "AutoBogus", () => Libraries.AutoBogusFlat().Generate());
        Try("Flat", "AutoFixture", () => Libraries.AutoFixture().Create<FlatCustomer>());
        Try("Flat", "NBuilder", () => Libraries.NBuilderFlat(2)[1]);
        Try("Value", "Faker.Net", () => new Dictionary<string, string> { ["Name"] = global::Faker.Name.FullName(), ["Email"] = global::Faker.Internet.Email(), ["Street"] = global::Faker.Address.StreetAddress(), ["Sentence"] = global::Faker.Lorem.Sentence() });
        Try("Graph", "Phoney (reflection)", () => Libraries.PhoneyGraphReflection().Generate());
        Try("Graph", "Phoney (source generated)", () => Libraries.PhoneyGraphGenerated().Generate());
        Try("Graph", "Bogus", () => Libraries.BogusGraph().Generate());
        Try("Graph", "AutoBogus", () => Libraries.AutoBogusGraph().Generate());
        Try("Graph", "AutoFixture", () => Libraries.AutoFixture().Create<Shopper>());
        Try("Graph", "NBuilder (by hand)", () => Libraries.NBuilderGraph(2)[1]);
        return 0;
    }

    /// <summary>Shows what a library makes, or why it can't (under Native AOT several of them can't).</summary>
    private static void Try(string shape, string library, Func<object> make)
    {
        try
        {
            Show(shape, library, make());
        }
        catch (Exception exception)
        {
            var message = exception.Message.Split('\n')[0];
            Console.WriteLine($"{shape,-5} {library,-26}\n      FAILED: {exception.GetType().Name}: {(message.Length > 200 ? message[..200] + "…" : message)}");
        }
    }

    private static void Show(string shape, string library, object value)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(value, CheckJson.Default.GetTypeInfo(value.GetType())!);
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
