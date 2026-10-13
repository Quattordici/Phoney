using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

public sealed class UsageAnalyzerTests
{
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
            public Address? Address { get; set; }
        }

        public record Person(string FirstName, string LastName)
        {
            public string Email { get; set; } = "";
        }

        public struct Point { public int X; public int Y; }

        public abstract class Shape { public string Label { get; set; } = ""; }

        public static class Usage
        {
            private static readonly IDisposable Scope = Fake.Seeded(7);

        """;

    private static async Task<List<Diagnostic>> Diagnose(string id, string body, CancellationToken cancellationToken) =>
        [.. (await GeneratorHarness.Analyze(Types + body + "\n}", new UsageAnalyzer(), cancellationToken)).Where(d => d.Id == id)];

    private static string TextOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    // PHONEY005: results of immutable generators that are thrown away

    [Test]
    public async Task Discarded_generator_configuration_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY005", """
            public static void A()
            {
                var generator = Fake.For<Product>();
                generator.With(p => p.Name, "x");
                generator.Seed(1).Locale("sv");
                generator.Ignore(p => p.Price);
            }
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics.Select(TextOf).ShouldBe(["With", "Locale", "Ignore"], ignoreOrder: true);
        diagnostics[0].GetMessage().ShouldContain("immutable");
    }

    [Test]
    public async Task Discarded_Populate_of_a_struct_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY005", """
            public static void A()
            {
                var point = new Point();
                Fake.Populate(point);
                Fake.For<Point>().Populate(point);
                Fake.For<Point>().Populate(new[] { point });
            }
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics[0].GetMessage().ShouldContain("Point");
    }

    [Test]
    public async Task Used_results_and_class_populate_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY005", """
            public static Generator<Product> A() => Fake.For<Product>().With(p => p.Name, "x");
            public static void B()
            {
                var generator = Fake.For<Product>();
                var named = generator.With(p => p.Name, "x");
                _ = generator.Seed(1);
                var point = Fake.Populate(new Point());
                Fake.Populate(new Product());
                Fake.For<Product>().Populate(new Product());
                generator.Generate();
                named.Generate(2);
            }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    // PHONEY006: Fake.Seeded scopes that are never disposed

    [Test]
    public async Task Seeded_scope_that_is_dropped_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY006", """
            public static void A() { Fake.Seeded(1); }
            public static void B() { _ = Fake.Seeded(1, "sv"); }
            public static void C() { var scope = Fake.Seeded(1); Fake.One<Product>(); }
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics.ShouldAllBe(d => TextOf(d) == "Seeded");
        diagnostics[0].GetMessage().ShouldContain("using");
    }

    [Test]
    public async Task Disposed_or_kept_seeded_scopes_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY006", """
            public static void A() { using var _ = Fake.Seeded(1); }
            public static void B() { using (Fake.Seeded(1)) { } }
            public static void C() { using (var scope = Fake.Seeded(1)) { } }
            public static void D() { var scope = Fake.Seeded(1); scope.Dispose(); }
            public static IDisposable E() => Fake.Seeded(1);
            public static IDisposable F() { var scope = Fake.Seeded(1); return scope; }
            public static void G(List<IDisposable> scopes) { scopes.Add(Fake.Seeded(1)); }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    // PHONEY007: rules that throw when the generator is first used

    [Test]
    public async Task Rule_for_a_member_that_cannot_be_generated_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY007", """
            public static object A() => Fake.For<Product>().With(p => p.Code, "x");
            public static object B() => Fake.For<Product>().Ignore(p => p.Code).WithOneOf(p => p.Code, new[] { "a" }).Unique(p => p.Code);
            """, cancellationToken);

        diagnostics.Count.ShouldBe(4);
        diagnostics.ShouldAllBe(d => d.GetMessage().Contains("'Product.Code'") && d.GetMessage().Contains("settable"));
        TextOf(diagnostics[0]).ShouldBe("p => p.Code");
    }

    [Test]
    public async Task Selector_that_is_not_a_direct_member_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY007", """
            public static object A() => Fake.For<Product>().With(p => p.Address!.Street, "x");
            public static object B() => Fake.For<Product>().With(p => p.Name.Length, 3);
            public static object C() => Fake.For<Product>().Ignore(p => p.Name.ToUpper());
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics.ShouldAllBe(d => d.GetMessage().Contains("directly"));
        diagnostics[0].GetMessage().ShouldContain("Fake.For<");
    }

    [Test]
    public async Task Dependent_rule_on_a_constructor_parameter_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY007", """
            public static object A() => Fake.For<Person>().With(p => p.FirstName, (f, p) => p.LastName);
            """, cancellationToken);

        var message = diagnostics.ShouldHaveSingleItem().GetMessage();
        message.ShouldContain("'Person.FirstName'");
        message.ShouldContain("constructor parameter");
    }

    [Test]
    public async Task Valid_rules_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY007", """
            public static object A() => Fake.For<Product>().With(p => p.Name, "x").With(p => (object)p.Id, 1).With(p => p.Price, f => 2m).Ignore(p => p.Address).Unique(p => p.Name!);
            public static object B() => Fake.For<Product>().With(p => p.Name!, (f, p) => "x").WithOneOf(p => p.Id, new[] { 1, 2 }).With(p => p.Address, Fake.For<Address>().With(a => a.Street, "s"));
            public static object C() => Fake.For<Person>().With(p => p.FirstName, "Ada").With(p => p.Email, (f, p) => p.FirstName + "@x.se").Ignore(p => p.LastName);
            public static object D() => new Generator<Product>(null!).With(p => p.Code, "custom model, members unknown");
            public static object E() => Fake.For<Shape>().With(s => s.Label, "abstract types are not analyzed");
            public static object F(System.Linq.Expressions.Expression<Func<Product, string>> member) => Fake.For<Product>().With(member, "unknown selector");
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    // PHONEY008: rules that replace each other

    [Test]
    public async Task Rule_replaced_in_the_same_chain_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY008", """
            public static object A() => Fake.For<Product>().With(p => p.Name, "a").With(p => p.Name, "b");
            public static object B() => Fake.For<Product>().With(p => p.Price, 1m).Seed(1).Ignore(p => p.Price);
            public static object C() => Fake.For<Product>().Ignore(p => (object)p.Id).WithOneOf(p => p.Id, new[] { 1 });
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics.ShouldAllBe(d => d.GetMessage().Contains("replaces"));
        diagnostics[0].GetMessage().ShouldContain("'Name'");
        TextOf(diagnostics[0]).ShouldBe("p => p.Name");
    }

    [Test]
    public async Task Different_members_derived_generators_and_unique_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY008", """
            public static object A() => Fake.For<Product>().With(p => p.Name, "a").With(p => p.Price, 1m).Unique(p => p.Name);
            public static object B(Generator<Product> baseline) => baseline.With(p => p.Name, "variant");
            public static object C()
            {
                var baseline = Fake.For<Product>().With(p => p.Name, "a");
                return baseline.With(p => p.Name, "b");
            }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
