using Phoney.Generation;

namespace Phoney.Tests;

public enum Seniority
{
    Junior,
    Senior,
}

public sealed class Employee
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int Age { get; set; }
    public decimal Salary { get; set; }
    public DateOnly Hired { get; set; }
    public Seniority Level { get; set; }
    public bool Active { get; set; }
    public Guid? Badge { get; set; }
    public string? Note { get; set; }
}

public sealed record Pilot(string Callsign, int Hours);

public sealed class LogEntry
{
    public DateTimeOffset At { get; set; }
    public DateTime Logged { get; set; }
}

/// <summary><c>WithFeed</c> fills objects from the rows of a CSV or JSON data source.</summary>
public sealed class DataFeedTests
{
    private const string People = "FirstName,LastName,Age\nAda,Lovelace,36\nGrace,Hopper,85\nAlan,Turing,41\n";

    private static ObjectGenerator<Employee> Make(DataFeed feed, FeedOrder order = FeedOrder.InOrder) =>
        Fake.For<Employee>().Seed(3).WithFeed(feed, order);

    [Test]
    public void Each_object_takes_the_next_row_and_the_rows_repeat()
    {
        var employees = Make(DataFeed.FromCsv(People)).Generate(7);

        employees.Select(e => $"{e.FirstName} {e.LastName} {e.Age}").ShouldBe(
        [
            "Ada Lovelace 36", "Grace Hopper 85", "Alan Turing 41", "Ada Lovelace 36", "Grace Hopper 85", "Alan Turing 41", "Ada Lovelace 36",
        ]);
        employees.Select(e => e.Hired).Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Test]
    public void The_row_of_item_n_does_not_depend_on_how_the_items_are_generated()
    {
        var feed = DataFeed.FromCsv(People);
        var sequential = Make(feed).Generate(8).Select(e => e.FirstName).ToList();
        var parallel = Make(feed).GenerateParallel(8).Select(e => e.FirstName).ToList();
        var batches = Make(feed);
        var split = batches.Generate(3).Concat(batches.Generate(5)).Select(e => e.FirstName).ToList();

        parallel.ShouldBe(sequential);
        split.ShouldBe(sequential);
    }

    [Test]
    public void Quoting_delimiters_line_endings_and_a_byte_order_mark_are_understood()
    {
        var csv = "﻿FirstName;LastName;Note\r\n\"Ada\";\"Love;lace\";\"said \"\"hello\"\"\r\nand left\"\r\nGrace;Hopper;\r\n";

        var employees = Make(DataFeed.FromCsv(csv, ';')).Generate(2);

        employees[0].LastName.ShouldBe("Love;lace");
        employees[0].Note.ShouldBe("said \"hello\"\r\nand left");
        employees[1].Note.ShouldBe(""); // an empty CSV value is an empty string; JSON null is null
    }

    [Test]
    public void Json_arrays_of_flat_objects_work_like_csv()
    {
        var json = """[{"firstName":"Ada","age":36,"active":true,"note":null},{"first_name":"x"}]""";

        Should.Throw<InvalidOperationException>(() => DataFeed.FromJson(json)).Message.ShouldContain("same properties");

        var employees = Make(DataFeed.FromJson("""[{"firstName":"Ada","age":36,"active":true,"note":null},{"firstName":"Grace","age":85,"active":false,"note":"x"}]""")).Generate(2);

        employees.Select(e => (e.FirstName, e.Age, e.Active, e.Note)).ShouldBe([("Ada", 36, true, null), ("Grace", 85, false, "x")]);
    }

    [Test]
    public void Values_are_converted_to_the_type_of_the_member_with_the_invariant_culture()
    {
        var badge = Guid.NewGuid();
        var csv = $"salary,hired,level,active,badge,age\n\"1234.50\",2024-03-09,senior,true,{badge},40\n,,JUNIOR,0,,1\n";

        var feed = DataFeed.FromCsv(csv);
        Should.Throw<InvalidOperationException>(() => Make(feed).Generate()).Message.ShouldContain("row 2"); // an empty decimal is not a number

        var employees = Make(DataFeed.FromCsv($"salary,hired,level,active,badge,age\n\"1234.50\",2024-03-09,senior,true,{badge},40\n1,2020-01-01,JUNIOR,0,,1\n")).Generate(2);

        employees[0].Salary.ShouldBe(1234.50m);
        employees[0].Hired.ShouldBe(new DateOnly(2024, 3, 9));
        employees[0].Level.ShouldBe(Seniority.Senior);
        employees[0].Active.ShouldBeTrue();
        employees[0].Badge.ShouldBe(badge);
        employees[1].Level.ShouldBe(Seniority.Junior);
        employees[1].Active.ShouldBeFalse();
        employees[1].Badge.ShouldBeNull();
    }

    [Test]
    public void In_random_order_an_object_still_takes_a_whole_row_and_the_choice_follows_the_seed()
    {
        var feed = DataFeed.FromCsv(People);

        var first = Make(feed, FeedOrder.Random).Generate(60);
        var again = Make(feed, FeedOrder.Random).Generate(60);

        first.Select(e => e.FirstName).ShouldBe(again.Select(e => e.FirstName));
        first.Select(e => e.FirstName).Distinct().Count().ShouldBe(3);
        first.Select(e => e.FirstName).ShouldNotBe(Make(feed).Generate(60).Select(e => e.FirstName)); // not simply in order
        first.Select(e => $"{e.FirstName} {e.LastName} {e.Age}").Distinct().Order().ShouldBe(["Ada Lovelace 36", "Alan Turing 41", "Grace Hopper 85"]); // always a whole row
        Fake.For<Employee>().Seed(4).WithFeed(feed, FeedOrder.Random).Generate(60).Select(e => e.FirstName).ShouldNotBe(first.Select(e => e.FirstName));
    }

    [Test]
    public void Column_names_match_members_ignoring_case_spaces_and_underscores()
    {
        var employees = Make(DataFeed.FromCsv("first_name,LAST NAME,age\nAda,Lovelace,36\n")).Generate(1);

        (employees[0].FirstName, employees[0].LastName, employees[0].Age).ShouldBe(("Ada", "Lovelace", 36));
    }

    [Test]
    public void A_column_without_a_member_is_an_error_unless_it_is_ignored_on_purpose()
    {
        var feed = DataFeed.FromCsv("FirstName,Nickname,Shoe size\nAda,Countess,38\n");

        var message = Should.Throw<InvalidOperationException>(() => Make(feed).Generate()).Message;

        message.ShouldContain("Nickname");
        message.ShouldContain("Shoe size");
        message.ShouldContain("Employee");
        message.ShouldContain("FirstName"); // the members it could have been
        Fake.For<Employee>().WithFeed(feed, ignoreUnknownColumns: true).Generate().FirstName.ShouldBe("Ada");
    }

    [Test]
    public void A_value_that_cannot_be_converted_names_the_row_the_column_and_the_type()
    {
        var feed = DataFeed.FromCsv("FirstName,Age\nAda,36\nGrace,old\n");

        var message = Should.Throw<InvalidOperationException>(() => Make(feed).Generate()).Message;

        message.ShouldContain("row 2");
        message.ShouldContain("Age");
        message.ShouldContain("old");
        message.ShouldContain("Int32");
    }

    [Test]
    public void Rules_for_a_member_win_over_the_feed_and_the_feed_wins_over_conventions()
    {
        var employees = Make(DataFeed.FromCsv(People)).With(e => e.LastName, "Fixed").Generate(3);

        employees.ShouldAllBe(e => e.LastName == "Fixed");
        employees.Select(e => e.FirstName).ShouldBe(["Ada", "Grace", "Alan"]); // FirstName has a name convention; the feed is stronger
    }

    [Test]
    public void Constructor_parameters_of_records_are_filled_too()
    {
        var pilots = Fake.For<Pilot>().Seed(1).WithFeed(DataFeed.FromCsv("callsign,hours\nMaverick,1200\nGoose,900\n")).Generate(3);

        pilots.Select(p => (p.Callsign, p.Hours)).ShouldBe([("Maverick", 1200), ("Goose", 900), ("Maverick", 1200)]);
    }

    [Test]
    public void Populate_takes_rows_for_the_members_that_are_unset_and_keeps_the_others()
    {
        var filled = Make(DataFeed.FromCsv(People)).Populate([new Employee(), new Employee { FirstName = "Kept" }, new Employee()]);

        filled.Select(e => e.FirstName).ShouldBe(["Ada", "Kept", "Alan"]);
        filled.Select(e => e.LastName).ShouldBe(["Lovelace", "Hopper", "Turing"]);
    }

    [Test]
    public void Generated_models_use_the_feed_the_same_way()
    {
        var members = TestFakes.Member.Seed(2).WithFeed(DataFeed.FromCsv("FirstName,Username\nAda,ada1\nGrace,grace2\n")).Generate(3);

        members.Select(m => (m.FirstName, m.Username)).ShouldBe([("Ada", "ada1"), ("Grace", "grace2"), ("Ada", "ada1")]);
        members.ShouldAllBe(m => m.Email.Length > 0);
    }

    [Test]
    public void A_generator_with_a_feed_cannot_be_nested_because_every_nested_object_would_take_the_first_row()
    {
        var fed = Make(DataFeed.FromCsv(People));

        Should.Throw<ArgumentException>(() => Fake.For<Employee>().WithGeneratorFor(fed)).Message.ShouldContain("feed");
    }

    [Test]
    public void Feeds_are_validated_when_they_are_read()
    {
        Should.Throw<ArgumentNullException>(() => DataFeed.FromCsv(null!));
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv("")).Message.ShouldContain("header");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv("a,b\n")).Message.ShouldContain("no rows");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv("a,b\n1,2,3\n")).Message.ShouldContain("line 2");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv("a,a\n1,2\n")).Message.ShouldContain("twice");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromCsv("a,b\n\"1,2\n")).Message.ShouldContain("quote");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromJson("{}")).Message.ShouldContain("array");
        Should.Throw<InvalidOperationException>(() => DataFeed.FromJson("""[{"a":{"b":1}}]""")).Message.ShouldContain("flat");
        Should.Throw<ArgumentNullException>(() => Fake.For<Employee>().WithFeed(null!));
    }

    [Test]
    public void Moments_without_an_offset_are_utc_and_moments_with_one_are_the_same_on_every_machine()
    {
        var feed = DataFeed.FromCsv("At,Logged\n2024-01-01T10:00:00,2024-01-01T10:00:00+02:00\n");

        var entry = Fake.For<LogEntry>().WithFeed(feed).Generate();

        entry.At.ShouldBe(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero));
        entry.Logged.ShouldBe(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc));
        entry.Logged.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Test]
    public void A_number_that_is_not_a_value_of_the_enum_is_an_error()
    {
        Fake.For<Employee>().WithFeed(DataFeed.FromCsv("Level\n1\n"), ignoreUnknownColumns: true).Generate().Level.ShouldBe(Seniority.Senior);

        var message = Should.Throw<InvalidOperationException>(() => Fake.For<Employee>().WithFeed(DataFeed.FromCsv("Level\n7\n")).Generate()).Message;

        message.ShouldContain("'7'");
        message.ShouldContain("Junior, Senior");
    }

    [Test]
    public void Booleans_may_have_spaces_around_them_like_numbers()
    {
        var employees = Fake.For<Employee>().WithFeed(DataFeed.FromCsv("Age, Active\n36, true\n41, no\n")).Generate(2);

        employees.Select(e => (e.Age, e.Active)).ShouldBe([(36, true), (41, false)]);
    }

    [Test]
    public void Files_are_read_and_describe_themselves()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phoney-feed-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, People);
        try
        {
            var feed = DataFeed.FromCsvFile(path);

            feed.Count.ShouldBe(3);
            feed.Columns.ShouldBe(["FirstName", "LastName", "Age"]);
        }
        finally
        {
            File.Delete(path);
        }

        Should.Throw<FileNotFoundException>(() => DataFeed.FromCsvFile(path));
    }
}
