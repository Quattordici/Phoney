using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Phony.SourceGenerator.Tests;

/// <summary>Runs <see cref="FakeForGenerator"/> over source text with the Phony runtime referenced.</summary>
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
}
