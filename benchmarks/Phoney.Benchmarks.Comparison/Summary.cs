using System.Globalization;

namespace Phoney.Benchmarks.Comparison;

/// <summary>Joins the BenchmarkDotNet CSV reports of the timed run into the matrix of the README: one row per scenario, one column per library.</summary>
internal static class Summary
{
    private static readonly string[] Libraries = [LibraryNames.Phoney, LibraryNames.PhoneyReflection, LibraryNames.Bogus, LibraryNames.AutoBogus, LibraryNames.AutoFixture, LibraryNames.NBuilder, LibraryNames.FakerNet];
    private static readonly string[] Scenarios = ["First name", "Full name", "Email", "Street address", "Sentence", "Flat objects", "Object graphs"];

    public static int Run(string directory)
    {
        var reports = BenchmarkReports.Read(Path.Combine(directory, "results"));
        Console.WriteLine("| Scenario | " + string.Join(" | ", Libraries) + " |");
        Console.WriteLine("|---|" + string.Join("|", Libraries.Select(_ => "---:")) + "|");
        foreach (var scenario in Scenarios.Where(reports.Scenarios.Contains))
            Console.WriteLine($"| {RowName(scenario)} | {string.Join(" | ", Libraries.Select(library => Cell(reports, scenario, library)))} |");

        return 0;
    }

    private static string RowName(string scenario) => scenario is "Flat objects" or "Object graphs" ? scenario + " (×1,000)" : scenario;

    /// <summary>A cell of the matrix: time / allocation, in bold for the fastest library of the scenario.</summary>
    private static string Cell(BenchmarkReports reports, string scenario, string library)
    {
        if (!reports.TryGet(scenario, library, out var nanoseconds, out var bytes))
            return reports.Failed(scenario, library) ? "fails" : "–";
        var text = $"{Time(nanoseconds)} / {Size(bytes)}";
        return nanoseconds <= reports.Fastest(scenario, Libraries) ? $"**{text}**" : text; // ≤ the minimum is the minimum: ties are all bold
    }

    private static string Time(double ns) => ns switch
    {
        < 1_000 => $"{ns.ToString(ns < 100 ? "0.0" : "0", CultureInfo.InvariantCulture)} ns",
        < 1_000_000 => $"{ThreeDigits(ns / 1e3)} µs",
        < 1_000_000_000 => $"{ThreeDigits(ns / 1e6)} ms",
        _ => $"{(ns / 1e9).ToString("0.00", CultureInfo.InvariantCulture)} s",
    };

    private static string Size(double bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString("0", CultureInfo.InvariantCulture)} B",
        < 1024 * 1024 => $"{(bytes / 1024).ToString(bytes < 10240 ? "0.0" : "0", CultureInfo.InvariantCulture)} KB",
        < 1024.0 * 1024 * 1024 => $"{(bytes / 1048576).ToString(bytes < 10485760 ? "0.0" : "0", CultureInfo.InvariantCulture)} MB",
        _ => $"{(bytes / 1073741824).ToString("0.00", CultureInfo.InvariantCulture)} GB",
    };

    /// <summary>A value from 1 to 999 with three significant digits at most: 1.23, 12.3, 123.</summary>
    private static string ThreeDigits(double value)
    {
        var format = "0";
        if (value < 10)
            format = "0.00";
        else if (value < 100)
            format = "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}

/// <summary>The results in the BenchmarkDotNet CSV reports: time and allocation per scenario and library, and the ones that failed.</summary>
internal sealed class BenchmarkReports
{
    private readonly Dictionary<(string Scenario, string Library), (double Nanoseconds, double Bytes)> _results = [];
    private readonly HashSet<(string Scenario, string Library)> _failed = []; // NA: the library couldn't run, or made incomplete objects (see Verify in Comparisons.cs)

    /// <summary>The scenarios in the order the reports name them.</summary>
    public List<string> Scenarios { get; } = [];

    public static BenchmarkReports Read(string directory)
    {
        var reports = new BenchmarkReports();
        foreach (var file in Directory.GetFiles(directory, "*-report.csv").Order(StringComparer.Ordinal))
            reports.Add(File.ReadAllLines(file));
        return reports;
    }

    public bool TryGet(string scenario, string library, out double nanoseconds, out double bytes)
    {
        var found = _results.TryGetValue((scenario, library), out var result);
        (nanoseconds, bytes) = result;
        return found;
    }

    public bool Failed(string scenario, string library) => _failed.Contains((scenario, library));

    /// <summary>The shortest time of <paramref name="libraries"/> in the scenario.</summary>
    public double Fastest(string scenario, IEnumerable<string> libraries) =>
        libraries.Where(l => _results.ContainsKey((scenario, l))).Min(l => _results[(scenario, l)].Nanoseconds);

    private void Add(string[] lines)
    {
        var header = Split(lines[0]);
        int Column(string name) => Array.IndexOf(header, name);
        var (method, category, mean, allocated) = (Column("Method"), Column("Categories"), Column("Mean"), Column("Allocated"));
        foreach (var fields in lines.Skip(1).Where(l => l.Length > 0).Select(Split))
        {
            var scenario = fields[category];
            var library = MatrixColumn(fields[method].Trim('\''));
            if (!Scenarios.Contains(scenario))
                Scenarios.Add(scenario);
            if (fields[mean].Trim() == "NA")
                _failed.Add((scenario, library));
            else
                _results[(scenario, library)] = (Nanoseconds(fields[mean]), Bytes(fields[allocated]));
        }
    }

    /// <summary>The matrix column of a benchmark: the source generated Phoney is "Phoney", NBuilder composed by hand is "NBuilder".</summary>
    private static string MatrixColumn(string library) => library switch
    {
        LibraryNames.PhoneyGenerated => LibraryNames.Phoney,
        LibraryNames.NBuilderByHand => LibraryNames.NBuilder,
        _ => library,
    };

    /// <summary>The fields of a CSV line; a doubled quote inside quotes is a quote.</summary>
    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
            {
                field.Append('"');
                i += 2;
                continue;
            }

            if (c == '"')
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

            i++;
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
}
