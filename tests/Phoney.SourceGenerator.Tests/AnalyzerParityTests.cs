using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Diagnostics;
using Phoney.Generation;

namespace Phoney.SourceGenerator.Tests;

/// <summary>
/// The analyzers report calls the runtime rejects: constant arguments (PHONEY011), rules that cannot be applied (PHONEY007) and members strict mode leaves uncovered (PHONEY004).
/// Each row is one call, written once: it runs against the real <see cref="ObjectGenerator{T}"/> and the same text is compiled for the analyzer, so a row where the two
/// disagree fails. A rule the analyzer cannot know (an annotation it cannot see, an app-wide registration) is left out of these rows on purpose.
/// </summary>
public sealed class AnalyzerParityTests
{
    public class Address
    {
        public string Street { get; set; } = "";
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; } = "x";
        public decimal Price { get; set; }
        public string Flavor { get; set; } = "";
        public int Shelf { get; set; }
        public Address? Address { get; set; }
        public List<Address> Addresses { get; set; } = new();
    }

    public record Shipment(string Carrier, int Boxes = 1, double Weight = 2.5);

    /// <summary>The same types, written out for the analyzer's compilation.</summary>
    private const string Types = """
        using System;
        using System.Collections.Generic;
        using Phoney;
        using Phoney.Generation;

        public class Address { public string Street { get; set; } = ""; }

        public class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Code { get; } = "x";
            public decimal Price { get; set; }
            public string Flavor { get; set; } = "";
            public int Shelf { get; set; }
            public Address? Address { get; set; }
            public List<Address> Addresses { get; set; } = new();
        }

        public record Shipment(string Carrier, int Boxes = 1, double Weight = 2.5);

        public static class Calls
        {
            public static void All()
            {

        """;

    private sealed record Row(string Call, Action Run);

    private static Row R(Action run, [CallerArgumentExpression(nameof(run))] string text = "") => new(text.Replace("() => ", ""), run);

    private static async Task<List<string>> Disagreements(Row[] rows, DiagnosticAnalyzer analyzer, string id, Func<Exception, bool> rejects, CancellationToken cancellationToken)
    {
        var source = Types + string.Join("\n", rows.Select(r => $"        {r.Call};")) + "\n    }\n}\n";
        var firstRow = Types.Count(c => c == '\n');
        var diagnostics = await GeneratorHarness.Analyze(source, analyzer, cancellationToken);
        var reported = diagnostics.Where(d => d.Id == id).Select(d => d.Location.GetLineSpan().StartLinePosition.Line).ToHashSet();

        var mismatches = new List<string>();
        for (var i = 0; i < rows.Length; i++)
        {
            var throws = Throws(rows[i].Run, rejects);
            var flagged = reported.Contains(firstRow + i);
            if (throws != flagged)
                mismatches.Add($"{rows[i].Call}: the runtime {(throws ? "rejects it" : "accepts it")} but the analyzer {(flagged ? "reports it" : "does not report it")}");
        }

        rows.Count(r => Throws(r.Run, rejects)).ShouldBeGreaterThan(rows.Length / 4, "the table should hold rejected calls");
        rows.Count(r => !Throws(r.Run, rejects)).ShouldBeGreaterThan(rows.Length / 4, "the table should hold accepted calls");
        return mismatches;
    }

    private static bool Throws(Action run, Func<Exception, bool> rejects)
    {
        try
        {
            run();
            return false;
        }
        catch (Exception e) when (rejects(e))
        {
            return true;
        }
    }

    private static bool BadArgument(Exception e) => e is ArgumentException and not ArgumentNullException;

    private static bool CannotBeApplied(Exception e) => e is ArgumentException or InvalidOperationException;

    private static bool StrictViolation(Exception e) => e is InvalidOperationException && e.Message.Contains("strict", StringComparison.OrdinalIgnoreCase);

    [Test]
    public async Task Constant_arguments_are_reported_exactly_when_the_runtime_rejects_them(CancellationToken cancellationToken)
    {
        Row[] rows =
        [
            R(() => Fake.For<Product>().NullProbability(1.5)),
            R(() => Fake.For<Product>().NullProbability(-0.1)),
            R(() => Fake.For<Product>().NullProbability(0)),
            R(() => Fake.For<Product>().NullProbability(1)),
            R(() => Fake.For<Product>().CollectionSize(-1, 3)),
            R(() => Fake.For<Product>().CollectionSize(5, 2)),
            R(() => Fake.For<Product>().CollectionSize(0, 0)),
            R(() => Fake.For<Product>().CollectionSize(2, 3)),
            R(() => Fake.For<Product>().MaxDepth(-1)),
            R(() => Fake.For<Product>().MaxDepth(0)),
            R(() => Fake.For<Product>().Generate(-1)),
            R(() => Fake.For<Product>().Generate(0)),
            R(() => Fake.For<Product>().Generate(3, 2)),
            R(() => Fake.For<Product>().Generate(-1, 2)),
            R(() => Fake.For<Product>().Generate(2, 3)),
            R(() => Fake.For<Product>().GenerateParallel(-5)),
            R(() => Fake.For<Product>().GenerateParallel(0)),
            R(() => Fake.Many<Product>(-1)),
            R(() => Fake.Many<Product>(0)),
            R(() => Fake.Many<Product>(3, 2)),
            R(() => Fake.Many<Product>(2, 3)),
            R(() => Fake.For<Product>().Unique(x => x.Id, 0)),
            R(() => Fake.For<Product>().Unique(x => x.Id, 1)),
            R(() => Fake.For<Product>().Where(x => x.Id > 0, 0)),
            R(() => Fake.For<Product>().Where(x => x.Id > 0, 1)),
            R(() => Fake.For<Product>().Where(x => x.Id, v => v > 0, 0)),
            R(() => Fake.For<Product>().Where(x => x.Id, v => v > 0, 2)),
            R(() => Fake.For<Product>().Check(x => x.Id > 0, -3)),
            R(() => Fake.For<Product>().Check(x => x.Id > 0, 1)),
            R(() => Fake.For<Product>().Check(x => x.Id > 0, 1, -4)),
            R(() => Fake.For<Product>().Check(x => x.Id > 0, 1, 0)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, -1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, double.NaN, 1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, double.PositiveInfinity, 1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, double.PositiveInfinity)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, 5, 1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, 1, 5)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, double.NaN, 1)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, 0, double.NaN)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, null, null, 16)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, null, null, 15)),
            R(() => Fake.For<Product>().WithNormalDistribution(x => x.Price, 0, 1, null, null, -1)),
        ];

        (await Disagreements(rows, new UsageAnalyzer(), "PHONEY011", BadArgument, cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Rules_are_reported_exactly_when_generating_would_reject_them(CancellationToken cancellationToken)
    {
        Row[] rows =
        [
            R(() => Fake.For<Product>().With(p => p.Name, "x").Generate()),
            R(() => Fake.For<Product>().With(p => p.Address!.Street, "x").Ignore(p => p.Address!.Street).Generate()),
            R(() => Fake.For<Product>().With(p => p.Addresses.Each().Street, "x").Generate()),
            R(() => Fake.For<Product>().WithOneOf(p => p.Addresses.Each().Street, new[] { "a" }).Generate()),
            R(() => Fake.For<Product>().With(p => p.Address!, Fake.For<Address>()).Generate()),
            R(() => Fake.For<Product>().Unique(p => new { p.Name, p.Id }).Generate()),
            R(() => Fake.For<Product>().Where(p => p.Name != null).Where(p => p.Name != null, 5).Where(p => p.Name, n => n != null).Generate()),
            R(() => Fake.For<Product>().Sequence(p => p.Name, i => "n" + i).Cycle(p => p.Id, new[] { 1, 2 }).Generate()),
            R(() => Fake.For<Product>().With(p => p.Code, "x").Generate()),
            R(() => Fake.For<Product>().Ignore(p => p.Code).Generate()),
            R(() => Fake.For<Product>().WithOneOf(p => p.Code, new[] { "a" }).Generate()),
            R(() => Fake.For<Product>().Unique(p => p.Code).Generate()),
            R(() => Fake.For<Product>().With(p => p.Name.Length, 3).Generate()),
            R(() => Fake.For<Product>().Ignore(p => p.Name.ToUpper()).Generate()),
            R(() => Fake.For<Product>().With(p => p.Addresses[0].Street, "x").Generate()),
            R(() => Fake.For<Product>().With(p => p.Id.ToString(), "x").Generate()),
            R(() => Fake.For<Product>().Unique(p => p.Address!.Street).Generate()),
            R(() => Fake.For<Product>().With(p => p.Address!.Street, (f, p) => p.Name).Generate()),
            R(() => Fake.For<Product>().Unique(p => new { p.Name, Next = p.Id + 1 }).Generate()),
            R(() => Fake.For<Product>().Unique(p => new { p.Name, p.Address!.Street }).Generate()),
            R(() => Fake.For<Product>().Ignore(p => new { p.Name, p.Id }).Generate()),
            R(() => Fake.For<Product>().Unique(p => new { p.Code, p.Id }).Generate()),
            R(() => Fake.For<Product>().Where(p => p.Code, c => true).Generate()),
            R(() => Fake.For<Product>().Where(p => p.Address!.Street, s => true).Generate()),
            R(() => Fake.For<Product>().Sequence(p => p.Code, i => "x").Generate()),
            R(() => Fake.For<Product>().Cycle(p => p.Address!.Street, new[] { "a" }).Generate()),
        ];

        (await Disagreements(rows, new UsageAnalyzer(), "PHONEY007", CannotBeApplied, cancellationToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Members_are_reported_as_uncovered_exactly_when_strict_mode_would_throw(CancellationToken cancellationToken)
    {
        Row[] rows =
        [
            R(() => Fake.For<Product>().Strict().Generate()),
            R(() => Fake.For<Product>().Strict(false).Generate()),
            R(() => Fake.For<Product>().With(p => p.Flavor, "a").With(p => p.Shelf, 1).Strict().Generate()),
            R(() => Fake.For<Product>().Ignore(p => p.Flavor).Ignore(p => p.Shelf).Strict().Generate()),
            R(() => Fake.For<Product>().WithEveryMemberOfType<string>("n").WithEveryMemberOfType<int>(1).Strict().Generate()),
            R(() => Fake.For<Product>().WithConvention<string>(n => n == "Flavor", f => "x").WithConvention<int>(n => n == "Shelf", f => 1).Strict().Generate()),
            R(() => Fake.For<Product>().Sequence(p => p.Shelf, i => (int)i).Cycle(p => p.Flavor, new[] { "a" }).Strict().Generate()),
            R(() => Fake.For<Product>().With(p => p.Flavor, "a").With(p => p.Shelf, 1).WithNameConventions(false).Strict().Generate()),
            R(() => Fake.For<Product>().With(p => p.Address!.Street, "a").Strict().Generate()),
            R(() => Fake.For<Product>().Unique(p => p.Flavor).Unique(p => p.Shelf).Strict().Generate()),
            R(() => Fake.For<Product>().Where(p => p.Flavor, f => true).Where(p => p.Shelf, s => true).Strict().Generate()),
            R(() => Fake.For<Shipment>().Strict().Generate()),
            R(() => Fake.For<Shipment>().WithParameterDefaults().Strict().Generate()),
            R(() => Fake.For<Shipment>().WithParameterDefaults().With(s => s.Carrier, "UPS").Strict().Generate()),
            R(() => Fake.For<Shipment>().With(s => s.Carrier, "UPS").Strict().Generate()),
        ];

        (await Disagreements(rows, new StrictAnalyzer(), "PHONEY004", StrictViolation, cancellationToken)).ShouldBeEmpty();
    }
}
