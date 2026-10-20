using Phoney.Generation;

namespace Phoney.Tests;

public sealed class FakeMemberTests
{
    [Test]
    public void A_member_is_settable_unless_it_is_a_constructor_parameter()
    {
        new FakeMember("Name", typeof(string), ConventionKind.FullName).IsSettable.ShouldBeTrue();
        new FakeMember("Name", typeof(string), ConventionKind.FullName) { IsConstructorParameter = true }.IsSettable.ShouldBeFalse();
    }

    [Test]
    public void Settable_can_be_given_whatever_the_member_is()
    {
        new FakeMember("Name", typeof(string), ConventionKind.FullName) { IsConstructorParameter = true, IsSettable = true }.IsSettable.ShouldBeTrue();
        new FakeMember("Tags", typeof(List<string>), ConventionKind.None) { IsSettable = false, FillsExisting = true }.IsSettable.ShouldBeFalse();
    }

    [Test]
    public void The_other_values_are_set_with_the_initializer()
    {
        var constraints = new MemberConstraints { MaxLength = 5 };

        var member = new FakeMember("Code", typeof(string), ConventionKind.None) { IsInitOnly = true, Constraints = constraints, IsNested = false };

        member.IsInitOnly.ShouldBeTrue();
        member.Constraints.ShouldBeSameAs(constraints);
        member.IsConstructorParameter.ShouldBeFalse();
        member.FillsExisting.ShouldBeFalse();
    }

#pragma warning disable CS0618 // the constructor with every value is kept for models compiled against earlier versions
    [Test]
    public void The_constructor_with_every_value_still_describes_the_same_member()
    {
        var member = new FakeMember("Id", typeof(int), ConventionKind.Id, isConstructorParameter: true, isInitOnly: false, constraints: null, isSettable: true, isNested: false, fillsExisting: false);

        member.IsConstructorParameter.ShouldBeTrue();
        member.IsSettable.ShouldBeTrue();
        member.Convention.ShouldBe(ConventionKind.Id);
    }
#pragma warning restore CS0618
}
