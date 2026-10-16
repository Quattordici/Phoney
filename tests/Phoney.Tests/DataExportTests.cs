using System.Text;
using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

public enum ExportTier { Free, Pro, Team }

public sealed class ExportAddress
{
    public string City { get; set; } = "";
}

public sealed class ExportRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool Active { get; set; }
    public ExportTier Tier { get; set; }
    public DateOnly Joined { get; set; }
    public DateTimeOffset Seen { get; set; }
    public Guid Key { get; set; }
    public double Score { get; set; }
    public int? Maybe { get; set; }
    public string Note { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public ExportAddress Home { get; set; } = null!;
}

[FakeFor<ExportRow>]
public static partial class ExportFakes;

/// <summary>Writing generated objects as CSV or JSON, so they read back through <see cref="DataFeed"/>.</summary>
public sealed class DataExportTests
{
    private const string AwkwardNote = "a,b \"quoted\"\nsecond line; semicolon";

    private static ObjectGenerator<ExportRow> Rows(long seed = 3) =>
        Fake.For<ExportRow>().Seed(seed).ReferenceDate(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)).With(r => r.Note, f => f.Random.Bool() ? AwkwardNote : f.Lorem.Word());

    private static void ShouldMatchScalars(List<ExportRow> actual, List<ExportRow> expected)
    {
        actual.Count.ShouldBe(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            actual[i].Id.ShouldBe(expected[i].Id);
            actual[i].Name.ShouldBe(expected[i].Name);
            actual[i].Price.ShouldBe(expected[i].Price);
            actual[i].Active.ShouldBe(expected[i].Active);
            actual[i].Tier.ShouldBe(expected[i].Tier);
            actual[i].Joined.ShouldBe(expected[i].Joined);
            actual[i].Seen.ShouldBe(expected[i].Seen);
            actual[i].Key.ShouldBe(expected[i].Key);
            actual[i].Score.ShouldBe(expected[i].Score);
            actual[i].Maybe.ShouldBe(expected[i].Maybe);
            actual[i].Note.ShouldBe(expected[i].Note);
        }
    }

    private static List<ExportRow> ReadBack(DataFeed feed, int count) =>
        Fake.For<ExportRow>().Seed(99).WithFeed(feed, ignoreUnknownColumns: true).Generate(count);

    [Test]
    public void Csv_reads_back_through_DataFeed_with_awkward_text_intact()
    {
        var expected = Rows().Generate(40);

        var csv = Rows().ToCsv(40);
        var back = ReadBack(DataFeed.FromCsv(csv), 40);

        ShouldMatchScalars(back, expected);
        expected.Select(r => r.Note).ShouldContain(AwkwardNote);
    }

    [Test]
    public void Csv_has_a_header_of_the_scalar_members_and_skips_collections_and_nested_objects()
    {
        var csv = Rows().ToCsv(2);

        csv.Split('\n')[0].ShouldBe("Id,Name,Price,Active,Tier,Joined,Seen,Key,Score,Maybe,Note");
        csv.ShouldNotContain("Tags");
        csv.ShouldNotContain("Home");
    }

    [Test]
    public void Columns_choose_what_is_written_and_in_which_order()
    {
        var csv = Rows().ToCsv(3, columns: ["Price", "id"]);

        csv.Split('\n')[0].ShouldBe("Price,Id");
        var feed = DataFeed.FromCsv(csv);
        feed.Columns.ShouldBe(["Price", "Id"]);
        feed.Count.ShouldBe(3);
    }

    [Test]
    public void Unknown_nested_duplicate_and_empty_columns_are_refused_with_the_names_that_work()
    {
        var unknown = Should.Throw<ArgumentException>(() => Rows().ToCsv(1, columns: ["Nope"]));
        unknown.Message.ShouldContain("Nope");
        unknown.Message.ShouldContain("Price");
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, columns: ["Home"])).Message.ShouldContain("not a scalar");
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, columns: ["Tags"])).Message.ShouldContain("not a scalar");
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, columns: ["Id", "id"])).Message.ShouldContain("twice");
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, columns: [])).Message.ShouldContain("column");
    }

    [Test]
    public void A_semicolon_delimiter_round_trips()
    {
        var csv = Rows().ToCsv(10, delimiter: ';');

        csv.Split('\n')[0].ShouldStartWith("Id;Name;");
        ShouldMatchScalars(ReadBack(DataFeed.FromCsv(csv, ';'), 10), Rows().Generate(10));
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, delimiter: '"'));
        Should.Throw<ArgumentException>(() => Rows().ToCsv(1, delimiter: '\n'));
    }

    [Test]
    public void A_single_empty_column_value_is_still_a_row()
    {
        var csv = Fake.For<ExportRow>().Seed(1).With(r => r.Note, "").ToCsv(3, columns: ["Note"]);

        DataFeed.FromCsv(csv).Count.ShouldBe(3);
    }

    [Test]
    public void Zero_objects_write_only_the_header_or_an_empty_array()
    {
        Rows().ToCsv(0).ShouldBe("Id,Name,Price,Active,Tier,Joined,Seen,Key,Score,Maybe,Note\n");
        Rows().ToJson(0, indented: false).ShouldBe("[]");
    }

    [Test]
    public void Json_has_real_numbers_booleans_and_nulls_and_reads_back_through_DataFeed()
    {
        var json = Rows().NullProbability(0.5).ToJson(40);
        var expected = Rows().NullProbability(0.5).Generate(40);

        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.EnumerateArray().ToList();
        items.Count.ShouldBe(40);
        items.ShouldAllBe(i => i.GetProperty("Id").ValueKind == JsonValueKind.Number && i.GetProperty("Price").ValueKind == JsonValueKind.Number);
        items.ShouldAllBe(i => i.GetProperty("Active").ValueKind == JsonValueKind.True || i.GetProperty("Active").ValueKind == JsonValueKind.False);
        items.ShouldAllBe(i => i.GetProperty("Tier").ValueKind == JsonValueKind.String && i.GetProperty("Key").ValueKind == JsonValueKind.String);
        items.ShouldContain(i => i.GetProperty("Maybe").ValueKind == JsonValueKind.Null);
        items.ShouldContain(i => i.GetProperty("Maybe").ValueKind == JsonValueKind.Number);
        items[0].EnumerateObject().Select(p => p.Name).ShouldBe(["Id", "Name", "Price", "Active", "Tier", "Joined", "Seen", "Key", "Score", "Maybe", "Note"]);

        ShouldMatchScalars(ReadBack(DataFeed.FromJson(json), 40), expected);
    }

    [Test]
    public void Json_can_be_compact_or_indented_and_keeps_non_ascii_text_readable()
    {
        var compact = Fake.For<ExportRow>().Seed(1).With(r => r.Name, "Åsa Öberg").ToJson(1, indented: false);
        var indented = Fake.For<ExportRow>().Seed(1).With(r => r.Name, "Åsa Öberg").ToJson(1);

        compact.ShouldNotContain("\n");
        compact.ShouldContain("Åsa Öberg");
        indented.ShouldContain("\n");
        JsonSerializer.Serialize(JsonDocument.Parse(indented).RootElement).ShouldBe(JsonSerializer.Serialize(JsonDocument.Parse(compact).RootElement));
    }

    [Test]
    public void Reflection_and_generated_models_write_identical_files()
    {
        var reflected = new ObjectGenerator<ExportRow>(new ReflectionModel<ExportRow>()).Seed(11);
        var generated = ExportFakes.ExportRow.Seed(11);

        generated.ToCsv(25).ShouldBe(reflected.ToCsv(25));
        generated.ToJson(25).ShouldBe(reflected.ToJson(25));
    }

    [Test]
    public void The_same_seed_writes_the_same_file_and_the_next_call_continues_the_sequence()
    {
        Rows(4).ToCsv(5).ShouldBe(Rows(4).ToCsv(5));
        var generator = Rows(4);
        var first = generator.ToCsv(5);
        var second = generator.ToCsv(5);

        second.ShouldNotBe(first);
        DataFeed.FromCsv(first + second.Substring(second.IndexOf('\n') + 1)).Count.ShouldBe(10);
    }

    [Test]
    public void Writers_are_left_open_and_files_are_utf8_without_a_byte_order_mark()
    {
        using var writer = new StringWriter();
        Rows().WriteCsv(writer, 2);
        writer.Write("still open");
        writer.ToString().ShouldEndWith("still open");

        var directory = Path.Combine(Path.GetTempPath(), "phoney-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var csv = Path.Combine(directory, "rows.csv");
            var json = Path.Combine(directory, "rows.json");
            Fake.For<ExportRow>().Seed(2).With(r => r.Name, "Åsa").WriteCsvFile(csv, 3);
            Fake.For<ExportRow>().Seed(2).With(r => r.Name, "Åsa").WriteJsonFile(json, 3);

            File.ReadAllBytes(csv).Take(3).ShouldNotBe(Encoding.UTF8.GetPreamble());
            File.ReadAllBytes(json).Take(3).ShouldNotBe(Encoding.UTF8.GetPreamble());
            DataFeed.FromCsvFile(csv).Count.ShouldBe(3);
            DataFeed.FromJsonFile(json).Count.ShouldBe(3);
            File.ReadAllText(csv, Encoding.UTF8).ShouldContain("Åsa");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Bad_arguments_are_refused()
    {
        int negative = -1;
        Should.Throw<ArgumentNullException>(() => ((ObjectGenerator<ExportRow>)null!).ToCsv(1));
        Should.Throw<ArgumentNullException>(() => Rows().WriteCsv(null!, 1));
        Should.Throw<ArgumentNullException>(() => Rows().WriteJson(null!, 1));
        Should.Throw<ArgumentException>(() => Rows().WriteCsvFile(" ", 1));
        Should.Throw<ArgumentOutOfRangeException>(() => Rows().ToCsv(negative));
        Should.Throw<ArgumentOutOfRangeException>(() => Rows().ToJson(negative));
    }

    [Test]
    public void A_type_without_readable_scalar_members_says_so()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Fake.For<OnlyCollections>().ToCsv(1));

        exception.Message.ShouldContain("OnlyCollections");
        exception.Message.ShouldContain("scalar");
    }
}

public sealed class OnlyCollections
{
    public List<string> Tags { get; set; } = [];
}
