using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Phoney.SourceGenerator.Tests;

/// <summary>Model shapes the generator handles on their own path: structs, tuples, constructor parameters without a property, add-on types; and its cached results.</summary>
public sealed class GeneratedShapeTests
{
    private static string Generate(string source, CancellationToken cancellationToken)
    {
        var (driver, output, diagnostics) = GeneratorHarness.Run(source);
        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        return string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
    }

    [Test]
    public void Structs_are_default_constructed_and_then_populated(CancellationToken cancellationToken)
    {
        var source = Generate("""
            using Phoney.Generation;
            public struct Point { public int X { get; set; } public int Y { get; set; } }
            [FakeFor<Point>] public static partial class PointFakes;
            """, cancellationToken);

        source.ShouldContain("new global::Phoney.Generation.FakeMember(\"X\"");
        source.ShouldContain("new global::Point()");
    }

    [Test]
    public void Tuple_members_are_built_part_by_part(CancellationToken cancellationToken)
    {
        var source = Generate("""
            using Phoney.Generation;
            public class Route { public (string From, int Stops) Leg { get; set; } }
            [FakeFor<Route>] public static partial class RouteFakes;
            """, cancellationToken);

        source.ShouldContain("new global::System.ValueTuple<string, int>(");
    }

    [Test]
    public void A_constructor_parameter_without_a_property_is_named_like_one(CancellationToken cancellationToken)
    {
        var source = Generate("""
            using Phoney.Generation;
            public class Ticket
            {
                public Ticket(string title) => Caption = title.ToUpperInvariant();
                public string Caption { get; }
            }
            [FakeFor<Ticket>] public static partial class TicketFakes;
            """, cancellationToken);

        source.ShouldContain("new global::Phoney.Generation.FakeMember(\"Title\"");
    }

    [Test]
    public void Add_on_types_call_the_add_on_when_it_is_referenced(CancellationToken cancellationToken)
    {
        const string Model = """
            using Phoney.Generation;
            public class Meeting { public NodaTime.Instant Start { get; set; } }
            [FakeFor<Meeting>] public static partial class MeetingFakes;
            namespace NodaTime { public struct Instant; }
            """;
        const string AddOn = """
            namespace Phoney.NodaTime
            {
                public static class NodaTimeValues
                {
                    public static global::NodaTime.Instant Instant(global::Phoney.Generation.FakeScope scope, global::Phoney.Generation.ConventionKind kind, global::Phoney.Generation.MemberConstraints? constraints = null) => default;
                }
            }
            """;

        Generate(Model + AddOn, cancellationToken).ShouldContain("global::Phoney.NodaTime.NodaTimeValues.Instant(");
        Generate(Model, cancellationToken).ShouldNotContain("NodaTimeValues"); // without the add-on the type is a nested object, as before
    }

    [Test]
    public void Cached_results_compare_by_value()
    {
        static LocationInfo At(string path) =>
            LocationInfo.From(Location.Create(CSharpSyntaxTree.ParseText("class A { }", path: path), new TextSpan(6, 1)));
        var (location, same, elsewhere) = (At("a.cs"), At("a.cs"), At("b.cs"));
        var diagnostic = new DiagnosticInfo(Diagnostics.NotPartial, location, "Fakes");
        var result = new GenerationResult("Fakes.g.cs", "source", [diagnostic]);

        AssertEqual(location, same);
        location.Equals((object)elsewhere).ShouldBeFalse();
        AssertEqual(diagnostic, new DiagnosticInfo(Diagnostics.NotPartial, same, "Fakes"));
        diagnostic.Equals((object)new DiagnosticInfo(Diagnostics.NotPartial, same, "Other")).ShouldBeFalse();
        AssertEqual(result, new GenerationResult("Fakes.g.cs", "source", [new DiagnosticInfo(Diagnostics.NotPartial, same, "Fakes")]));
        result.Equals((object)new GenerationResult("Fakes.g.cs", "changed", [diagnostic])).ShouldBeFalse();
        result.Equals((object?)null).ShouldBeFalse();

        static void AssertEqual(object value, object other)
        {
            value.Equals(other).ShouldBeTrue();
            value.GetHashCode().ShouldBe(other.GetHashCode());
        }
    }
}
