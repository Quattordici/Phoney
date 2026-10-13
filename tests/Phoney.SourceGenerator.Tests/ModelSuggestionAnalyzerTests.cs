using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

public sealed class ModelSuggestionAnalyzerTests
{
    private const string Types = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using Phoney;
        using Phoney.Generation;

        public class Address { public string Street { get; set; } = ""; }
        public class Product { public string Name { get; set; } = ""; public Address? Address { get; set; } }
        public class Order { public Product? Product { get; set; } }
        public interface IShape;
        public sealed class Circle : IShape { public double Radius { get; set; } }
        public abstract class Animal { }
        public class Box<T> { public T? Value { get; set; } }
        public class NoConstructor { private NoConstructor() { } }

        """;

    private const string Usage = """

        public static class Usage
        {
            public static object A() => Fake.For<Product>();
            public static object B() => Fake.One<Product>();
            public static object C() => Fake.Many<Product>(2);
            public static object D() => Fake.Populate(new Product());
        }
        """;

    private static async Task<List<Diagnostic>> Diagnose(string source, CancellationToken cancellationToken) =>
        [.. (await GeneratorHarness.Analyze(Types + source, new ModelSuggestionAnalyzer(), cancellationToken)).Where(d => d.Id == "PHONEY012")];

    [Test]
    public async Task Reflection_use_of_a_type_without_a_model_is_suggested(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose(Usage, cancellationToken);

        diagnostics.Count.ShouldBe(4);
        diagnostics.ShouldAllBe(d => d.Severity == DiagnosticSeverity.Info);
        diagnostics[0].GetMessage().ShouldContain("[FakeFor<Product>]");
        diagnostics.Select(d => d.Location.SourceTree!.ToString().Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length))
            .ShouldBe(["For<Product>", "One<Product>", "Many<Product>", "Populate"], ignoreOrder: true);
    }

    [Test]
    [Arguments("[FakeFor<Product>] public static partial class Data;")]
    [Arguments("[FakeFor<Order>] public static partial class Data;")]
    [Arguments("[FakeFor<Order>] public partial record Data;")]
    [Arguments("[FakeFor<Address>, FakeFor<Product>] public static partial class Data;")]
    public async Task Types_with_a_model_in_the_compilation_are_not_reported(string declaration, CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose(declaration + Usage, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public async Task Subtypes_with_a_model_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            [FakeSubtype<IShape, Circle>] public static partial class Data;
            public static class Usage { public static object A() => Fake.For<Circle>(); }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public async Task Types_that_cannot_have_a_model_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static class Usage
            {
                public static object A() => Fake.For<string>();
                public static object B() => Fake.For<IShape>();
                public static object C() => Fake.For<Animal>();
                public static object D() => Fake.For<Box<int>>();
                public static object E() => Fake.For<List<int>>();
                public static object F() => Fake.For<NoConstructor>();
                public static object G<T>() => Fake.For<T>();
            }
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
