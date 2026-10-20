using System.Diagnostics;
using System.Globalization;

namespace Phoney.Benchmarks.Comparison;

internal static class ColdStart
{
    public static int Run(string[] only)
    {
        // Run through the `dotnet` host, the app's own executable otherwise.
        var host = Environment.ProcessPath!;
        string[] prefix = Path.GetFileNameWithoutExtension(host) == "dotnet" ? [typeof(ColdStart).Assembly.Location] : [];
        Console.WriteLine("| Library | Median (ms) | Fastest (ms) | Slowest (ms) |");
        Console.WriteLine("|---|---:|---:|---:|");
        foreach (var library in new[] { LibraryNames.PhoneyReflection, LibraryNames.PhoneyGenerated, LibraryNames.Bogus, LibraryNames.AutoBogus, LibraryNames.AutoFixture, LibraryNames.NBuilder, LibraryNames.FakerNet }.Where(l => only.Length == 0 || only.Contains(l)))
        {
            Print(library, Measure(host, [.. prefix, "cold-child", library]));
        }

        return 0;
    }

    /// <summary>The sorted times of 15 fresh processes, or <see langword="null"/> when the library can't run (it throws, as several do in the Native AOT build).</summary>
    private static List<double>? Measure(string executable, string[] arguments)
    {
        var samples = new List<double>();
        for (var run = 0; run < 15; run++)
        {
            using var process = Process.Start(new ProcessStartInfo(executable, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || !double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
                return null;
            samples.Add(milliseconds);
        }

        samples.Sort();
        return samples;
    }

    private static void Print(string library, List<double>? samples) =>
        Console.WriteLine(samples is null ? $"| {library} | fails | | |" : $"| {library} | {samples[samples.Count / 2].ToString("F1", CultureInfo.InvariantCulture)} | {samples[0].ToString("F1", CultureInfo.InvariantCulture)} | {samples[^1].ToString("F1", CultureInfo.InvariantCulture)} |");
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
        _ = new System.Text.RegularExpressions.Regex("[^A-Za-z0-9._+-]+", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Replace("a b", "");
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
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || !TryParseMarks(output, out var marks))
            {
                Console.Error.WriteLine($"cold-phases: run {run + 1} failed (exit code {process.ExitCode}, output '{output}').");
                return 1;
            }

            foreach (var (name, ms, jitMs, methods) in marks)
            {
                if (!samples.ContainsKey(name))
                {
                    samples[name] = [];
                    jitSamples[name] = [];
                    methodSamples[name] = [];
                    order.Add(name);
                }

                samples[name].Add(ms);
                jitSamples[name].Add(jitMs);
                methodSamples[name].Add(methods);
            }
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

    /// <summary>Reads the <c>name=ms,jitMs,methods</c> records that <see cref="Child"/> prints, separated by ';'; false when any of them is malformed.</summary>
    private static bool TryParseMarks(string output, out List<(string Name, double Ms, double JitMs, double Methods)> marks)
    {
        marks = [];
        if (output.Length == 0)
            return false;
        foreach (var part in output.Split(';'))
        {
            var at = part.LastIndexOf('=');
            var numbers = at > 0 ? part[(at + 1)..].Split(',') : [];
            if (numbers.Length != 3
                || !double.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                || !double.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var jitMs)
                || !double.TryParse(numbers[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var methods))
                return false;
            marks.Add((part[..at], ms, jitMs, methods));
        }

        return true;
    }
}
