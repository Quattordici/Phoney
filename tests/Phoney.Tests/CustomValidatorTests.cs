using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>A validation rule of a code base's own: even numbers only.</summary>
public sealed class EvenAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not int number || number % 2 == 0;
}

/// <summary>A validation rule with an argument: text that does not contain a word.</summary>
public sealed class WithoutAttribute(string word) : ValidationAttribute
{
    public override bool IsValid(object? value) => value is not string text || !text.Contains(word, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A rule no value passes.</summary>
public sealed class NeverAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => false;
}

/// <summary>A rule that needs the object, which Phoney can't check for one member on its own.</summary>
public sealed class NeedsContextAttribute : ValidationAttribute
{
    public override bool RequiresValidationContext => true;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) => ValidationResult.Success;
}

public sealed class Concert
{
    [Even, Range(1, 20)] public int Seats { get; set; }
    [Without("a"), StringLength(12)] public string Code { get; set; } = "";
    [NeedsContext] public string Note { get; set; } = "";
    public string Password { get; set; } = "";
    [Compare(nameof(Password)), StringLength(40)] public string ConfirmPassword { get; set; } = "";
}

public sealed class Impossible
{
    [Never] public int Value { get; set; }
}

/// <summary>Two members whose values can only be the same.</summary>
public sealed class Pinned
{
    [AllowedValues("same")] public string Word { get; set; } = "";
    [Compare(nameof(Word)), AllowedValues("same")] public string Again { get; set; } = "";
}

[FakeFor<Concert>]
[FakeFor<Pinned>]
[FakeFor<Impossible>]
public static partial class CustomValidatorFakes;

public sealed class CustomValidatorTests
{
    private static ObjectGenerator<T> Make<T>(bool generated, ObjectGenerator<T> fromGenerator) =>
        (generated ? fromGenerator : new ObjectGenerator<T>(new ReflectionModel<T>())).Seed(3);

    private static ObjectGenerator<Concert> Concerts(bool generated) => Make(generated, CustomValidatorFakes.Concert);

    private static string[] Failing(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Values_are_drawn_again_until_a_custom_validation_attribute_accepts_them(bool generated)
    {
        var concerts = Concerts(generated).Generate(200);

        concerts.SelectMany(Failing).Distinct().ShouldBeEmpty();
        concerts.ShouldAllBe(t => t.Seats % 2 == 0 && !t.Code.Contains('a', StringComparison.OrdinalIgnoreCase));
        concerts.Select(t => t.Seats).Distinct().Count().ShouldBe(10);
    }

    [Test]
    public void Generated_and_reflection_models_agree() =>
        Concerts(true).Generate(50).Select(Describe).ShouldBe(Concerts(false).Generate(50).Select(Describe));

    private static string Describe(Concert t) => $"{t.Seats} {t.Code} {t.Note} {t.Password} {t.ConfirmPassword}";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_rule_wins_over_a_custom_validation_attribute(bool generated) =>
        Concerts(generated).With(x => x.Seats, 7).Generate().Seats.ShouldBe(7);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_custom_validation_attribute_nothing_passes_says_how_to_fix_it(bool generated) =>
        Should.Throw<InvalidOperationException>(() => Make(generated, CustomValidatorFakes.Impossible).Generate())
            .Message.ShouldContain("[Never]");

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_gives_a_compared_member_a_value_that_differs(bool generated)
    {
        var concerts = Concerts(generated).Invalid(x => x.ConfirmPassword).Generate(30);

        concerts.ShouldAllBe(t => Failing(t).SequenceEqual(new[] { nameof(Concert.ConfirmPassword) }));
        concerts.ShouldContain(t => t.ConfirmPassword != t.Password && t.ConfirmPassword.Length <= 40); // the other case is the length
        var pinned = Make(generated, CustomValidatorFakes.Pinned).InvalidCases().Single(c => c.Description == "differs from Word").Value; // its own value would be the same
        (pinned.Word, pinned.Again).ShouldBe(("same", "samf"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_cases_include_the_comparison(bool generated)
    {
        var cases = Concerts(generated).InvalidCases();

        cases.ShouldContain(c => c.Member == nameof(Concert.ConfirmPassword) && c.Description.Contains("differs from Password"));
        foreach (var c in cases)
            Failing(c.Value).ShouldNotBeEmpty(c.ToString());
    }

    [Test]
    public void Breaking_a_comparison_leaves_the_other_members_as_in_a_valid_object()
    {
        var valid = Concerts(true).Generate(20);
        var broken = Concerts(true).Invalid(x => x.ConfirmPassword).Generate(20);

        broken.Select(t => (t.Seats, t.Code, t.Password)).ShouldBe(valid.Select(t => (t.Seats, t.Code, t.Password)));
    }
}
