using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The creation time of one object: drawn once, replaceable by a given one only before anything is built around it.</summary>
public sealed class TimelineTests
{
    private static readonly DateTimeOffset Given = new(2020, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static RuleSlot ConstantCreated(object value) =>
        new(new FakeMember("CreatedAt", typeof(DateTimeOffset), ConventionKind.PastDate), new SlotDecision(RuleKind.Constant, value));

    [Test]
    public void Draws_the_creation_time_once()
    {
        var faker = new Faker(seed: 5);
        var timeline = new Timeline();

        var first = timeline.Created(faker, null, null);
        var second = timeline.Created(faker, null, null);

        second.ShouldBe(first);
        first.ShouldBe(new Faker(seed: 5).Date.Past());
    }

    [Test]
    public void A_given_time_replaces_the_drawn_one_but_the_draw_still_happens()
    {
        var faker = new Faker(seed: 5);
        var timeline = new Timeline();
        timeline.Give(Given);

        timeline.Created(faker, null, null).ShouldBe(Given);

        var expected = new Faker(seed: 5);
        expected.Date.Past();
        faker.Random.Int(0, 1_000_000).ShouldBe(expected.Random.Int(0, 1_000_000));
    }

    [Test]
    public void The_first_given_time_counts()
    {
        var timeline = new Timeline();
        timeline.Give(Given);
        timeline.Give(Given.AddYears(1));

        timeline.Given(null).ShouldBe(Given);
    }

    [Test]
    public void A_time_given_after_the_creation_time_was_drawn_is_ignored()
    {
        var faker = new Faker(seed: 5);
        var timeline = new Timeline();
        var drawn = timeline.Created(faker, null, null);

        timeline.Give(Given);

        timeline.Created(faker, null, null).ShouldBe(drawn);
        timeline.Given(null).ShouldBeNull();
    }

    [Test]
    public void Reads_a_DateTime_as_UTC_unless_it_is_local_and_a_DateOnly_as_midnight_UTC()
    {
        var utc = new Timeline();
        utc.Give(new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Unspecified));
        utc.Given(null).ShouldBe(new DateTimeOffset(2020, 3, 4, 5, 6, 7, TimeSpan.Zero));

        var local = new Timeline();
        var moment = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Local);
        local.Give(moment);
        local.Given(null).ShouldBe(new DateTimeOffset(moment));

        var day = new Timeline();
        day.Give(new DateOnly(2020, 3, 4));
        day.Given(null).ShouldBe(new DateTimeOffset(2020, 3, 4, 0, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void Anything_that_is_not_a_date_is_ignored()
    {
        var timeline = new Timeline();
        timeline.Give("2020-03-04");
        timeline.Give(42);

        timeline.Given(null).ShouldBeNull();
    }

    [Test]
    public void A_constant_rule_for_the_creation_time_is_taken_before_anything_is_drawn()
    {
        var timeline = new Timeline();
        var slots = new RuleSlot?[] { null, ConstantCreated(Given) };

        timeline.Given(slots).ShouldBe(Given);
        timeline.Created(new Faker(seed: 5), null, slots).ShouldBe(Given);
    }
}
