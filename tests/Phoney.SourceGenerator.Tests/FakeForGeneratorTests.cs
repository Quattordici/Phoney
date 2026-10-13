using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

public sealed class FakeForGeneratorTests
{
    private const string Models = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.Text.Json.Serialization;
        using Phoney.Generation;

        namespace Shop;

        public enum Status { Active, Blocked }

        public class Customer
        {
            public int Id { get; set; }
            [StringLength(20, MinimumLength = 2)]
            public string FirstName { get; set; } = "";
            [RegularExpression("[A-Z]{3}")]
            public string? Nickname { get; set; }
            [Range(0, 10_000), Required]
            public decimal? Balance { get; set; }
            public required string Email { get; init; }
            public Address Address { get; init; } = null!;
            [MaxLength(2)]
            public List<Order> Orders { get; set; } = new();
            [AllowedValues(Status.Active)]
            public Status Status;
            public IPayment? Payment { get; set; }
        }

        [JsonDerivedType(typeof(Card))]
        public interface IPayment;

        public sealed class Card : IPayment { public string CreditCardNumber { get; set; } = ""; }

        public sealed class Swish : IPayment { public string Phone { get; set; } = ""; }

        public record Address([property: Required] string Street, [EmailAddress] string City);

        public record Order(Guid Id, DateTime CreatedAt)
        {
            public Customer? Customer { get; set; }
        }

        [FakeFor<Customer>, FakeSubtype<IPayment, Swish>]
        public static partial class TestData;
        """;

    [Test]
    public void Generates_models_for_requested_and_nested_types()
    {
        var (driver, _, _) = GeneratorHarness.Run(Models);
        var generated = driver.GetRunResult().GeneratedTrees.Single();
        Snapshot.Match(generated.ToString(), "Shop.TestData");
    }

    [Test]
    public void Generated_code_compiles_without_errors(CancellationToken cancellationToken)
    {
        var (_, output, diagnostics) = GeneratorHarness.Run(Models);
        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    [Test]
    public void Classes_with_only_FakeSubtype_get_models_and_registrations(CancellationToken cancellationToken)
    {
        var (driver, output, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public interface IShape;
            public sealed class Circle : IShape { public double Radius { get; set; } }
            [FakeSubtype<IShape, Circle>] public static partial class Shapes;
            """);
        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        source.ShouldContain("ModelRegistry.Register(__phoneyModel_Circle)");
        source.ShouldContain("global::Phoney.Fake.Subtypes.Add<global::IShape, global::Circle>();");
    }

    [Test]
    public void Non_partial_classes_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public class Thing { public string Name { get; set; } = ""; }
            [FakeFor<Thing>] public static class Data { }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONEY001");
    }

    [Test]
    public void Types_that_cannot_be_created_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public abstract class Shape { }
            [FakeFor<Shape>] public static partial class Data { }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONEY002");
    }

    [Test]
    public void Generic_containers_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public class Thing { }
            public partial class Outer<T> { [FakeFor<Thing>] public static partial class Data { } }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONEY003");
    }

    [Test]
    public void Records_can_carry_FakeFor_and_FakeSubtype(CancellationToken cancellationToken)
    {
        var (driver, output, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public interface IShape;
            public sealed class Circle : IShape { public double Radius { get; set; } }
            public class Thing { public string Name { get; set; } = ""; }
            [FakeFor<Thing>, FakeSubtype<IShape, Circle>] public partial record TestData;
            """);

        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        source.ShouldContain("partial record TestData");
        source.ShouldContain("Generator<global::Thing> Thing =>");
        source.ShouldContain("Fake.Subtypes.Add<global::IShape, global::Circle>();");
    }

    [Test]
    public void Non_partial_records_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public class Thing { public string Name { get; set; } = ""; }
            [FakeFor<Thing>] public record Data;
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONEY001");
    }

    [Test]
    [Arguments("public abstract class Square : IShape { }")]
    [Arguments("public interface ISquare : IShape { }")]
    public void Subtypes_that_cannot_be_created_are_reported_and_not_registered(string subtype)
    {
        var (driver, _, diagnostics) = GeneratorHarness.Run($$"""
            using Phoney.Generation;
            public interface IShape;
            {{subtype}}
            [FakeSubtype<IShape, {{(subtype.Contains("interface") ? "ISquare" : "Square")}}>] public static partial class Shapes;
            """);

        diagnostics.ShouldContain(d => d.Id == "PHONEY009");
        // Registering it would make the module initializer throw when the app starts.
        driver.GetRunResult().GeneratedTrees.ShouldAllBe(t => !t.ToString().Contains("Subtypes.Add"));
    }

    [Test]
    public void Subtypes_without_a_model_are_not_registered()
    {
        var (driver, _, diagnostics) = GeneratorHarness.Run("""
            using Phoney.Generation;
            public interface IShape;
            public sealed class Square : IShape { internal Square() { } }
            public sealed class Circle : IShape { public double Radius { get; set; } }
            [FakeSubtype<IShape, Square>, FakeSubtype<IShape, Circle>] public static partial class Shapes;
            """);

        diagnostics.ShouldContain(d => d.Id == "PHONEY002");
        var source = driver.GetRunResult().GeneratedTrees.Single().ToString();
        source.ShouldContain("Subtypes.Add<global::IShape, global::Circle>();");
        source.ShouldNotContain("global::Square");
    }
}
