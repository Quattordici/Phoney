using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

// Types only these tests use.
public sealed class Lodging : IValidatableObject
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Guest { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (End <= Start)
            yield return new ValidationResult("End must be after Start.", [nameof(End)]);
    }
}

public sealed class Journey
{
    public string Name { get; set; } = "";
    public Lodging Lodging { get; set; } = null!;
    public List<Lodging> Legs { get; set; } = [];
}

public sealed class Pass : IValidatableObject
{
    public string Seat { get; set; } = "";
    public int Row { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
}

public sealed class NeverValid : IValidatableObject
{
    [StringLength(5)] public string Code { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [new ValidationResult("Never valid.")];
}

[FakeFor<Lodging>]
[FakeFor<Journey>]
[FakeFor<Pass>]
public static partial class LodgingFakes;

/// <summary>
/// An object that implements <see cref="IValidatableObject"/> passes its own <c>Validate</c>: one that doesn't is generated again like an object a
/// <c>Where</c> condition rejects, at the root and nested, through both models; an object that passes is the one it would be without the check.
/// </summary>
public sealed class ObjectValidationTests
{
    private static ObjectGenerator<T> Make<T>(bool generated, ObjectGenerator<T> generatedOne) =>
        (generated ? generatedOne : new ObjectGenerator<T>(new ReflectionModel<T>())).Seed(1);

    private static bool Passes(object item) => Validator.TryValidateObject(item, new ValidationContext(item), null, validateAllProperties: true);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Every_object_passes_its_own_Validate(bool generated) =>
        Make(generated, LodgingFakes.Lodging).Generate(1000).ShouldAllBe(b => Passes(b));

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Nested_objects_and_collection_elements_pass_too(bool generated) =>
        Make(generated, LodgingFakes.Journey).Generate(300).ShouldAllBe(t => Passes(t.Lodging) && t.Legs.All(Passes));

    [Test]
    public void Without_the_check_about_half_fail() =>
        LodgingFakes.Lodging.Seed(1).ValidateObjects(false).Generate(1000).Count(b => !Passes(b)).ShouldBeInRange(350, 650);

    [Test]
    public void An_object_that_passes_is_the_one_it_would_be_without_the_check()
    {
        var checkedOnes = LodgingFakes.Lodging.Seed(1).Generate(200);
        var plain = LodgingFakes.Lodging.Seed(1).ValidateObjects(false).Generate(200);

        for (var i = 0; i < plain.Count; i++)
        {
            if (Passes(plain[i]))
                (checkedOnes[i].Start, checkedOnes[i].End, checkedOnes[i].Guest).ShouldBe((plain[i].Start, plain[i].End, plain[i].Guest));
        }

        LodgingFakes.Pass.Seed(1).Generate(50).Select(t => (t.Seat, t.Row)).ShouldBe(LodgingFakes.Pass.Seed(1).ValidateObjects(false).Generate(50).Select(t => (t.Seat, t.Row)));
    }

    [Test]
    public void Generated_and_reflection_models_agree() =>
        Make(false, LodgingFakes.Journey).Generate(100).Select(Describe).ShouldBe(Make(true, LodgingFakes.Journey).Generate(100).Select(Describe));

    [Test]
    public void An_object_that_never_passes_says_why_and_how_to_fix_it()
    {
        var error = Should.Throw<InvalidOperationException>(() => Fake.For<NeverValid>().Seed(1).Generate());

        error.Message.ShouldContain("NeverValid");
        error.Message.ShouldContain("its own Validate");
        error.Message.ShouldContain("Never valid.");
        error.Message.ShouldContain("ValidateObjects(false)");
    }

    [Test]
    public void Invalid_cases_are_not_checked() =>
        Fake.For<NeverValid>().Seed(1).InvalidCases().ShouldNotBeEmpty();

    [Test]
    public void Populate_checks_with_PopulateMode_All_and_keeps_what_MissingOnly_keeps()
    {
        LodgingFakes.Lodging.Seed(1).Populate(new Lodging(), PopulateMode.All).ShouldSatisfyAllConditions(b => Passes(b).ShouldBeTrue());
        var kept = new Lodging { Start = new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc), End = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        LodgingFakes.Lodging.Seed(1).Populate(kept).End.ShouldBe(kept.End); // kept values can't be made again, so nothing is retried
    }

    private static string Describe(Journey t) =>
        $"{t.Name}|{t.Lodging.Start:O}|{t.Lodging.End:O}|{t.Lodging.Guest}|{string.Join(",", t.Legs.Select(l => $"{l.Start:O}-{l.End:O}"))}";
}
