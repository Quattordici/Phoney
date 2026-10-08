using Phony.Generation;

namespace Phony.Tests;

// Separate types from GenerationTests so those keep exercising the reflection model.
public sealed class Member
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public Gender Gender { get; set; }
    public DateOnly BirthDate { get; set; }
    public decimal? Balance { get; set; }
    public string? Nickname { get; set; }
    public required string Username { get; init; }
    public MemberAddress Address { get; init; } = null!;
    public List<Membership> Memberships { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public byte[] Avatar { get; set; } = [];
}

public sealed record MemberAddress(string Street, string City, string ZipCode);

public sealed record Membership(string CompanyName, DateTime StartDate)
{
    public Member? Owner { get; init; }
}

public struct Coordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; init; }
}

[FakeFor<Member>]
[FakeFor<Coordinates>]
public static partial class TestFakes;

public sealed class SourceGeneratedTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generated_generators_populate_objects()
    {
        var member = TestFakes.Member.Seed(1).ReferenceDate(Reference).Generate();

        member.Id.ShouldBe(1);
        member.Email.ShouldContain("@");
        member.Username.ShouldNotBeNullOrWhiteSpace();
        member.Address.City.ShouldNotBeNullOrWhiteSpace();
        member.Memberships.ShouldNotBeEmpty();
        member.Memberships.ShouldAllBe(m => m.Owner == null); // cycle back to Member stops
        member.Scores.ShouldNotBeEmpty();
        member.Avatar.Length.ShouldBe(16);
    }

    [Fact]
    public void Generated_models_are_registered_for_Fake_For_and_nested_types()
    {
        ModelRegistry.IsRegistered<Member>().ShouldBeTrue();
        ModelRegistry.IsRegistered<MemberAddress>().ShouldBeTrue();
        ModelRegistry.IsRegistered<Membership>().ShouldBeTrue();
        ModelRegistry.For<Member>().GetType().Name.ShouldStartWith("__PhonyModel_");
    }

    [Fact]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        var generated = TestFakes.Member.Seed(7).ReferenceDate(Reference).Generate(25);
        var reflected = new Generator<Member>(new ReflectionModel<Member>()).Seed(7).ReferenceDate(Reference).Generate(25);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));

        static string Describe(Member m) =>
            $"{m.Id}|{m.FirstName}|{m.LastName}|{m.Email}|{m.Gender}|{m.BirthDate}|{m.Balance}|{m.Nickname}|{m.Username}|" +
            $"{m.Address.Street}|{m.Address.City}|{string.Join(",", m.Memberships.Select(x => x.CompanyName + x.StartDate.ToString("O")))}|" +
            $"{string.Join(",", m.Scores.Select(x => x.Key + "=" + x.Value))}|{string.Join(",", m.Tags)}|{Convert.ToHexString(m.Avatar)}";
    }

    [Fact]
    public void Rules_work_with_generated_models_including_init_only_dependents()
    {
        var members = TestFakes.Member
            .With(x => x.FirstName, "Grace")
            .With(x => x.Username, (f, x) => x.FirstName.ToLowerInvariant() + "_" + x.Id)
            .Ignore(x => x.Memberships)
            .Generate(3);

        members.ShouldAllBe(m => m.FirstName == "Grace");
        members.Select(m => m.Username).ShouldBe(["grace_1", "grace_2", "grace_3"]);
        members.ShouldAllBe(m => m.Memberships.Count == 0);
    }

    [Fact]
    public void Structs_with_init_members_are_generated()
    {
        var c = TestFakes.Coordinates.Generate();
        c.Latitude.ShouldBeInRange(-90, 90);
        c.Longitude.ShouldBeInRange(-180, 180);
    }
}
