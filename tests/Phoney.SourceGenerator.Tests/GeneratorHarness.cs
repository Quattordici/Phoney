using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Phoney.SourceGenerator.Tests;

/// <summary>Runs <see cref="FakeForGenerator"/> over source text with the Phoney runtime referenced.</summary>
internal static class GeneratorHarness
{
    public static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trustedAssemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Faker).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Tests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FakeForGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (driver, output, diagnostics);
    }

    /// <summary>Analyzers run concurrently and report in no particular order; tests index into the results, so sort them by position.</summary>
    internal static ImmutableArray<Diagnostic> InSourceOrder(IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.OrderBy(d => d.Location.SourceTree?.FilePath, StringComparer.Ordinal).ThenBy(d => d.Location.SourceSpan.Start).ThenBy(d => d.Id, StringComparer.Ordinal)];

    /// <summary>Runs the generator, then <paramref name="analyzer"/> over the result; returns the analyzer's diagnostics.</summary>
    public static async Task<ImmutableArray<Diagnostic>> Analyze(string source, DiagnosticAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        var (_, output, _) = Run(source);
        var errors = output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException("Test source does not compile: " + string.Join(Environment.NewLine, errors));
        return InSourceOrder(await output.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellationToken));
    }
}
