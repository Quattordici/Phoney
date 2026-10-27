using Phoney.Generation;
using Phoney.Modules;

namespace Phoney.Tests;

/// <summary>What an object's identity is told before its person is drawn, and when the person is drawn.</summary>
public sealed class IdentityTests
{
    private static readonly FakeMember Age = new("Age", typeof(int), ConventionKind.Age);
    private static readonly FakeMember FirstName = new("FirstName", typeof(string), ConventionKind.FirstName);
    private static readonly FakeMember Gender = new("Gender", typeof(string), ConventionKind.Gender);

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

        var age = Identity.AgeAt(identity.Settle().BirthDate, DateOnly.FromDateTime(new Faker(seed: 9).ReferenceDate.UtcDateTime));
        age.ShouldBeInRange(18, 80);
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
        identity.Adopt(Gender, "Female");

        var settled = identity.Settle();
        settled.FirstName.ShouldBe("Ada");
        settled.Sex.ShouldBe(Sex.Female);
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
}
