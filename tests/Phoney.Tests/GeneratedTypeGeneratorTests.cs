using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Type-wide generators (<c>WithGeneratorFor</c>) through source-generated models, and their parity with the reflection model.</summary>
public sealed class GeneratedTypeGeneratorTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static ObjectGenerator<Member> Reflected() => new(new ReflectionModel<Member>());

    private static ObjectGenerator<Gallery> ReflectedGallery() => new(new ReflectionModel<Gallery>());

    [Test]
    public void Generated_models_use_the_type_generator_for_members_collections_and_records()
    {
        var members = TestFakes.Member
            .Seed(1)
            .WithGeneratorFor(Fake.ForGenerated<MemberAddress>().With(a => a.City, "Oslo"))
            .WithGeneratorFor(Fake.ForGenerated<Membership>().With(m => m.CompanyName, "Acme"))
            .Generate(10);

        members.ShouldAllBe(m => m.Address.City == "Oslo");
        members.SelectMany(m => m.Memberships).ShouldNotBeEmpty();
        members.SelectMany(m => m.Memberships).ShouldAllBe(m => m.CompanyName == "Acme");
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data_with_type_generators()
    {
        ObjectGenerator<Member> Configure(ObjectGenerator<Member> g, ObjectGenerator<MemberAddress> address, ObjectGenerator<Membership> membership) =>
            g.Seed(7).ReferenceDate(Reference)
                .WithGeneratorFor(address.With(a => a.City, f => "City " + f.Random.Int(1, 99)))
                .WithGeneratorFor(membership.With(m => m.CompanyName, f => f.Company.Name()));

        var generated = Configure(TestFakes.Member, Fake.ForGenerated<MemberAddress>(), Fake.ForGenerated<Membership>()).Generate(25);
        var reflected = Configure(
                Reflected(),
                new ObjectGenerator<MemberAddress>(new ReflectionModel<MemberAddress>()),
                new ObjectGenerator<Membership>(new ReflectionModel<Membership>()))
            .Generate(25);

        generated.ShouldAllBe(m => m.Address.City.StartsWith("City "));
        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data_for_interfaces_and_subtypes()
    {
        ObjectGenerator<Gallery> Configure(ObjectGenerator<Gallery> g, ObjectGenerator<Painting> painting) =>
            g.Seed(9).ReferenceDate(Reference)
                .Subtype<IExhibit, Sculpture>() // loses against the type generator below
                .WithGeneratorFor<IExhibit, Painting>(painting.With(p => p.Price, 12.5m));

        var generated = Configure(GalleryFakes.Gallery, Fake.ForGenerated<Painting>()).Generate(20);
        var reflected = Configure(ReflectedGallery(), new ObjectGenerator<Painting>(new ReflectionModel<Painting>())).Generate(20);

        generated.ShouldAllBe(g => g.Highlight is Painting);
        generated.SelectMany(g => g.Exhibits).ShouldAllBe(e => e is Painting);
        generated.Select(g => ((Painting)g.Highlight).Price).ShouldAllBe(p => p == 12.5m);
        reflected.Select(Describe).ShouldBe(generated.Select(Describe));
    }

    [Test]
    public void Generated_and_reflection_models_populate_missing_nested_objects_with_type_generators_identically()
    {
        Member Existing() => new() { Username = "kept", FirstName = "Grace" };

        var generated = TestFakes.Member.Seed(3).ReferenceDate(Reference).WithGeneratorFor(Fake.ForGenerated<MemberAddress>().With(a => a.City, "Oslo")).Populate(Existing());
        var reflected = Reflected().Seed(3).ReferenceDate(Reference).WithGeneratorFor(new ObjectGenerator<MemberAddress>(new ReflectionModel<MemberAddress>()).With(a => a.City, "Oslo")).Populate(Existing());

        generated.Address.City.ShouldBe("Oslo");
        generated.Username.ShouldBe("kept");
        Describe(reflected).ShouldBe(Describe(generated));
    }

    private static string Describe(Member m) =>
        $"{m.Id}|{m.FirstName}|{m.Email}|{m.Username}|{m.Address.Street}|{m.Address.City}|{m.Address.ZipCode}|" +
        $"{string.Join(",", m.Memberships.Select(x => x.CompanyName + x.StartDate.ToString("O")))}|{string.Join(",", m.Scores.Select(x => x.Key + "=" + x.Value))}";

    private static string Describe(Gallery g) =>
        $"{g.Name}|{Exhibit(g.Highlight)}|{string.Join(",", g.Exhibits.Select(Exhibit))}|{g.Host?.GetType().Name}";

    private static string Exhibit(IExhibit? e) => e switch
    {
        Painting p => $"P:{p.Title}:{p.Price}",
        Sculpture s => $"S:{s.Material}:{s.Weight}",
        null => "null",
        _ => "?",
    };
}
