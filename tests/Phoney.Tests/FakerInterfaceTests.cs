using System.Reflection;

namespace Phoney.Tests;

public sealed class FakerInterfaceTests
{
    [Test]
    public void IFaker_exposes_every_public_member_of_Faker()
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var expected = typeof(Faker).GetMembers(Public)
            .Where(m => m is PropertyInfo || m is MethodInfo { IsSpecialName: false })
            .Select(m => m.Name)
            .Distinct()
            .Order();
        var actual = typeof(IFaker).GetMembers()
            .Where(m => m is PropertyInfo || m is MethodInfo { IsSpecialName: false })
            .Select(m => m.Name)
            .Distinct()
            .Order();

        actual.ShouldBe(expected);
    }

    [Test]
    public void A_faker_can_be_used_through_the_interface()
    {
        IFaker faker = new Faker("sv", seed: 1);

        faker.Person.FirstName().ShouldNotBeNullOrWhiteSpace();
        faker.Locale.ShouldBe("sv");
        faker.Seed(2).ShouldBeSameAs(faker);
        faker.Parse("{{person.lastName}}").ShouldNotContain("{{");
    }
}
