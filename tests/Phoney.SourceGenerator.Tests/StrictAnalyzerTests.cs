using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

public sealed class StrictAnalyzerTests
{
    private const string Types = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using Phoney;
        using Phoney.Generation;

        public class Product
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Price { get; set; }
            public string Flavor { get; set; } = "";
            public int Shelf { get; set; }
            [Range(1, 10)] public int Batch { get; set; }
            public Product? Parent { get; set; }
        }

        [FakeFor<Product>]
        public static partial class Data;

        public static class Usage
        {
        """;

    private static async Task<List<Diagnostic>> Diagnose(string body, CancellationToken cancellationToken) =>
        [.. (await GeneratorHarness.Analyze(Types + body + "\n}", new StrictAnalyzer(), cancellationToken)).Where(d => d.Id == "PHONEY004")];

    [Test]
    public async Task Reports_scalar_members_without_rule_convention_or_annotation(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("public static object A() => Fake.For<Product>().Strict().Generate();", cancellationToken);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.GetMessage().ShouldContain("'Product'");
        diagnostic.GetMessage().ShouldContain("Flavor, Shelf");
        diagnostic.GetMessage().ShouldNotContain("Batch");
        diagnostic.GetMessage().ShouldNotContain("Price");
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length).ShouldBe("Strict");
    }

    [Test]
    public async Task Rules_before_and_after_Strict_cover_members(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Fake.For<Product>().With(p => p.Flavor, "Mint").Strict().Ignore(p => (object)p.Shelf).Generate(3);
            public static object B() => Data.Product.Strict().WithOneOf(x => x.Flavor, new[] { "a" }).Unique(x => x.Shelf).Stream();
            public static object C() => new ObjectGenerator<Product>(null!).WithConvention<string>(n => true, f => "x").With(p => p.Shelf, f => 1).Strict().GenerateParallel(2);
            public static object F() => new ObjectGenerator<Product>(null!).Strict().WithFeed(null!).Generate(2);
            public static object E() => new ObjectGenerator<Product>(null!).Sequence(p => p.Flavor, i => "f" + i).Cycle(p => p.Shelf, new int?[] { 1 }).Strict().Generate(2);
            public static object D() => new ObjectGenerator<Product>(null!).WithEveryMemberOfType("x").With(p => p.Shelf, f => 1).Strict().GenerateParallel(2);
            public static object G() => Fake.For<Product>().WithNormalDistribution(p => p.Shelf, 5, 1).With(p => p.Flavor, "Mint").Strict().Generate(2);
            public static object H() => Fake.For<Product>().WithOneOf(p => p.Flavor, ("a", 3), ("b", 1)).WithOneOf(p => p.Shelf, new[] { 1, 2 }, new[] { 9.0, 1.0 }).Strict().Generate(2);
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public async Task Invalid_and_Boundary_rules_cover_members(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            // Flavor and Shelf have no annotation or convention, so only the rule covers them (what it does at runtime is not the analyzer's concern).
            public static object A() => Data.Product.Strict().Invalid(p => p.Shelf).Ignore(p => p.Flavor).Generate();
            public static object B() => Data.Product.Boundary(p => p.Flavor).Strict().Ignore(p => (object)p.Shelf).Generate(2);
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public async Task Rules_for_members_of_nested_objects_do_not_cover_members_of_the_generated_type(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("public static object A() => Data.Product.Strict().With(p => p.Parent!.Flavor, \"x\").Generate();", cancellationToken);

        var message = diagnostics.ShouldHaveSingleItem().GetMessage();
        message.ShouldContain("Flavor, Shelf"); // Product.Flavor is not Product.Parent.Flavor
    }

    [Test]
    public async Task Turning_conventions_off_reports_convention_members_too(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("public static object A() => Data.Product.WithNameConventions(false).Strict().Ignore(p => p.Flavor).Ignore(p => p.Shelf).Generate();", cancellationToken);

        var message = diagnostics.ShouldHaveSingleItem().GetMessage();
        message.ShouldContain("Id, Name, Price");
        message.ShouldNotContain("Batch");
    }

    [Test]
    public async Task Chains_that_may_get_more_rules_elsewhere_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static ObjectGenerator<Product> Configured() => Fake.For<Product>().Strict();
            public static object FromVariable(ObjectGenerator<Product> g) => g.Strict().Generate();
            public static object NotStrict() => Fake.For<Product>().Strict(false).Generate();
            public static object Unknown(bool strict) => Fake.For<Product>().Strict(strict).Generate();
            public static object UnknownMember(System.Linq.Expressions.Expression<Func<Product, string>> e) => Fake.For<Product>().Strict().Ignore(e).Generate();
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
