using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public enum Shade
{
    Red,
    Green,
    Blue,
}

/// <summary>Data annotations that used to be ignored or broken by the way values were fitted to them; every member must pass validation.</summary>
public sealed class Annotated
{
    [MaxLength(5), StringLength(50)] public string Code { get; set; } = "";
    [StringLength(50), MaxLength(5)] public string Tag { get; set; } = "";
    [StringLength(50, MinimumLength = 2), MinLength(8)] public string Note { get; set; } = "";
    [EmailAddress, MaxLength(12)] public string Email { get; set; } = "";
    [EmailAddress, StringLength(40, MinimumLength = 35)] public string LongEmail { get; set; } = "";
    [MaxLength(4)] public byte[] Short { get; set; } = [];
    [MinLength(20)] public byte[] Long { get; set; } = [];
    [Range(typeof(TimeSpan), "00:00:01", "00:00:02")] public TimeSpan Wait { get; set; }
    [Range(typeof(TimeOnly), "10:00", "11:00")] public TimeOnly Opens { get; set; }
    [Range(1, 3)] public string Level { get; set; } = "";
    [Range('a', 'c')] public char Grade { get; set; }
    [DeniedValues(Shade.Red)] public Shade NotRed { get; set; }
    [RegularExpression("^(?<area>\\d{3})-\\d{4}$")] public string Phone { get; set; } = "";
    [RegularExpression("^(?=.*\\d)[a-z\\d]{8}$")] public string Password { get; set; } = "";
    [RegularExpression("^(?!admin)[a-z]{5}$")] public string Login { get; set; } = "";
    [RegularExpression("^\\p{Lu}\\p{Ll}{3}[\\p{N}]$")] public string Word { get; set; } = "";
    [RegularExpression("^[a-z]{2}\\B[a-z ]{2}$")] public string Joined { get; set; } = "";
}

/// <summary>An exclusive <see cref="TimeSpan"/> range.</summary>
public sealed class OpenDuration
{
    [Range(typeof(TimeSpan), "00:00:00", "00:00:00.002", MinimumIsExclusive = true, MaximumIsExclusive = true)] public TimeSpan Wait { get; set; }
}

/// <summary>Allowed values written as the names of an enum's values (the validator compares them with <see cref="object.Equals(object, object)"/>, so it rejects every value, but the intent is clear).</summary>
public sealed class NamedShade
{
    [AllowedValues("Green", "Blue")] public Shade Shade { get; set; }
}

/// <summary>Annotations no value can satisfy.</summary>
public sealed class EveryShadeDenied
{
    [DeniedValues(Shade.Red, Shade.Green, Shade.Blue)] public Shade Shade { get; set; }
}

/// <inheritdoc cref="EveryShadeDenied"/>
public sealed class UnknownShade
{
    [AllowedValues("Purple")] public Shade Shade { get; set; }
}

/// <inheritdoc cref="EveryShadeDenied"/>
public sealed class BackReference
{
    [RegularExpression("^(a)\\1$")] public string Twice { get; set; } = "";
}

[FakeFor<Annotated>]
[FakeFor<OpenDuration>]
[FakeFor<NamedShade>]
[FakeFor<EveryShadeDenied>]
[FakeFor<UnknownShade>]
[FakeFor<BackReference>]
public static partial class AnnotationFakes;

public sealed class AnnotationEdgeTests
{
    private static ObjectGenerator<Annotated> Make(bool generated) =>
        (generated ? AnnotationFakes.Annotated : new ObjectGenerator<Annotated>(new ReflectionModel<Annotated>())).Seed(11);

    private static string[] Failing(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Every_generated_value_passes_its_annotations(bool generated)
    {
        var values = Make(generated).Generate(500);

        values.SelectMany(Failing).Distinct().ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Format_annotations_hold_without_name_conventions_because_they_are_validation_rules(bool generated)
    {
        var values = Make(generated).WithNameConventions(false).Generate(200);

        values.SelectMany(Failing).Distinct().ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Length_limits_from_several_annotations_take_the_strictest(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.ShouldAllBe(v => v.Code.Length <= 5 && v.Tag.Length <= 5);
        values.ShouldAllBe(v => v.Note.Length >= 8);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_shortened_email_is_still_an_email(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.ShouldAllBe(v => v.Email.Length <= 12 && v.Email.IndexOf('@') > 0 && v.Email.IndexOf('@') < v.Email.Length - 1);
        values.ShouldAllBe(v => v.LongEmail.Length >= 35 && v.LongEmail.IndexOf('@') > 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Byte_arrays_keep_their_length_limits(bool generated)
    {
        var values = Make(generated).Generate(50);

        values.ShouldAllBe(v => v.Short.Length == 4 && v.Long.Length == 20);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Durations_and_times_of_day_stay_in_their_range(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.ShouldAllBe(v => v.Wait >= TimeSpan.FromSeconds(1) && v.Wait <= TimeSpan.FromSeconds(2));
        values.ShouldAllBe(v => v.Opens >= new TimeOnly(10, 0) && v.Opens <= new TimeOnly(11, 0));
        var open = (generated ? AnnotationFakes.OpenDuration : new ObjectGenerator<OpenDuration>(new ReflectionModel<OpenDuration>())).Seed(1).Generate(50);
        open.ShouldAllBe(o => o.Wait == TimeSpan.FromMilliseconds(1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_numeric_range_on_text_or_a_character_gives_values_inside_it(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.Select(v => v.Level).Distinct().Order(StringComparer.Ordinal).ShouldBe(["1", "2", "3"]);
        values.Select(v => v.Grade).Distinct().Order().ShouldBe(['a', 'b', 'c']);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Enums_avoid_denied_values_and_accept_allowed_names(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.Select(v => v.NotRed).Distinct().Order().ShouldBe([Shade.Green, Shade.Blue]);
        var named = (generated ? AnnotationFakes.NamedShade : new ObjectGenerator<NamedShade>(new ReflectionModel<NamedShade>())).Seed(1).Generate(100);
        named.Select(v => v.Shade).Distinct().Order().ShouldBe([Shade.Green, Shade.Blue]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Patterns_with_named_groups_lookarounds_and_unicode_categories_are_matched(bool generated)
    {
        var values = Make(generated).Generate(200);

        values.ShouldAllBe(v => System.Text.RegularExpressions.Regex.IsMatch(v.Phone, "^\\d{3}-\\d{4}$"));
        values.ShouldAllBe(v => v.Password.Any(char.IsAsciiDigit));
        values.ShouldAllBe(v => System.Text.RegularExpressions.Regex.IsMatch(v.Word, "^[A-Z][a-z]{3}[0-9]$"));
    }

    [Test]
    public void Generated_and_reflection_models_agree()
    {
        Make(true).Generate(50).Select(Describe).ShouldBe(Make(false).Generate(50).Select(Describe));

        static string Describe(Annotated a) =>
            $"{a.Code} {a.Tag} {a.Note} {a.Email} {a.LongEmail} {Convert.ToHexString(a.Short)} {Convert.ToHexString(a.Long)} {a.Wait} {a.Opens:O} {a.Level} {a.Grade} {a.NotRed} {a.Phone} {a.Password} {a.Login} {a.Word} {a.Joined}";
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Annotations_no_value_can_satisfy_say_so_instead_of_breaking_them(bool generated)
    {
        Should.Throw<InvalidOperationException>(() => (generated ? AnnotationFakes.EveryShadeDenied : new ObjectGenerator<EveryShadeDenied>(new ReflectionModel<EveryShadeDenied>())).Generate())
            .Message.ShouldContain("[DeniedValues(");
        Should.Throw<InvalidOperationException>(() => (generated ? AnnotationFakes.UnknownShade : new ObjectGenerator<UnknownShade>(new ReflectionModel<UnknownShade>())).Generate())
            .Message.ShouldContain("Purple");
        Should.Throw<InvalidOperationException>(() => (generated ? AnnotationFakes.BackReference : new ObjectGenerator<BackReference>(new ReflectionModel<BackReference>())).Generate())
            .Message.ShouldContain("[RegularExpression(");
    }

    [Test]
    public void Patterns_with_escapes_that_cannot_be_generated_are_rejected()
    {
        var faker = new Faker();

        foreach (var pattern in new[] { "(a)\\1", "\\k<x>", "\\cA", "\\q", "\\p{IsGreek}" })
            Should.Throw<FormatException>(() => faker.Helpers.FromRegExp(pattern), pattern).Message.ShouldContain(pattern);
        faker.Helpers.FromRegExp("\\Aab\\z").ShouldBe("ab");
        faker.Helpers.FromRegExp("[\\p{Lu}]{3}").ShouldMatch("^[A-Z]{3}$");
        faker.Helpers.FromRegExp("(?<y>\\d{4})-(?'m'\\d{2})").ShouldMatch("^\\d{4}-\\d{2}$");
        faker.Helpers.FromRegExp("(?<=x)ab(?<!y)").ShouldBe("ab");
        Modules.PatternGenerator.Get("^[a-z]+$").IsExact.ShouldBeTrue(); // anchors at the ends always hold
        Modules.PatternGenerator.Get("a\\bb").IsExact.ShouldBeFalse(); // an anchor inside can fail, so values are checked
        Modules.PatternGenerator.Get("a|^b").IsExact.ShouldBeFalse();
    }
}
