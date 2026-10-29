using System.ComponentModel.DataAnnotations;
using Phoney.Generation;

namespace Phoney.Tests;

public enum Tier
{
    Free = 1,
    Pro = 2,
    Team = 4,
}

[Flags]
public enum Access
{
    Read = 1,
    Write = 2,
    Admin = 4,
}

[Flags]
public enum Rights
{
    None = 0,
    View = 1,
    Edit = 2,
}

/// <summary>A sign-up form with the annotations beyond lengths, ranges and patterns: comparisons, enum codes, file extensions and data type hints.</summary>
public sealed class Registration
{
    [Compare(nameof(Code))] public string CodeAgain { get; set; } = ""; // compared with a member that comes later
    public string Code { get; set; } = "";
    public string Password { get; set; } = "";
    [Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
    [EmailAddress] public string Email { get; set; } = "";
    [Compare(nameof(Email))] public string RepeatEmail { get; set; } = "";
    [EnumDataType(typeof(Tier))] public string PlanName { get; set; } = "";
    [EnumDataType(typeof(Tier))] public int PlanId { get; set; }
    [EnumDataType(typeof(Access))] public long AccessBits { get; set; }
    [EnumDataType(typeof(Rights))] public uint RightBits { get; set; }
    [FileExtensions(Extensions = "pdf,docx"), MaxLength(12)] public string Resume { get; set; } = "";
    [FileExtensions] public string Photo { get; set; } = "";
    [DataType(DataType.EmailAddress)] public string Contact { get; set; } = "";
    [DataType(DataType.PhoneNumber)] public string Reach { get; set; } = "";
    [DataType(DataType.PostalCode)] public string Area { get; set; } = "";
    [DataType(DataType.ImageUrl)] public string Picture { get; set; } = "";
    [DataType(DataType.MultilineText)] public string About { get; set; } = "";
    [DataType(DataType.Currency)] public decimal Fee { get; set; }
}

/// <summary>A comparison between constructor parameters, which can only be set through <c>init</c>.</summary>
public sealed record PinChange(string Pin, [property: Compare(nameof(PinChange.Pin))] string PinAgain);

/// <summary>A form inside another object.</summary>
public sealed class Club
{
    public Registration Form { get; set; } = new();
}

/// <summary>A comparison with a member Phoney doesn't generate.</summary>
public sealed class Mismatched
{
    public string First { get; set; } = "";
    [Compare(nameof(Shout))] public string Again { get; set; } = "";
    public string Shout => First.ToUpperInvariant();
}

/// <summary>Annotations of a code base's own, built on the standard ones.</summary>
public sealed class SkuAttribute() : RegularExpressionAttribute("^[A-Z]{3}-[0-9]{4}$");

/// <inheritdoc cref="SkuAttribute"/>
public sealed class PrefixedAttribute(string prefix) : RegularExpressionAttribute("^" + prefix + "-[0-9]{3}$");

/// <inheritdoc cref="SkuAttribute"/>
public sealed class ShortTextAttribute() : StringLengthAttribute(8);

/// <inheritdoc cref="SkuAttribute"/>
public sealed class StockAttribute() : RangeAttribute(1, 99);

/// <summary>A product described with annotations derived from the standard ones.</summary>
public sealed class Product
{
    [Sku] public string Code { get; set; } = "";
    [Prefixed("AB")] public string Batch { get; set; } = "";
    [ShortText(MinimumLength = 3), Required] public string Label { get; set; } = "";
    [Stock] public int InStock { get; set; }
}

[FakeFor<Product>]
[FakeFor<Mismatched>]
[FakeFor<Registration>]
[FakeFor<PinChange>]
[FakeFor<Club>]
public static partial class ValidationAnnotationFakes;

public sealed class ValidationAnnotationTests
{
    private static ObjectGenerator<T> Make<T>(bool generated, ObjectGenerator<T> fromGenerator) =>
        (generated ? fromGenerator : new ObjectGenerator<T>(new ReflectionModel<T>())).Seed(7);

    private static ObjectGenerator<Registration> Registrations(bool generated) => Make(generated, ValidationAnnotationFakes.Registration);

    private static string[] Failing(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        return [.. results.SelectMany(r => r.MemberNames).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Every_generated_form_passes_validation(bool generated)
    {
        var values = Registrations(generated).Generate(300);

        values.SelectMany(Failing).Distinct().ShouldBeEmpty();
        Make(generated, ValidationAnnotationFakes.PinChange).Generate(50).SelectMany(Failing).Distinct().ShouldBeEmpty();
        Make(generated, ValidationAnnotationFakes.Club).Generate(50).Select(m => m.Form).SelectMany(Failing).Distinct().ShouldBeEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_compared_member_repeats_the_other_whichever_comes_first(bool generated)
    {
        var values = Registrations(generated).Generate(50);

        values.ShouldAllBe(v => v.ConfirmPassword == v.Password && v.RepeatEmail == v.Email && v.CodeAgain == v.Code);
        values.Select(v => v.Password).Distinct().Count().ShouldBeGreaterThan(40);
        Make(generated, ValidationAnnotationFakes.PinChange).Generate(20).ShouldAllBe(p => p.PinAgain == p.Pin && p.Pin.Length > 0);
        Make(generated, ValidationAnnotationFakes.Club).Generate(20).ShouldAllBe(m => m.Form.ConfirmPassword == m.Form.Password);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_rule_wins_over_a_comparison_and_the_compared_member_follows_a_rule_on_the_other(bool generated)
    {
        Registrations(generated).With(x => x.ConfirmPassword, "typo").Generate().ConfirmPassword.ShouldBe("typo");

        var followed = Registrations(generated).With(x => x.Password, "s3cret!").Generate();
        followed.ConfirmPassword.ShouldBe("s3cret!");

        Make(generated, ValidationAnnotationFakes.Club).With(x => x.Form.Password, "nested").Generate().Form.ConfirmPassword.ShouldBe("nested");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Populate_matches_a_kept_value_in_either_direction(bool generated)
    {
        Registrations(generated).Populate(new Registration { Password = "kept" }).ConfirmPassword.ShouldBe("kept");
        Registrations(generated).Populate(new Registration { ConfirmPassword = "kept" }).Password.ShouldBe("kept");

        var all = Registrations(generated).Populate(new Registration { Password = "old", ConfirmPassword = "old" }, PopulateMode.All);
        all.ConfirmPassword.ShouldBe(all.Password);
        all.Password.ShouldNotBe("old");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Enum_data_types_give_names_for_text_and_defined_values_for_numbers(bool generated)
    {
        var values = Registrations(generated).Generate(200);

        values.Select(v => v.PlanName).Distinct().Order(StringComparer.Ordinal).ShouldBe(["Free", "Pro", "Team"]);
        values.Select(v => v.PlanId).Distinct().Order().ShouldBe([1, 2, 4]);
        values.Select(v => v.AccessBits).Distinct().Order().ShouldBe([1L, 2L, 4L]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void File_extensions_give_file_names_with_a_listed_extension(bool generated)
    {
        var values = Registrations(generated).Generate(200);

        values.Select(v => Path.GetExtension(v.Resume)).Distinct().Order(StringComparer.Ordinal).ShouldBe([".docx", ".pdf"]);
        values.ShouldAllBe(v => v.Resume.Length <= 12 && v.Resume.Length > ".docx".Length);
        values.Select(v => Path.GetExtension(v.Photo)).Distinct().Order(StringComparer.Ordinal).ShouldBe([".gif", ".jpeg", ".jpg", ".png"]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_data_type_picks_the_value_like_a_name_would(bool generated)
    {
        var values = Registrations(generated).Generate(100);

        values.ShouldAllBe(v => v.Contact == v.Email); // the person's email, like a member called Email
        values.ShouldAllBe(v => new PhoneAttribute().IsValid(v.Reach) && v.Reach.Any(char.IsAsciiDigit));
        values.ShouldAllBe(v => v.Area.Length > 0 && v.Area.Any(char.IsAsciiDigit));
        values.ShouldAllBe(v => Uri.IsWellFormedUriString(v.Picture, UriKind.Absolute));
        values.ShouldAllBe(v => v.About.Contains(' ') && v.About.Length > 40);
        values.ShouldAllBe(v => v.Fee > 0 && decimal.Round(v.Fee, 2) == v.Fee);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_data_type_hint_counts_for_strict_mode(bool generated) =>
        Should.NotThrow(() => Registrations(generated).Strict().Ignore(x => x.Code).Ignore(x => x.Password).Generate());

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Invalid_breaks_enum_data_types_and_file_extensions(bool generated)
    {
        foreach (var c in Registrations(generated).InvalidCases())
            Failing(c.Value).ShouldNotBeEmpty(c.ToString());

        Registrations(generated).Invalid(x => x.PlanName).Generate(20).ShouldAllBe(v => Failing(v).SequenceEqual(new[] { nameof(Registration.PlanName) }));
        Registrations(generated).Invalid(x => x.PlanId).Generate(20).ShouldAllBe(v => Failing(v).SequenceEqual(new[] { nameof(Registration.PlanId) }));
        Registrations(generated).Invalid(x => x.AccessBits).Generate(20).ShouldAllBe(v => Failing(v).SequenceEqual(new[] { nameof(Registration.AccessBits) }));
        Registrations(generated).Invalid(x => x.RightBits).Generate(20).ShouldAllBe(v => Failing(v).SequenceEqual(new[] { nameof(Registration.RightBits) }));
        Registrations(generated).Invalid(x => x.Photo).Generate(20).ShouldAllBe(v => Failing(v).SequenceEqual(new[] { nameof(Registration.Photo) }));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Annotations_derived_from_the_standard_ones_are_honored(bool generated)
    {
        var products = Make(generated, ValidationAnnotationFakes.Product).Generate(200);

        products.SelectMany(Failing).Distinct().ShouldBeEmpty();
        products.ShouldAllBe(p => p.Label.Length >= 3 && p.Label.Length <= 8 && p.InStock >= 1 && p.InStock <= 99 && p.Batch.StartsWith("AB-"));
    }

    [Test]
    public void Generated_and_reflection_models_agree_on_derived_annotations() =>
        Make(true, ValidationAnnotationFakes.Product).Generate(30).Select(p => $"{p.Code} {p.Batch} {p.Label} {p.InStock}")
            .ShouldBe(Make(false, ValidationAnnotationFakes.Product).Generate(30).Select(p => $"{p.Code} {p.Batch} {p.Label} {p.InStock}"));

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void A_comparison_Phoney_cannot_satisfy_says_how_to_fix_it(bool generated)
    {
        var mismatched = Make(generated, ValidationAnnotationFakes.Mismatched);

        Should.Throw<InvalidOperationException>(() => mismatched.Generate()).Message.ShouldContain("Mismatched.Again has [Compare(\"Shout\")]");
        mismatched.With(x => x.Again, "set by a rule").Generate().Again.ShouldBe("set by a rule");
    }

    [Test]
    public void Generated_and_reflection_models_agree()
    {
        Registrations(true).Generate(50).Select(Describe).ShouldBe(Registrations(false).Generate(50).Select(Describe));

        static string Describe(Registration s) =>
            $"{s.CodeAgain} {s.Code} {s.Password} {s.ConfirmPassword} {s.Email} {s.RepeatEmail} {s.PlanName} {s.PlanId} {s.AccessBits} {s.RightBits} {s.Resume} {s.Photo} {s.Contact} {s.Reach} {s.Area} {s.Picture} {s.About} {s.Fee}";
    }
}
