using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Annotations on a virtual or abstract member, which an override inherits (the validator checks them on the override too).</summary>
public abstract class AnnotatedBase
{
    [Range(1, 3)] public virtual int Score { get; set; }

    [StringLength(4, MinimumLength = 2)] public abstract string Code { get; set; }

    [Range(10, 20)] public virtual int Level { get; set; }
}

/// <summary>Overrides that add no annotations of their own, and one that replaces the inherited range with its own.</summary>
public sealed class AnnotatedOverride : AnnotatedBase
{
    public override int Score { get; set; }

    public override string Code { get; set; } = "";

    [Range(30, 31)] public override int Level { get; set; }
}

/// <summary>A member hidden with <c>new</c> doesn't inherit anything: the validator ignores the hidden member's annotations.</summary>
public sealed class HidingMember : AnnotatedBase
{
    [Range(50, 52)] public new int Score { get; set; }

    public override string Code { get; set; } = "";
}

[FakeFor<AnnotatedOverride>, FakeFor<HidingMember>]
public static partial class InheritedAnnotationFakes;

public sealed class InheritedAnnotationTests
{
    private static ObjectGenerator<T> Make<T>(bool generated) =>
        (generated ? Fake.ForGenerated<T>() : new ObjectGenerator<T>(new ReflectionModel<T>())).Seed(3);

    private static bool Valid(object value) => Validator.TryValidateObject(value, new ValidationContext(value), null, validateAllProperties: true);

    // MemberInfo.GetCustomAttributes(inherit: true) ignores inherit for properties, and the generator read only the override's own attributes, so both models
    // generated Score from 0 to 1000 and Code of any length, which the validator then rejected.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void An_override_keeps_the_annotations_of_the_member_it_overrides(bool generated)
    {
        var values = Make<AnnotatedOverride>(generated).Generate(200);

        values.ShouldAllBe(v => Valid(v));
        values.ShouldAllBe(v => v.Score >= 1 && v.Score <= 3 && v.Code.Length >= 2 && v.Code.Length <= 4);
        values.ShouldAllBe(v => v.Level >= 30 && v.Level <= 31);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_hidden_member_uses_only_its_own_annotations(bool generated)
    {
        var values = Make<HidingMember>(generated).Generate(100);

        values.ShouldAllBe(v => Valid(v));
        values.ShouldAllBe(v => v.Score >= 50 && v.Score <= 52);
    }

    [Test]
    public void Both_models_agree_on_inherited_annotations()
    {
        string Text(AnnotatedOverride v) => $"{v.Score} {v.Code} {v.Level}";

        Make<AnnotatedOverride>(generated: true).Generate(30).Select(Text).ShouldBe(Make<AnnotatedOverride>(generated: false).Generate(30).Select(Text));
    }
}
