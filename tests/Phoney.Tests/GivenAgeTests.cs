using Phoney.Generation;

namespace Phoney.Tests;

public sealed class AgedPatient
{
    public string FirstName { get; set; } = "";
    public int Age { get; set; }
    public DateOnly BirthDate { get; set; }
    public string Email { get; set; } = "";
}

/// <summary>Declares its birthdate before its age.</summary>
public sealed class AgedMember
{
    public DateOnly BirthDate { get; set; }
    public int Age { get; set; }
    public string City { get; set; } = "";
}

[FakeFor<AgedPatient>]
[FakeFor<AgedMember>]
public static partial class AgedFakes;

/// <summary>An age the object is given, by a rule or a kept value, is the age its birthdate is drawn for.</summary>
public sealed class GivenAgeTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Faker.DefaultSeededReferenceDate.UtcDateTime);

    private static ObjectGenerator<AgedPatient> AgedPatients(bool generated) =>
        (generated ? AgedFakes.AgedPatient : new ObjectGenerator<AgedPatient>(new ReflectionModel<AgedPatient>())).Seed(1);

    private static ObjectGenerator<AgedMember> AgedMembers(bool generated) =>
        (generated ? AgedFakes.AgedMember : new ObjectGenerator<AgedMember>(new ReflectionModel<AgedMember>())).Seed(1);

    private static int AgeOf(DateOnly birthDate) => Identity.AgeAt(birthDate, Today);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_constant_age_gives_a_matching_birthdate(bool generated)
    {
        AgedPatients(generated).With(p => p.Age, 30).Generate(200).ShouldAllBe(p => p.Age == 30 && AgeOf(p.BirthDate) == 30);
        AgedMembers(generated).With(m => m.Age, 30).Generate(200).ShouldAllBe(m => m.Age == 30 && AgeOf(m.BirthDate) == 30); // birthdate declared first
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void An_age_from_a_factory_declared_before_the_birthdate_gives_a_matching_birthdate(bool generated) =>
        AgedPatients(generated).With(p => p.Age, f => f.Random.Int(20, 25)).Generate(200).ShouldAllBe(p => p.Age >= 20 && p.Age <= 25 && AgeOf(p.BirthDate) == p.Age);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Populate_draws_the_birthdate_for_a_kept_age(bool generated) =>
        AgedMembers(generated).Populate(Enumerable.Range(0, 200).Select(_ => new AgedMember { Age = 42 })).ShouldAllBe(m => m.Age == 42 && AgeOf(m.BirthDate) == 42);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void The_other_members_draw_what_they_drew_before(bool generated)
    {
        var plain = AgedPatients(generated).Generate(50).Select(p => $"{p.FirstName}|{p.Email}");
        var ruled = AgedPatients(generated).With(p => p.Age, 30).Generate(50).Select(p => $"{p.FirstName}|{p.Email}");

        ruled.ShouldBe(plain);
    }

    [Test]
    public void Generated_and_reflection_models_agree() =>
        AgedPatients(generated: false).With(p => p.Age, 30).Generate(30).Select(p => p.BirthDate)
            .ShouldBe(AgedPatients(generated: true).With(p => p.Age, 30).Generate(30).Select(p => p.BirthDate));
}
