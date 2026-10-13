using Microsoft.CodeAnalysis.Diagnostics;

namespace Phoney.SourceGenerator.Tests;

public sealed class DiagnosticCategoryTests
{
    [Test]
    public void Advisory_rules_share_the_Phoney_Usage_category_so_they_can_be_switched_off_together()
    {
        DiagnosticAnalyzer[] analyzers = [new StrictAnalyzer(), new UsageAnalyzer(), new ModelSuggestionAnalyzer()];
        var descriptors = analyzers.SelectMany(a => a.SupportedDiagnostics).ToList();

        descriptors.Select(d => d.Id).Order().ShouldBe(
            ["PHONEY004", "PHONEY005", "PHONEY006", "PHONEY007", "PHONEY008", "PHONEY010", "PHONEY011", "PHONEY012"]);
        descriptors.ShouldAllBe(d => d.Category == "Phoney.Usage");
    }

    [Test]
    public void Generator_diagnostics_stay_in_the_Phoney_category()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public class Thing { public string Name { get; set; } = ""; }
            public abstract class Shape { }
            public interface IShape;
            public abstract class Square : IShape;
            [FakeFor<Thing>] public static class NotPartial { }
            [FakeFor<Shape>, FakeSubtype<IShape, Square>] public static partial class Data { }
            public partial class Outer<T> { [FakeFor<Thing>] public static partial class Inner { } }
            """);

        diagnostics.Select(d => d.Id).Distinct().Order().ShouldBe(["PHONEY001", "PHONEY002", "PHONEY003", "PHONEY009"]);
        diagnostics.ShouldAllBe(d => d.Descriptor.Category == "Phoney");
    }
}
