using Phoney.Generation;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>What an object's identity is told before its person is drawn, and when the person is drawn.</summary>
public sealed class IdentityTests
{
    private static readonly FakeMember Age = new("Age", typeof(int), ConventionKind.Age);
    private static readonly FakeMember FirstName = new("FirstName", typeof(string), ConventionKind.FirstName);
    private static readonly FakeMember Gender = new("Gender", typeof(Sex), ConventionKind.Gender);

    [Test]
    public void The_person_is_drawn_when_the_identity_is_settled_and_not_before()
    {
        var faker = new Faker(seed: 9);
        var identity = new Identity(faker);

        identity.Give(GivenValue.Age, 30);
        faker.Random.Int(0, 1_000_000).ShouldBe(new Faker(seed: 9).Random.Int(0, 1_000_000));

        var settled = new Faker(seed: 9);
        new Identity(settled).Settle().Sex.ShouldBe(new Faker(seed: 9).Person.Sex());
    }

    [Test]
    public void A_given_age_decides_the_birthdate_and_the_first_one_counts()
    {
        var faker = new Faker(seed: 9);
        var identity = new Identity(faker);
        identity.Give(GivenValue.Age, 30);
        identity.Give(GivenValue.Age, 50);

        Identity.AgeAt(identity.Settle().BirthDate, DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime)).ShouldBe(30);
    }

    [Test]
    public void An_age_that_cannot_be_used_is_left_to_the_rule()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Age, -1);
        identity.Give(GivenValue.Age, 100_000);
        identity.Give(GivenValue.Age, "thirty");

        var expected = new Faker(seed: 9);
        expected.Person.Sex();
        identity.Settle().BirthDate.ShouldBe(expected.Date.Birthdate());
    }

    [Test]
    public void A_kept_birthdate_wins_over_a_given_age()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Adopt(new FakeMember("BirthDate", typeof(DateOnly), ConventionKind.BirthDate), new DateOnly(1990, 5, 6));
        identity.Give(GivenValue.Age, 30);

        identity.Settle().BirthDate.ShouldBe(new DateOnly(1990, 5, 6));
    }

    [Test]
    public void A_kept_name_and_sex_shape_the_generated_person()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Adopt(FirstName, "Ada");
        var drawn = new Faker(seed: 9).Person.Sex();
        var kept = drawn == Sex.Female ? Sex.Male : Sex.Female;
        identity.Adopt(Gender, kept);

        var settled = identity.Settle();
        settled.FirstName.ShouldBe("Ada");
        settled.Sex.ShouldBe(kept);
        settled.Email.ShouldStartWith("Ada", Case.Insensitive);
    }

    [Test]
    public void A_kept_age_is_read_like_a_given_one()
    {
        var faker = new Faker(seed: 9);
        var identity = new Identity(faker);
        identity.Adopt(Age, 41);

        Identity.AgeAt(identity.Settle().BirthDate, DateOnly.FromDateTime(faker.ReferenceDate.UtcDateTime)).ShouldBe(41);
    }

    [Test]
    public void A_given_country_makes_the_country_code_match()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Country, "Sweden");

        identity.Settle().CountryCode().ShouldBe("SE");
    }

    [Test]
    public void Once_settled_only_the_first_given_country_counts()
    {
        var identity = new Identity(new Faker(seed: 9)).Settle();
        identity.Give(GivenValue.Country, "Sweden");
        identity.Give(GivenValue.CountryCode, "NO");

        identity.CountryCode().ShouldBe("SE");
    }

    [Test]
    public void A_given_birthdate_is_the_birthdate_but_its_draw_still_happens()
    {
        var faker = new Faker(seed: 9);
        var identity = new Identity(faker);
        identity.Give(GivenValue.BirthDate, new DateOnly(1990, 5, 6));

        identity.Settle().BirthDate.ShouldBe(new DateOnly(1990, 5, 6));

        var expected = new Faker(seed: 9);
        expected.Person.Sex();
        expected.Date.Birthdate();
        faker.Random.Int(0, 1_000_000).ShouldBe(expected.Random.Int(0, 1_000_000));
    }

    [Test]
    public void A_given_birthdate_wins_over_a_given_age_and_the_first_birthdate_counts()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Age, 30);
        identity.Give(GivenValue.BirthDate, new DateTime(1990, 5, 6, 12, 0, 0, DateTimeKind.Utc));
        identity.Give(GivenValue.BirthDate, new DateOnly(1970, 1, 1));

        identity.Settle().BirthDate.ShouldBe(new DateOnly(1990, 5, 6));
    }

    [Test]
    public void Something_that_is_not_a_date_is_not_a_birthdate()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.BirthDate, "1990-05-06");
        identity.Give(GivenValue.BirthDate, 42);

        var expected = new Faker(seed: 9);
        expected.Person.Sex();
        identity.Settle().BirthDate.ShouldBe(expected.Date.Birthdate());
    }

    [Test]
    public void A_given_sex_is_the_sex_but_its_draw_still_happens()
    {
        var drawn = new Faker(seed: 9).Person.Sex();
        var given = drawn == Sex.Female ? Sex.Male : Sex.Female;
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Sex, given);

        identity.Settle().Sex.ShouldBe(given);

        var expected = new Faker(seed: 9);
        expected.Person.Sex(); // still drawn, so the name after it is the one the drawn sex would have got
        identity.FirstName.ShouldBe(expected.Person.FirstName(given));
    }

    [Test]
    public void A_given_enum_with_Female_and_Male_members_gives_the_sex()
    {
        var drawn = new Faker(seed: 9).Person.Sex();
        var given = drawn == Sex.Female ? Tests.Gender.Male : Tests.Gender.Female;
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Sex, given);

        identity.Settle().Sex.ShouldBe(given == Tests.Gender.Female ? Sex.Female : Sex.Male);
    }

    [Test]
    public void A_given_value_that_names_no_sex_leaves_the_drawn_one()
    {
        var identity = new Identity(new Faker(seed: 9));
        identity.Give(GivenValue.Sex, "Female");
        identity.Give(GivenValue.Sex, StringComparison.Ordinal);

        identity.Settle().Sex.ShouldBe(new Faker(seed: 9).Person.Sex());
    }
}
