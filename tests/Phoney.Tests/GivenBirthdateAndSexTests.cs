namespace Phoney.Tests;

/// <summary>A birthdate, sex or gender a rule gives a member is the person's: the age, names and email of the same object agree with it.</summary>
public sealed class GivenBirthdateAndSexTests
{
    private static readonly DateOnly Born = new(1990, 5, 6);

    [Test]
    public void A_birthdate_rule_decides_the_age()
    {
        foreach (var customer in Fake.For<Customer>().Seed(7).With(x => x.DateOfBirth, Born).Generate(20))
        {
            customer.DateOfBirth.ShouldBe(Born);
            customer.Age.ShouldBe(AgeAtReference(Born));
        }
    }

    [Test]
    public void A_birthdate_from_a_factory_decides_the_age_too()
    {
        foreach (var customer in Fake.For<Customer>().Seed(7).With(x => x.DateOfBirth, _ => Born).Generate(20))
            customer.Age.ShouldBe(AgeAtReference(Born));
    }

    [Test]
    public void A_gender_rule_decides_the_names()
    {
        var female = Fake.For<Customer>().Seed(7).With(x => x.Gender, Gender.Female).Generate(50);
        var male = Fake.For<Customer>().Seed(7).With(x => x.Gender, Gender.Male).Generate(50);

        female.ShouldAllBe(c => c.Gender == Gender.Female);
        male.ShouldAllBe(c => c.Gender == Gender.Male);
        female.Zip(male).Count(pair => pair.First.FirstName != pair.Second.FirstName).ShouldBe(42); // the same draws pick from the other list; 8 names are on both
    }

    [Test]
    public void A_gender_rule_does_not_move_what_is_drawn_after_it()
    {
        var plain = Fake.For<Customer>().Seed(7).Generate(20);
        var ruled = Fake.For<Customer>().Seed(7).With(x => x.Gender, Gender.Female).Generate(20);

        ruled.Select(c => c.Phone).ShouldBe(plain.Select(c => c.Phone));
        ruled.Select(c => c.Tags.Count).ShouldBe(plain.Select(c => c.Tags.Count));
    }

    [Test]
    public void A_birthdate_rule_does_not_move_what_is_drawn_after_it()
    {
        var plain = Fake.For<Customer>().Seed(7).Generate(20);
        var ruled = Fake.For<Customer>().Seed(7).With(x => x.DateOfBirth, Born).Generate(20);

        ruled.Select(c => c.Phone).ShouldBe(plain.Select(c => c.Phone));
        ruled.Select(c => c.FirstName).ShouldBe(plain.Select(c => c.FirstName));
    }

    // A seeded generator's dates are relative to the default seeded reference date.
    private static int AgeAtReference(DateOnly born) =>
        Phoney.Generation.Identity.AgeAt(born, DateOnly.FromDateTime(Faker.DefaultSeededReferenceDate.UtcDateTime));
}
