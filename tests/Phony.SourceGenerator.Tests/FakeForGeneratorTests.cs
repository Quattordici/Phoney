using Microsoft.CodeAnalysis;

namespace Phony.SourceGenerator.Tests;

public sealed class FakeForGeneratorTests
{
    private const string Models = """
        using System;
        using System.Collections.Generic;
        using Phony.Generation;

        namespace Shop;

        public enum Status { Active, Blocked }

        public class Customer
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string? Nickname { get; set; }
            public decimal? Balance { get; set; }
            public required string Email { get; init; }
            public Address Address { get; init; } = null!;
            public List<Order> Orders { get; set; } = new();
            public Status Status;
        }

        public record Address(string Street, string City);

        public record Order(Guid Id, DateTime CreatedAt)
        {
            public Customer? Customer { get; set; }
        }

        [FakeFor<Customer>]
        public static partial class TestData;
        """;

    [Fact]
    public void Generates_models_for_requested_and_nested_types()
    {
        var (driver, _, _) = GeneratorHarness.Run(Models);
        var generated = driver.GetRunResult().GeneratedTrees.Single();
        Snapshot.Match(generated.ToString(), "Shop.TestData");
    }

    [Fact]
    public void Generated_code_compiles_without_errors()
    {
        var (_, output, diagnostics) = GeneratorHarness.Run(Models);
        diagnostics.ShouldBeEmpty();
        output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
    }

    [Fact]
    public void Non_partial_classes_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phony.Generation;
            public class Thing { public string Name { get; set; } = ""; }
            [FakeFor<Thing>] public static class Data { }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONY001");
    }

    [Fact]
    public void Types_that_cannot_be_created_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phony.Generation;
            public abstract class Shape { }
            [FakeFor<Shape>] public static partial class Data { }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONY002");
    }

    [Fact]
    public void Generic_containers_are_reported()
    {
        var (_, _, diagnostics) = GeneratorHarness.Run("""
            using Phony.Generation;
            public class Thing { }
            public partial class Outer<T> { [FakeFor<Thing>] public static partial class Data { } }
            """);
        diagnostics.ShouldContain(d => d.Id == "PHONY003");
    }
}
