using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq.Expressions;
using Phoney.Generation;

namespace Phoney.Tests;

public sealed class Booking
{
    [Range(1, 10)] public int Guests { get; set; }
    [Range(0.5, 5.0)] public double Rating { get; set; }
    [Range(1, 5)] public int? Stars { get; set; }
    [Range(typeof(DateTime), "2020-01-01", "2020-12-31")] public DateTime Arrival { get; set; }
    [StringLength(20, MinimumLength = 3)] public string Name { get; set; } = "";
    [EmailAddress] public string Email { get; set; } = "";
    [Required] public string Reference { get; set; } = "";
    [Required] public string? Title { get; set; }
    [RegularExpression("^[A-Z]{3}-[0-9]{4}$")] public string Code { get; set; } = "";
    [AllowedValues("S", "M", "L")] public string Size { get; set; } = "";
    [AllowedValues(1, 2, 3)] public int Level { get; set; }
    [DeniedValues("root", "admin")] public string Username { get; set; } = "";
    [MinLength(2), MaxLength(4)] public List<string> Items { get; set; } = [];
    [MaxLength(3)] public string[] Tags { get; set; } = [];
    public string Notes { get; set; } = "";
}

/// <summary>Gives <see cref="Booking"/> a source-generated model, so the tests can run against both models.</summary>
[FakeFor<Booking>]
public static partial class ConstraintFakes;

/// <summary>Invalid values and values on the edge of the limits, derived from data annotations, in both models.</summary>
public sealed class ConstraintCaseTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Generator<Booking> Make(bool generated) =>
        (generated ? ConstraintFakes.Booking : new Generator<Booking>(new ReflectionModel<Booking>())).Seed(3).ReferenceDate(Reference);

    /// <summary>The members whose data annotations fail, found with the real validators.</summary>
    private static string[] Failing(Booking booking)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(booking, new ValidationContext(booking), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static readonly (string Member, Expression<Func<Booking, object?>> Select)[] Constrained =
    [
        ("Guests", b => b.Guests),
        ("Rating", b => b.Rating),
        ("Stars", b => b.Stars),
        ("Arrival", b => b.Arrival),
        ("Name", b => b.Name),
        ("Email", b => b.Email),
        ("Reference", b => b.Reference),
        ("Title", b => b.Title),
        ("Code", b => b.Code),
        ("Size", b => b.Size),
        ("Level", b => b.Level),
        ("Username", b => b.Username),
        ("Items", b => b.Items),
        ("Tags", b => b.Tags),
    ];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Generated_bookings_are_valid_to_begin_with(bool generated) =>
        Make(generated).Generate(50).ShouldAllBe(b => Failing(b).Length == 0);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_breaks_one_member_and_leaves_the_others_valid(bool generated)
    {
        foreach (var (member, select) in Constrained)
        {
            var bookings = Make(generated).Invalid(select).Generate(20);

            foreach (var booking in bookings)
                Failing(booking).ShouldBe([member], $"{member} should be the only invalid member");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_picks_among_the_violations_reproducibly(bool generated)
    {
        var guests = Make(generated).Invalid(b => b.Guests).Generate(60).Select(b => b.Guests).ToList();

        guests.Distinct().Order().ShouldBe([0, 11]); // one below the minimum, one above the maximum
        Make(generated).Invalid(b => b.Guests).Generate(60).Select(b => b.Guests).ShouldBe(guests);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Violations_sit_just_outside_the_limits(bool generated)
    {
        var cases = Make(generated).InvalidCases();

        Values(cases, "Guests", b => b.Guests).ShouldBe([0, 11]);
        Values(cases, "Rating", b => b.Rating).ShouldBe([-0.5, 6.0]);
        Values(cases, "Stars", b => b.Stars).ShouldBe([0, 6]);
        Values(cases, "Arrival", b => b.Arrival.Date).ShouldBe([new DateTime(2019, 12, 31), new DateTime(2021, 1, 1)]);
        Values(cases, "Name", b => b.Name.Length).ShouldBe([2, 21]);
        Values(cases, "Items", b => b.Items.Count).ShouldBe([1, 5]);
        Values(cases, "Tags", b => b.Tags.Length).ShouldBe([4]); // only a maximum
        Values(cases, "Level", b => b.Level).ShouldBe([4]);
        Values(cases, "Size", b => b.Size).Single().ShouldNotBeOneOf("S", "M", "L");
        Values(cases, "Username", b => b.Username).Single().ShouldBeOneOf("root", "admin");
        Values(cases, "Reference", b => b.Reference).ShouldBe([null, ""], ignoreOrder: true);
        Values(cases, "Title", b => b.Title).ShouldBe([null, ""], ignoreOrder: true);
        new EmailAddressAttribute().IsValid(Values(cases, "Email", b => b.Email).Single()).ShouldBeFalse();
        System.Text.RegularExpressions.Regex.IsMatch(Values(cases, "Code", b => b.Code).Single()!, "^[A-Z]{3}-[0-9]{4}$").ShouldBeFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void InvalidCases_lists_every_violation_of_every_constrained_member(bool generated)
    {
        var cases = Make(generated).InvalidCases();

        cases.GroupBy(c => c.Member).ToDictionary(g => g.Key, g => g.Count()).ShouldBe(new Dictionary<string, int>
        {
            ["Guests"] = 2, ["Rating"] = 2, ["Stars"] = 2, ["Arrival"] = 2, ["Name"] = 2, ["Email"] = 1, ["Reference"] = 2,
            ["Title"] = 2, ["Code"] = 1, ["Size"] = 1, ["Level"] = 1, ["Username"] = 1, ["Items"] = 2, ["Tags"] = 1,
        }, ignoreOrder: true);
        cases.ShouldAllBe(c => c.Description.Length > 0);
        foreach (var c in cases)
            Failing(c.Value).ShouldBe([c.Member], c.ToString()); // each case breaks exactly its member
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_case_is_the_valid_object_with_one_member_replaced(bool generated)
    {
        var valid = Parts(Make(generated).Generate());

        var invalid = Make(generated).InvalidCases();
        foreach (var c in invalid.Concat(Make(generated).BoundaryCases()))
        {
            var parts = Parts(c.Value);
            if (invalid.Contains(c))
                parts[c.Member].ShouldNotBe(valid[c.Member], c.ToString()); // an edge may coincide with the valid value, a violation never does

            // Violating a value draws what the valid one draws, so later members stay the same; a collection changes
            // how many elements are drawn, so only the members declared before it can be compared.
            var order = valid.Keys.ToList();
            var compared = c.Member is "Items" or "Tags" ? order.Take(order.IndexOf(c.Member)) : order;
            foreach (var member in compared.Where(m => m != c.Member))
                parts[member].ShouldBe(valid[member], $"{member} in {c}");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void BoundaryCases_hit_the_limits_and_stay_valid(bool generated)
    {
        var cases = Make(generated).BoundaryCases();

        cases.GroupBy(c => c.Member).ToDictionary(g => g.Key, g => g.Count()).ShouldBe(new Dictionary<string, int>
        {
            ["Guests"] = 2, ["Rating"] = 2, ["Stars"] = 2, ["Arrival"] = 2, ["Name"] = 2, ["Reference"] = 1, ["Title"] = 1,
            ["Items"] = 2, ["Tags"] = 2,
        }, ignoreOrder: true);
        foreach (var c in cases)
            Failing(c.Value).ShouldBeEmpty(c.ToString());

        Values(cases, "Guests", b => b.Guests).ShouldBe([1, 10]);
        Values(cases, "Rating", b => b.Rating).ShouldBe([0.5, 5.0]);
        Values(cases, "Stars", b => b.Stars).ShouldBe([1, 5]);
        Values(cases, "Arrival", b => b.Arrival.Date).ShouldBe([new DateTime(2020, 1, 1), new DateTime(2020, 12, 31)]);
        Values(cases, "Name", b => b.Name.Length).ShouldBe([3, 20]);
        Values(cases, "Items", b => b.Items.Count).ShouldBe([2, 4]);
        Values(cases, "Tags", b => b.Tags.Length).ShouldBe([0, 3]);
        Values(cases, "Reference", b => b.Reference.Length).ShouldBe([1]); // [Required] means at least one character
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Boundary_picks_an_edge_and_leaves_the_others_valid(bool generated)
    {
        var bookings = Make(generated).Boundary(b => b.Guests).Generate(40);

        bookings.Select(b => b.Guests).Distinct().Order().ShouldBe([1, 10]);
        bookings.ShouldAllBe(b => Failing(b).Length == 0);
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_cases()
    {
        static IEnumerable<string> Describe(IReadOnlyList<ConstraintCase<Booking>> cases) =>
            cases.Select(c => $"{c.Member}|{c.Description}|{DescribeValue(c.Value)}");

        Describe(Make(true).InvalidCases()).ShouldBe(Describe(Make(false).InvalidCases()));
        Describe(Make(true).BoundaryCases()).ShouldBe(Describe(Make(false).BoundaryCases()));
        Make(true).Invalid(b => b.Items).Generate(20).Select(DescribeValue)
            .ShouldBe(Make(false).Invalid(b => b.Items).Generate(20).Select(DescribeValue));
    }

    [Test]
    public void Members_without_data_annotations_cannot_be_violated()
    {
        var exception = Should.Throw<InvalidOperationException>(() => Make(false).Invalid(b => b.Notes).Generate());
        exception.Message.ShouldContain("Booking.Notes");
        exception.Message.ShouldContain("data annotations");

        Should.Throw<InvalidOperationException>(() => Make(true).Boundary(b => b.Email).Generate())
            .Message.ShouldContain("Booking.Email");
    }

    [Test]
    public void Cases_skip_ignored_members_and_replace_rules_of_the_broken_member()
    {
        var cases = Make(false).Ignore(b => b.Guests).With(b => b.Name, "Fixed name").InvalidCases();

        cases.ShouldNotContain(c => c.Member == "Guests");
        cases.Where(c => c.Member == "Name").Select(c => c.Value.Name.Length).Order().ShouldBe([2, 21]);
        cases.Where(c => c.Member != "Name").ShouldAllBe(c => c.Value.Name == "Fixed name");
    }

    [Test]
    public void Violations_also_work_for_objects_created_by_a_factory()
    {
        var bookings = Make(false).CreateWith(_ => new Booking { Name = "Abc" }).Invalid(b => b.Name).Generate(20);

        bookings.Select(b => b.Name.Length).Distinct().Order().ShouldBe([2, 21]);
    }

    [Test]
    public void Invalid_and_Boundary_can_be_combined_with_other_rules()
    {
        var booking = Make(true).With(b => b.Email, "kept@example.com").Invalid(b => b.Guests).Boundary(b => b.Rating).Generate();

        booking.Email.ShouldBe("kept@example.com");
        booking.Guests.ShouldBeOneOf(0, 11);
        booking.Rating.ShouldBeOneOf(0.5, 5.0);
        Failing(booking).ShouldBe(["Guests"]);
    }

    private static List<TValue> Values<TValue>(IReadOnlyList<ConstraintCase<Booking>> cases, string member, Func<Booking, TValue> select) =>
        [.. cases.Where(c => c.Member == member).Select(c => select(c.Value))];

    private static Dictionary<string, string> Parts(Booking b) => new()
    {
        ["Guests"] = b.Guests.ToString(CultureInfo.InvariantCulture),
        ["Rating"] = b.Rating.ToString(CultureInfo.InvariantCulture),
        ["Stars"] = b.Stars?.ToString(CultureInfo.InvariantCulture) ?? "<null>",
        ["Arrival"] = b.Arrival.ToString("O", CultureInfo.InvariantCulture),
        ["Name"] = b.Name,
        ["Email"] = b.Email,
        ["Reference"] = b.Reference ?? "<null>",
        ["Title"] = b.Title ?? "<null>",
        ["Code"] = b.Code,
        ["Size"] = b.Size,
        ["Level"] = b.Level.ToString(CultureInfo.InvariantCulture),
        ["Username"] = b.Username,
        ["Items"] = string.Join(",", b.Items),
        ["Tags"] = string.Join(",", b.Tags),
        ["Notes"] = b.Notes,
    };

    private static string DescribeValue(Booking b) =>
        string.Join("|", b.Guests, b.Rating.ToString(CultureInfo.InvariantCulture), b.Stars, b.Arrival.ToString("O", CultureInfo.InvariantCulture), b.Name, b.Email,
            b.Reference ?? "<null>", b.Title ?? "<null>", b.Code, b.Size, b.Level, b.Username, string.Join(",", b.Items), string.Join(",", b.Tags), b.Notes);
}
