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

        public class Circle : Shape { public double Radius { get; set; } }

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
    public async Task Invalid_and_Boundary_rules_for_a_member_that_cannot_be_generated_are_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY007", """
            public static object A() => Fake.For<Product>().Invalid(p => p.Code);
            public static object B() => Fake.For<Product>().Boundary(p => p.Code);
            public static object C() => Fake.For<Product>().Invalid(p => p.Name).Boundary(p => p.Price);
            """, cancellationToken);

        diagnostics.Count.ShouldBe(2);
        diagnostics.ShouldAllBe(d => d.GetMessage().Contains("'Product.Code'"));
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
    public async Task Invalid_and_Boundary_replace_other_rules_of_the_member(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY008", """
            public static object A() => Fake.For<Product>().With(p => p.Price, 1m).Invalid(p => p.Price);
            public static object B() => Fake.For<Product>().Invalid(p => p.Price).Boundary(p => p.Price);
            public static object C() => Fake.For<Product>().Invalid(p => p.Price).Boundary(p => p.Id);
            """, cancellationToken);

        diagnostics.Count.ShouldBe(2);
        diagnostics.ShouldAllBe(d => d.GetMessage().Contains("'Price'"));
    }

    [Test]
    public async Task Use_that_replaces_an_earlier_Use_for_the_same_type_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY008", """
            public static object A() => Fake.For<Product>().Use(Fake.For<Address>()).Seed(1).Use(Fake.For<Address>().With(a => a.Street, "x"));
            public static object B() => Fake.For<Product>().Use<Shape, Circle>(Fake.For<Circle>()).Use<Shape, Circle>(Fake.For<Circle>().With(c => c.Radius, 2));
            """, cancellationToken);

        diagnostics.Count.ShouldBe(2);
        diagnostics[0].GetMessage().ShouldContain("'Address'");
        diagnostics[1].GetMessage().ShouldContain("'Shape'");
        TextOf(diagnostics[0]).ShouldBe("Use");
    }

    [Test]
    public async Task Use_for_different_types_or_on_derived_generators_is_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY008", """
            public static object A() => Fake.For<Product>().Use(Fake.For<Address>()).Use<Shape, Circle>(Fake.For<Circle>()).Use(Fake.For<Circle>());
            public static object B(Generator<Product> baseline) => baseline.Use(Fake.For<Address>());
            public static object C()
            {
                var baseline = Fake.For<Product>().Use(Fake.For<Address>());
                return baseline.Use(Fake.For<Address>().With(a => a.Street, "x"));
            }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
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

    // PHONEY010: a new generator with a constant seed in a loop repeats itself

    [Test]
    public async Task Constant_seed_in_a_new_generator_inside_a_loop_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY010", """
            public static void A(List<Product> products)
            {
                for (var i = 0; i < 3; i++)
                    products.Add(Fake.For<Product>().Seed(1).Generate());
            }
            public static void B(List<int> ids, List<Product> products)
            {
                foreach (var id in ids)
                    products.AddRange(Fake.For<Product>().With(p => p.Id, id).Seed(42).Generate(2));
            }
            public static void C(List<Product> products)
            {
                var i = 0;
                while (i++ < 3)
                {
                    var product = Fake.For<Product>().Seed(7).Locale("sv").Populate(new Product());
                    products.Add(product);
                }
            }
            """, cancellationToken);

        diagnostics.Count.ShouldBe(3);
        diagnostics.ShouldAllBe(d => TextOf(d) == "Seed");
        diagnostics[0].GetMessage().ShouldContain("same");
    }

    [Test]
    public async Task Varying_seeds_and_generators_made_outside_the_loop_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY010", """
            public static void A(List<Product> products)
            {
                for (var i = 0; i < 3; i++)
                    products.Add(Fake.For<Product>().Seed(i).Generate());
            }
            public static void B(List<Product> products)
            {
                var generator = Fake.For<Product>().Seed(1);
                for (var i = 0; i < 3; i++)
                    products.Add(generator.Generate());
            }
            public static void C(List<Product> products)
            {
                for (var i = 0; i < 3; i++)
                    products.Add(Fake.For<Product>().Generate());
                products.Add(Fake.For<Product>().Seed(1).Generate());
            }
            public static void D(List<Product> products, int seed)
            {
                for (var i = 0; i < 3; i++)
                    products.Add(Fake.For<Product>().Seed(1).Seed(seed + i).Generate());
            }
            public static void E(List<Product> products)
            {
                // Evaluated once, before the first iteration.
                foreach (var product in Fake.For<Product>().Seed(1).Generate(3))
                    products.Add(product);
                for (var batch = Fake.For<Product>().Seed(2).Generate(2); batch.Count > 5; batch.RemoveAt(0))
                    products.Add(batch[0]);
            }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    // PHONEY011: constant arguments that make the call throw

    [Test]
    public async Task Constant_arguments_out_of_range_are_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY011", """
            public static object A() => Fake.For<Product>().NullProbability(1.5);
            public static object B() => Fake.For<Product>().NullProbability(-0.1);
            public static object C() => Fake.For<Product>().CollectionSize(5, 2);
            public static object D() => Fake.For<Product>().CollectionSize(-1, 3);
            public static object E() => Fake.For<Product>().MaxDepth(-1);
            public static object F() => Fake.For<Product>().Generate(-1);
            public static object G() => Fake.For<Product>().GenerateParallel(-5);
            public static object H() => Fake.For<Product>().Unique(p => p.Name, 0);
            public static object I() => Fake.Many<Product>(-1);
            """, cancellationToken);

        diagnostics.Count.ShouldBe(9);
        diagnostics.Select(TextOf).ShouldBe(["1.5", "-0.1", "2", "-1", "-1", "-1", "-5", "0", "-1"], ignoreOrder: true);
        diagnostics.Single(d => TextOf(d) == "1.5").GetMessage().ShouldContain("NullProbability");
        diagnostics.Single(d => TextOf(d) == "2").GetMessage().ShouldContain("at least 5");
    }

    [Test]
    public async Task Valid_and_unknown_arguments_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("PHONEY011", """
            private const int Many = 3;
            public static object A() => Fake.For<Product>().NullProbability(0).NullProbability(1).CollectionSize(0, 0).CollectionSize(2, Many).MaxDepth(0).Unique(p => p.Name).Unique(p => p.Id, 1);
            public static object B() => (Fake.For<Product>().Generate(0), Fake.For<Product>().Generate(Many));
            public static object C(int n, double p) => Fake.For<Product>().NullProbability(p).CollectionSize(n, 1).CollectionSize(2, n).MaxDepth(n).Generate(n);
            public static object D() => Fake.Many<Product>(0);
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
