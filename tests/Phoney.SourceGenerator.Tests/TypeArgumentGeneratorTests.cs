using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Phoney.SourceGenerator.Tests;

/// <summary>Type arguments of <c>[FakeForTypeArgument]</c> type parameters get models as if listed with <c>[FakeFor]</c>.</summary>
public sealed class TypeArgumentGeneratorTests
{
    private const string Builder = """
        using Phoney;
        using Phoney.Generation;

        namespace Testing;

        public abstract class DataBuilder<TBuilder, [FakeForTypeArgument] T> where T : class
        {
            public T Build() => Fake.ForGenerated<T>().Generate();
        }

        public static class Factory
        {
            public static ObjectGenerator<T> Create<[FakeForTypeArgument] T>() => Fake.ForGenerated<T>();
        }
        """;

    private const string Models = """
        using System.Collections.Generic;

        namespace Shop;

        public sealed record Customer(string FirstName, string Email)
        {
            public Address? Address { get; init; }
            public List<Order> Orders { get; init; } = new();
        }

        public sealed class Address { public string City { get; set; } = ""; }

        public sealed class Order { public int Id { get; set; } }

        public sealed class Product { public string Name { get; set; } = ""; }
        """;

    [Test]
    public void Base_class_type_arguments_get_registered_models(CancellationToken cancellationToken)
    {
        var (driver, output, diagnostics) = GeneratorHarness.Run(Builder, Models, """
            namespace Shop;
            internal sealed class CustomerBuilder : Testing.DataBuilder<CustomerBuilder, Customer>;
            """);

        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        source.ShouldContain("internal static class __PhoneyTypeArgumentModels_Tests");
        source.ShouldContain("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.ShouldContain("ModelRegistry.Register(__phoneyModel_Shop_Customer)");
        source.ShouldContain("FakeModel<global::Shop.Address>"); // contained types too
        source.ShouldContain("FakeModel<global::Shop.Order>");
        source.ShouldNotContain("Shop_Product");
        source.ShouldNotContain("CustomerBuilder"); // TBuilder is not marked
    }

    [Test]
    public void Explicit_generic_method_type_arguments_get_models(CancellationToken cancellationToken)
    {
        var (driver, output, diagnostics) = GeneratorHarness.Run(Builder, Models, """
            namespace Shop;
            public static class Usage
            {
                public static Product Make() => Testing.Factory.Create<Product>().Generate();
            }
            """);

        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        source.ShouldContain("ModelRegistry.Register(__phoneyModel_Shop_Product)");
        source.ShouldNotContain("Shop_Customer");
    }

    [Test]
    public void Open_abstract_and_framework_type_arguments_generate_nothing()
    {
        var (driver, _, diagnostics) = GeneratorHarness.Run(Builder, """
            namespace Shop;
            public abstract class Shape { }
            public static class Usage
            {
                public static T Make<T>() where T : class => Testing.Factory.Create<T>().Generate();
                public static Shape Abstract() => Testing.Factory.Create<Shape>().Generate();
                public static System.Uri Framework() => Testing.Factory.Create<System.Uri>().Generate();
            }
            """);

        diagnostics.ShouldBeEmpty();
        driver.GetRunResult().GeneratedTrees.ShouldBeEmpty();
    }

    [Test]
    public void Unmarked_type_parameters_generate_nothing()
    {
        var (driver, _, diagnostics) = GeneratorHarness.Run(Models, """
            namespace Shop;
            public sealed class Holder<T> { }
            public static class Usage { public static Holder<Customer> Value = new(); }
            """);

        diagnostics.ShouldBeEmpty();
        driver.GetRunResult().GeneratedTrees.ShouldBeEmpty();
    }

    [Test]
    public void Inaccessible_type_arguments_are_reported_where_they_are_passed()
    {
        const string usage = """
            namespace Shop;
            public static class Outer
            {
                private sealed class Secret { public string Name { get; set; } = ""; }
                private sealed class SecretBuilder : Testing.DataBuilder<SecretBuilder, Secret>;
            }
            """;
        var (driver, _, diagnostics) = GeneratorHarness.Run(Builder, usage);

        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Id.ShouldBe("PHONEY002");
        diagnostic.Location.SourceSpan.Start.ShouldBe(usage.IndexOf("Secret>", StringComparison.Ordinal));
        driver.GetRunResult().GeneratedTrees.ShouldBeEmpty();
    }

    [Test]
    public void Marked_type_parameters_in_referenced_assemblies_are_honoured(CancellationToken cancellationToken)
    {
        // The generic helper lives in a compiled library, as a shared test-helpers package would.
        var library = GeneratorHarness.Compile(Builder).WithAssemblyName("Library");
        using var image = new MemoryStream();
        var emitted = library.Emit(image, cancellationToken: cancellationToken);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));

        var consumer = GeneratorHarness.Compile(Models, """
                namespace Shop;
                internal sealed class CustomerBuilder : Testing.DataBuilder<CustomerBuilder, Customer>;
                """)
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FakeForGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(consumer, out var output, out var diagnostics, cancellationToken);

        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        driver.GetRunResult().GeneratedTrees.Single().ToString().ShouldContain("ModelRegistry.Register(__phoneyModel_Shop_Customer)");
    }
}
