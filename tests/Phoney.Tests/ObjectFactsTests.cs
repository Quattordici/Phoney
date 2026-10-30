using Phoney.Generation;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>
/// What one object is told about itself (values rules give, values Populate keeps) and where each fact goes: the person to the identity, the creation time to the
/// timeline. Constants are taken when their part is first needed, other values when their member is produced; the first one counts.
/// </summary>
public sealed class ObjectFactsTests
{
    private static readonly DateTimeOffset Moment = new(2020, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static RuleSlot Constant(string name, Type type, ConventionKind convention, object value) =>
        new(new FakeMember(name, type, convention), new SlotDecision(RuleKind.Constant, value));

    [Test]
    public void A_kept_creation_time_goes_to_the_timeline_and_a_kept_name_to_the_identity()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();

        facts.Adopt(faker, new FakeMember("CreatedAt", typeof(DateTimeOffset), ConventionKind.PastDate), Moment);
        facts.Adopt(faker, new FakeMember("FirstName", typeof(string), ConventionKind.FirstName), "Ada");

        facts.GivenCreated(null).ShouldBe(Moment);
        facts.Identity(faker, null).FirstName.ShouldBe("Ada");
    }

    [Test]
    public void A_creation_time_a_rule_gives_is_the_objects_creation_time()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();

        facts.Give(faker, null, GivenValue.Created, Moment);

        facts.Created(faker, null, null).ShouldBe(Moment);
    }

    [Test]
    public void A_given_sex_replaces_the_drawn_one_and_is_drawn_where_its_member_is()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();

        facts.Give(faker, null, GivenValue.Sex, Sex.Female);

        var expected = new Faker(seed: 3);
        expected.Person.Sex();
        faker.Random.Int(0, 1_000_000).ShouldBe(expected.Random.Int(0, 1_000_000));
        facts.Identity(faker, null).Sex.ShouldBe(Sex.Female);
    }

    [Test]
    public void A_value_the_identity_cant_use_draws_nothing()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();

        facts.Give(faker, null, GivenValue.Sex, "female");

        faker.Random.Int(0, 1_000_000).ShouldBe(new Faker(seed: 3).Random.Int(0, 1_000_000));
    }

    [Test]
    public void Constants_are_taken_when_the_part_is_first_needed()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();
        RuleSlot?[] slots = [Constant("Age", typeof(int), ConventionKind.Age, 30), Constant("CreatedAt", typeof(DateTimeOffset), ConventionKind.PastDate, Moment)];

        facts.Identity(faker, slots).Age.ShouldBe(30);
        facts.GivenCreated(slots).ShouldBe(Moment);
    }

    [Test]
    public void A_value_given_before_the_part_is_needed_wins_over_a_constant()
    {
        var faker = new Faker(seed: 3);
        var facts = new ObjectFacts();
        RuleSlot?[] slots = [Constant("Age", typeof(int), ConventionKind.Age, 30)];

        facts.Give(faker, slots, GivenValue.Age, 40);

        facts.Identity(faker, slots).Age.ShouldBe(40);
    }
}
