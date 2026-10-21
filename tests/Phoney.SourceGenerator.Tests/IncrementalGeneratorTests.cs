using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Phoney.SourceGenerator.Tests;

/// <summary>
/// The generator is incremental: an edit that changes no annotated class reuses its results, which only works when the results
/// (sources and diagnostics with their locations) compare equal by value.
/// </summary>
public sealed class IncrementalGeneratorTests
{
    private const string Models = """
        using Phoney.Generation;

        namespace Shop;

        public class Customer
        {
            public string FirstName { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public interface IShape;

        public sealed class Circle : IShape
        {
            public double Radius { get; set; }
        }

        [FakeFor<Customer>]
        public static partial class CustomerFakes;

        [FakeFor<Customer>]
        public static class NotPartialFakes; // PHONEY001: a diagnostic, cached like a source

        [FakeSubtype<IShape, Circle>]
        public static partial class ShapeFakes;
        """;

    private static GeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create(
            [new FakeForGenerator().AsSourceGenerator()],
            parseOptions: GeneratorHarness.ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static IncrementalStepRunReason[] OutputReasons(GeneratorDriver driver) =>
        [.. driver.GetRunResult().Results.Single().TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(run => run.Outputs).Select(output => output.Reason)];

    [Test]
    public void An_unrelated_edit_reuses_the_generated_sources_and_diagnostics()
    {
        var compilation = GeneratorHarness.Compile(Models, "namespace Shop; public class Unrelated { }");
        var driver = Driver().RunGenerators(compilation);
        var first = driver.GetRunResult();
        first.Results.Single().GeneratedSources.ShouldNotBeEmpty();
        first.Diagnostics.ShouldContain(d => d.Id == "PHONEY001");

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText("namespace Shop; public class Unrelated { public int Added { get; set; } }", GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        var reasons = OutputReasons(driver);
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(r => r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged);
        driver.GetRunResult().Diagnostics.Select(Describe).ShouldBe(first.Diagnostics.Select(Describe));
        driver.GetRunResult().Results.Single().GeneratedSources.Select(s => s.SourceText.ToString())
            .ShouldBe(first.Results.Single().GeneratedSources.Select(s => s.SourceText.ToString()));
    }

    [Test]
    public void An_edit_of_an_annotated_model_generates_again()
    {
        var compilation = GeneratorHarness.Compile(Models);
        var driver = Driver().RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Models.Replace("public string Email", "public int Age { get; set; }\n    public string Email", StringComparison.Ordinal), GeneratorHarness.ParseOptions));
        driver = driver.RunGenerators(edited);

        OutputReasons(driver).ShouldContain(IncrementalStepRunReason.Modified);
        driver.GetRunResult().Results.Single().GeneratedSources.ShouldContain(s => s.SourceText.ToString().Contains("\"Age\"", StringComparison.Ordinal));
    }

    /// <summary>Everything that tells two diagnostics apart: id, file, span, severity and message.</summary>
    private static (string Id, string Path, Microsoft.CodeAnalysis.Text.TextSpan Span, DiagnosticSeverity Severity, string Message) Describe(Diagnostic diagnostic) =>
        (diagnostic.Id, diagnostic.Location.SourceTree?.FilePath ?? "", diagnostic.Location.SourceSpan, diagnostic.Severity, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
}
