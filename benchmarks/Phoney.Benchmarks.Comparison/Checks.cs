using System.Diagnostics;
using AutoFixture;

namespace Phoney.Benchmarks.Comparison;

/// <summary>Serialises the objects that <c>check</c> prints without reflection, so it works in the Native AOT build too.</summary>
[System.Text.Json.Serialization.JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(FlatCustomer))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Shopper))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CheckJson : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>Makes 1,000 objects per library and checks they are real: how many differ, and whether the members that should be filled are. Run it in the Native AOT build before trusting a time there.</summary>
internal static class Sanity
{
    public static int Run()
    {
        Flat(LibraryNames.PhoneyGenerated, () => Libraries.PhoneyFlatGenerated().Generate(1000));
        Flat(LibraryNames.Bogus, () => Libraries.BogusFlat().Generate(1000));
        Flat(LibraryNames.AutoBogus, () => Libraries.AutoBogusFlat().Generate(1000));
        Flat(LibraryNames.AutoFixture, () => Libraries.AutoFixture().CreateMany<FlatCustomer>(1000).ToList());
        Flat(LibraryNames.NBuilder, () => Libraries.NBuilderFlat(1000));
        Graph(LibraryNames.PhoneyGenerated, () => Libraries.PhoneyGraphGenerated().Generate(1000));
        Graph(LibraryNames.Bogus, () => Libraries.BogusGraph().Generate(1000));
        Graph(LibraryNames.AutoBogus, () => Libraries.AutoBogusGraph().Generate(1000));
        Graph(LibraryNames.AutoFixture, () => Libraries.AutoFixture().CreateMany<Shopper>(1000).ToList());
        Graph("NBuilder (by hand)", () => Libraries.NBuilderGraph(1000));
        return 0;
    }

    private static void Flat(string library, Func<IList<FlatCustomer>> make) => Report("Flat ", library, () =>
    {
        var items = make();
        return (items.Count, items.Select(i => i.FirstName).Distinct().Count(), items.Count(i => string.IsNullOrEmpty(i.Email) || string.IsNullOrEmpty(i.City)));
    });

    private static void Graph(string library, Func<IList<Shopper>> make) => Report("Graph", library, () =>
    {
        var items = make();
        return (items.Count, items.Select(i => i.FirstName).Distinct().Count(), items.Count(i => i.Purchases.Count != 2 || i.Purchases.Any(p => p.Lines.Count != 2)));
    });

    private static void Report(string shape, string library, Func<(int Count, int Distinct, int Incomplete)> run)
    {
        try
        {
            run(); // warm up
            var clock = Stopwatch.StartNew();
            var (count, distinct, incomplete) = run();
            clock.Stop();
            Console.WriteLine($"{shape} {library,-28} {count,5} objects, {distinct,4} distinct first names, {incomplete,4} incomplete, {clock.Elapsed.TotalMilliseconds,9:F2} ms");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"{shape} {library,-28} FAILED: {exception.GetType().Name}: {exception.Message.Split('\n')[0][..Math.Min(110, exception.Message.Split('\n')[0].Length)]}");
        }
    }
}

internal static class Check
{
    private const string Flat = "Flat";
    private const string Graph = "Graph";

    public static int Run()
    {
        Try(Flat, LibraryNames.PhoneyReflection, () => Libraries.PhoneyFlatReflection().Generate());
        Try(Flat, LibraryNames.PhoneyGenerated, () => Libraries.PhoneyFlatGenerated().Generate());
        Try(Flat, LibraryNames.Bogus, () => Libraries.BogusFlat().Generate());
        Try(Flat, LibraryNames.AutoBogus, () => Libraries.AutoBogusFlat().Generate());
        Try(Flat, LibraryNames.AutoFixture, () => Libraries.AutoFixture().Create<FlatCustomer>());
        Try(Flat, LibraryNames.NBuilder, () => Libraries.NBuilderFlat(2)[1]);
        Try("Value", LibraryNames.FakerNet, () => new Dictionary<string, string> { ["Name"] = global::Faker.Name.FullName(), ["Email"] = global::Faker.Internet.Email(), ["Street"] = global::Faker.Address.StreetAddress(), ["Sentence"] = global::Faker.Lorem.Sentence() });
        Try(Graph, LibraryNames.PhoneyReflection, () => Libraries.PhoneyGraphReflection().Generate());
        Try(Graph, LibraryNames.PhoneyGenerated, () => Libraries.PhoneyGraphGenerated().Generate());
        Try(Graph, LibraryNames.Bogus, () => Libraries.BogusGraph().Generate());
        Try(Graph, LibraryNames.AutoBogus, () => Libraries.AutoBogusGraph().Generate());
        Try(Graph, LibraryNames.AutoFixture, () => Libraries.AutoFixture().Create<Shopper>());
        Try(Graph, "NBuilder (by hand)", () => Libraries.NBuilderGraph(2)[1]);
        return 0;
    }

    /// <summary>Shows what a library makes, or why it can't (under Native AOT several of them can't).</summary>
    private static void Try(string shape, string library, Func<object> make)
    {
        try
        {
            Show(shape, library, make());
        }
        catch (Exception exception)
        {
            var message = exception.Message.Split('\n')[0];
            Console.WriteLine($"{shape,-5} {library,-26}\n      FAILED: {exception.GetType().Name}: {(message.Length > 200 ? message[..200] + "…" : message)}");
        }
    }

    private static void Show(string shape, string library, object value)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(value, CheckJson.Default.GetTypeInfo(value.GetType())!);
        Console.WriteLine($"{shape,-5} {library,-26}{GraphCounts(value)}\n      {(text.Length > 420 ? text[..420] + "…" : text)}");
    }

    /// <summary>The shape of a <see cref="Shopper"/> graph, which the JSON cuts short; nothing for other values.</summary>
    private static string GraphCounts(object value)
    {
        if (value is not Shopper s)
            return "";
        var home = s.Home is null ? "null" : "set";
        return $"  [purchases={s.Purchases.Count}, lines={string.Join("/", s.Purchases.Select(p => p.Lines.Count))}, home={home}]";
    }
}
