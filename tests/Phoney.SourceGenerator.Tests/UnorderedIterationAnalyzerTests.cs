using Microsoft.CodeAnalysis;
using Phoney.DevAnalyzers;

namespace Phoney.SourceGenerator.Tests;

public sealed class UnorderedIterationAnalyzerTests
{
    private const string Types = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Frozen;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;

        public static class Usage
        {
            private static readonly HashSet<string> Set = new();
            private static readonly Dictionary<string, int> Map = new();
            private static readonly ConcurrentDictionary<string, int> Concurrent = new();
            private static readonly ImmutableDictionary<string, int> Immutable = ImmutableDictionary<string, int>.Empty;
            private static readonly FrozenDictionary<string, int> Frozen = FrozenDictionary<string, int>.Empty;
            private static readonly List<string> List = new();

        """;

    private static async Task<List<Diagnostic>> Diagnose(string body, CancellationToken cancellationToken) =>
        [.. (await GeneratorHarness.Analyze(Types + body + "\n}", new UnorderedIterationAnalyzer(), cancellationToken)).Where(d => d.Id == "PHONEYDEV001")];

    private static string TextOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    [Test]
    public async Task Enumerating_unordered_collections_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static void A() { foreach (var x in Set) Console.WriteLine(x); }
            public static void B() { foreach (var kv in Map) Console.WriteLine(kv); }
            public static void C() { foreach (var k in Map.Keys) Console.WriteLine(k); }
            public static void D() { foreach (var v in Concurrent.Values) Console.WriteLine(v); }
            public static void E() { foreach (var kv in Immutable) Console.WriteLine(kv); }
            public static void F() { foreach (var k in Frozen.Keys) Console.WriteLine(k); }
            """, cancellationToken);

        diagnostics.Count.ShouldBe(6);
        diagnostics.Select(TextOf).ShouldBe(["Set", "Map", "Map.Keys", "Concurrent.Values", "Immutable", "Frozen.Keys"]);
        diagnostics[0].GetMessage().ShouldContain("HashSet");
    }

    [Test]
    public async Task Order_sensitive_LINQ_and_spreads_are_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static object A() => Set.ToList();
            public static object B() => Map.Keys.Select(k => k.Length).ToArray();
            public static object C() => Concurrent.Values.First();
            public static object D() => Immutable.Keys.Where(k => k.Length > 1).Distinct();
            public static object E() => new List<string>([.. Set]);
            public static object F() => Concurrent.Keys.FirstOrDefault(k => k.Length == 3);
            """, cancellationToken);

        diagnostics.Count.ShouldBe(6);
    }

    [Test]
    public async Task Sorted_order_insensitive_and_ordered_uses_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static void A() { foreach (var x in Set.Order(StringComparer.Ordinal)) Console.WriteLine(x); }
            public static object B() => Map.Keys.Order(StringComparer.Ordinal).ToArray();
            public static object C() => Map.Keys.Where(k => k.Length > 1).Select(k => k + "!").OrderBy(k => k, StringComparer.Ordinal).ToList();
            public static object D() => (Set.Contains("a"), Set.Count, Map.ContainsKey("a"), Concurrent.Keys.Count, Map.TryGetValue("a", out _), Set.Any(), Map.Values.Sum(), Map.Values.Max());
            public static object E() => (Set.ToHashSet(), Map.ToDictionary(kv => kv.Key, kv => kv.Value), Set.ToFrozenSet(), Map.ToImmutableDictionary());
            public static void F() { foreach (var x in new SortedSet<string>()) Console.WriteLine(x); }
            public static void G() { foreach (var x in List) Console.WriteLine(x); }
            public static void H() { foreach (var x in new SortedDictionary<string, int>().Keys) Console.WriteLine(x); }
            public static object I() => List.Select(x => x).ToList();
            public static object J() => new SortedDictionary<string, int>().Values.ToArray();
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
