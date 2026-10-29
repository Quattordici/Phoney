using Microsoft.CodeAnalysis;
using Phoney.Generation;
using Phoney.ShapeCases;

namespace Phoney.SourceGenerator.Tests;

/// <summary>
/// How generated models make a value of each kind of member type, read from Roslyn symbols. The runtime's tests run the same table (<c>ValueShapeCases</c>)
/// through <c>System.Type</c>, so a type both models don't make the same way fails one of them.
/// </summary>
public sealed class ValueShapeTests
{
    private static readonly Lazy<INamedTypeSymbol> Holder = new(() =>
    {
        var properties = string.Join("\n", ValueShapeCases.All.Select((c, i) => $"    public {c.Source} P{i} {{ get; set; }}"));
        var compilation = GeneratorHarness.Compile($$"""
            public class Holder
            {
            {{properties}}
            }
            """);
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return compilation.GetTypeByMetadataName("Holder")!;
    });

    public static IEnumerable<Func<int>> Rows() => Enumerable.Range(0, ValueShapeCases.All.Length).Select(i => (Func<int>)(() => i));

    [Test]
    [MethodDataSource(nameof(Rows))]
    public void A_member_type_has_the_shape_both_models_give_it(int row)
    {
        var (source, _, shape) = ValueShapeCases.All[row];
        var type = ((IPropertySymbol)Holder.Value.GetMembers("P" + row).Single()).Type;

        ValueShapes.Of(type).ShouldBe(shape, source);
    }
}
