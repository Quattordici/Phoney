using System.Text;

namespace Phoney.Tests.Adversarial;

public sealed class FeedTarget
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public Single Kind { get; set; }
    public int? Maybe { get; set; }
}

/// <summary>Feeds that are malformed, ragged, huge or random: each is read, or refused with the line, row or column at fault, never with an unrelated exception.</summary>
public sealed class FeedAbuseTests
{
    [Test]
    [Arguments("", "no header")]
    [Arguments("a,b", "no rows")]
    [Arguments("a,b\n1\n", "line 2")]
    [Arguments("a\n\"abc\n", "never closed")]
    [Arguments("a,A\n1,2", "same name")]
    [Arguments(",\n1,2", "no name")]
    public void Malformed_csv_is_refused_with_what_is_wrong(string csv, string message) =>
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv(csv)).Message.ShouldContain(message);

    [Test]
    [Arguments("[]", "no rows")]
    [Arguments("{}", "array of objects")]
    [Arguments("[1,2]", "not an object")]
    [Arguments("[{\"a\":{\"b\":1}}]", "flat values")]
    [Arguments("[{\"a\":1},{\"b\":2}]", "same properties")]
    public void Malformed_json_is_refused_with_what_is_wrong(string json, string message) =>
        Should.Throw<InvalidOperationException>(() => DataFeed.FromJson(json)).Message.ShouldContain(message);

    [Test]
    [Arguments("[{\"a\":1}] x")]
    [Arguments("[{\"a\":")]
    public void Json_that_is_not_json_is_a_json_error(string json) =>
        Should.Throw<System.Text.Json.JsonException>(() => DataFeed.FromJson(json));

    [Test]
    public void Json_nested_deeper_than_the_reader_allows_is_a_json_error_not_a_stack_overflow() =>
        Should.Throw<System.Text.Json.JsonException>(() => DataFeed.FromJson(new string('[', 100_000) + new string(']', 100_000)));

    [Test]
    public void Quotes_byte_order_marks_and_line_endings_are_read_as_csv_means_them()
    {
        DataFeed.FromCsv("﻿Name\n1").Columns.ShouldBe(["Name"]);
        DataFeed.FromCsv("a\r\n1\r\n2\r\n").Count.ShouldBe(2);
        DataFeed.FromCsv("a\n\"x\ny\"\n").Count.ShouldBe(1);
        DataFeed.FromCsv("a\n\"say \"\"hi\"\"\"\n").Count.ShouldBe(1);
    }

    [Test]
    [Arguments('"')]
    [Arguments('\n')]
    [Arguments('\r')]
    public void A_delimiter_that_would_make_csv_ambiguous_is_refused(char delimiter) =>
        Should.Throw<ArgumentException>(() => DataFeed.FromCsv("a\n1", delimiter)).ParamName.ShouldBe("delimiter");

    [Test]
    [Arguments("Number\nabc", "'abc'")]
    [Arguments("Number\n99999999999", "too large")]
    [Arguments("Number,Name\n,x", "empty")]
    [Arguments("Kind\n7", "not one of Only")]
    [Arguments("Nope\n1", "no member for: 'Nope'")]
    public void Values_that_do_not_fit_their_member_name_the_row_and_column(string csv, string message) =>
        Should.Throw<InvalidOperationException>(() => Fake.For<FeedTarget>().WithFeed(DataFeed.FromCsv(csv)).Generate()).Message.ShouldContain(message);

    [Test]
    public void A_million_rows_are_read_and_cycled_through()
    {
        var feed = DataFeed.FromCsv("Number\n" + string.Join('\n', Enumerable.Range(0, 1_000_000)));

        feed.Count.ShouldBe(1_000_000);
        Fake.For<FeedTarget>().WithFeed(feed).Generate(3).Select(t => t.Number).ShouldBe([0, 1, 2]);
    }

    /// <summary>Random text made of the characters that matter to CSV: it is a feed or an <see cref="InvalidOperationException"/>, never anything else.</summary>
    [Test]
    public void Random_csv_soup_is_either_a_feed_or_refused_with_a_reason()
    {
        const string Soup = "ab1,,\"\"\n\r ;\t﻿";
        var random = new SeededRandom(13);
        var read = 0;
        for (var i = 0; i < 20_000; i++)
        {
            var text = new StringBuilder();
            for (var c = random.Int(0, 20); c > 0; c--)
                text.Append(Soup[random.Index(Soup.Length)]);
            try
            {
                var feed = DataFeed.FromCsv(text.ToString());
                feed.Count.ShouldBeGreaterThan(0);
                read++;
            }
            catch (InvalidOperationException)
            {
                // refused with a reason
            }
        }

        read.ShouldBeGreaterThan(0);
    }
}
