using System.Text;
using Phoney.DataCompiler;

// Compiles the raw faker.js export (data/raw, produced by tools/extract) into Phoney's runtime data:
//   src/Phoney/Resources/Locales/<code>.bin.br   binary locale data (Brotli)
//   src/Phoney/Data/Generated/*.g.cs             key catalog + locale catalog
//   data/report.md                              summary used in sync pull requests
//
// Usage: dotnet run --project tools/Phoney.DataCompiler [-- <repoRoot>]

var repoRoot = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
var rawDirectory = Path.Combine(repoRoot, "data", "raw");
var resourceDirectory = Path.Combine(repoRoot, "src", "Phoney", "Resources", "Locales");
var generatedDirectory = Path.Combine(repoRoot, "src", "Phoney", "Data", "Generated");

var (fakerVersion, methods, locales) = LocaleReader.Read(rawDirectory);
var catalog = new Catalog(locales);

var validator = new TemplateValidator(locales, methods);
validator.Validate();
if (validator.Errors.Count > 0)
{
    Console.Error.WriteLine($"Template validation failed with {validator.Errors.Count} error(s):");
    foreach (var error in validator.Errors)
        Console.Error.WriteLine("  " + error);
    return 1;
}

if (Directory.Exists(resourceDirectory))
    Directory.Delete(resourceDirectory, recursive: true);
Directory.CreateDirectory(resourceDirectory);
Directory.CreateDirectory(generatedDirectory);

long totalBytes = 0;
var sizes = new List<(string Code, int Entries, int Bytes)>();
foreach (var locale in locales)
{
    var bytes = LocaleWriter.Write(locale, catalog, fakerVersion);
    File.WriteAllBytes(Path.Combine(resourceDirectory, locale.Code + ".bin.br"), bytes);
    totalBytes += bytes.Length;
    sizes.Add((locale.Code, locale.Leaves.Count, bytes.Length));
}

// Outputs are committed and checked by CI, so they must be byte-identical on every OS: always use LF.
static void WriteText(string path, string text) => File.WriteAllText(path, text.ReplaceLineEndings("\n"));

WriteText(Path.Combine(generatedDirectory, "DataKeys.g.cs"), CodeEmitter.DataKeys(catalog));
WriteText(
    Path.Combine(generatedDirectory, "LocaleCatalog.g.cs"),
    CodeEmitter.LocaleCatalog(fakerVersion, locales, validator.MethodReferences.Keys));

var report = new StringBuilder();
report.AppendLine("# Locale data report");
report.AppendLine();
report.AppendLine($"Generated from `@faker-js/faker` **{fakerVersion}** by `tools/Phoney.DataCompiler`.");
report.AppendLine();
report.AppendLine($"- Locales: {locales.Count}");
report.AppendLine($"- Data keys: {catalog.Keys.Count} in {catalog.Groups.Count} entry groups");
report.AppendLine($"- Embedded size: {totalBytes / 1024.0:F0} KiB (Brotli)");
report.AppendLine();
report.AppendLine("## faker.js methods referenced by templates");
report.AppendLine();
foreach (var (method, example) in validator.MethodReferences)
    report.AppendLine($"- `{method}` — e.g. `{{{{{example}}}}}`");
report.AppendLine();
report.AppendLine("## Locales");
report.AppendLine();
report.AppendLine("| Locale | Fallback | Entries | Size (bytes) |");
report.AppendLine("|---|---|---:|---:|");
foreach (var (code, entries, bytes) in sizes)
{
    var fallback = string.Join(" → ", locales.First(l => l.Code == code).Fallback);
    report.AppendLine($"| `{code}` | {fallback} | {entries} | {bytes} |");
}

WriteText(Path.Combine(repoRoot, "data", "report.md"), report.ToString());

Console.WriteLine($"Compiled {locales.Count} locales ({catalog.Keys.Count} keys, {totalBytes / 1024.0:F0} KiB) from faker.js {fakerVersion}.");
return 0;

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Phoney.slnx")))
            return dir.FullName;
    }

    throw new InvalidOperationException("Could not find Phoney.slnx above the compiler; pass the repository root as the first argument.");
}
