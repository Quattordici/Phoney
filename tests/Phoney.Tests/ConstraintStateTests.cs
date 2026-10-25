using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The rules for merging data annotations that the reflection model and the source generator share.</summary>
public sealed class ConstraintStateTests
{
    [Test]
    public void The_strictest_length_wins_whatever_order_the_annotations_come_in()
    {
        var state = new ConstraintState();
        state.MaxLength(20);
        state.MaxLength(10);
        state.MaxLength(15);
        state.MinLength(2);
        state.MinLength(5);
        state.MinLength(3);

        (state.MinLengthValue, state.MaxLengthValue).ShouldBe((5, 10));
    }

    [Test]
    public void String_length_always_sets_the_maximum_but_only_a_positive_minimum()
    {
        var plain = new ConstraintState();
        plain.StringLength(8, 0);
        (plain.MinLengthValue, plain.MaxLengthValue).ShouldBe((null, 8));

        var both = new ConstraintState();
        both.StringLength(8, 3);
        (both.MinLengthValue, both.MaxLengthValue).ShouldBe((3, 8));
    }

    [Test]
    public void A_max_length_annotation_without_a_positive_length_sets_nothing()
    {
        var state = new ConstraintState();
        state.MaxLengthAnnotation(0);
        state.MaxLengthAnnotation(-1);

        state.MaxLengthValue.ShouldBeNull();
        state.Any.ShouldBeFalse();

        state.MaxLengthAnnotation(4);
        state.MaxLengthValue.ShouldBe(4);
    }

    [Test]
    public void A_length_annotation_sets_both_limits()
    {
        var state = new ConstraintState();
        state.Length(2, 9);

        (state.MinLengthValue, state.MaxLengthValue).ShouldBe((2, 9));
    }

    [Test]
    public void A_range_keeps_its_bounds_and_exclusive_flags()
    {
        var state = new ConstraintState();
        state.Range("1", "10");
        state.ExclusiveMinimum();

        (state.RangeMin, state.RangeMax, state.RangeMinExclusive, state.RangeMaxExclusive).ShouldBe(("1", "10", true, false));
    }

    [Test]
    public void A_later_range_replaces_an_earlier_one_flags_included()
    {
        var state = new ConstraintState();
        state.Range("1", "10");
        state.ExclusiveMinimum();
        state.ExclusiveMaximum();
        state.Range("5", "6");

        (state.RangeMin, state.RangeMax, state.RangeMinExclusive, state.RangeMaxExclusive).ShouldBe(("5", "6", false, false));
    }

    [Test]
    public void The_keys_are_recorded_in_the_order_they_were_first_set()
    {
        var state = new ConstraintState();
        state.MaxLength(10);
        state.Range("1", "2");
        state.MinLength(1);
        state.MaxLength(5);
        state.RegularExpression("a");
        state.Required();

        state.SetKeys.ShouldBe(["MaxLength", "RangeMin", "RangeMax", "MinLength", "Pattern", "Required"]);
    }

    [Test]
    public void Only_a_format_pattern_range_allowed_values_or_base64_define_the_value()
    {
        bool Defines(Action<ConstraintState> set) { var s = new ConstraintState(); set(s); return s.DefinesValue; }

        Defines(s => s.Format(ConventionKind.Email)).ShouldBeTrue();
        Defines(s => s.RegularExpression("x")).ShouldBeTrue();
        Defines(s => s.Range("1", "2")).ShouldBeTrue();
        Defines(s => s.AllowedValues()).ShouldBeTrue();
        Defines(s => s.Base64()).ShouldBeTrue();

        Defines(s => s.MaxLength(3)).ShouldBeFalse();
        Defines(s => s.MinLength(3)).ShouldBeFalse();
        Defines(s => s.Required()).ShouldBeFalse();
        Defines(s => s.DeniedValues()).ShouldBeFalse();
    }

    [Test]
    public void Any_is_true_once_any_annotation_was_read()
    {
        new ConstraintState().Any.ShouldBeFalse();
        var denied = new ConstraintState();
        denied.DeniedValues();
        denied.Any.ShouldBeTrue();
        var format = new ConstraintState();
        format.Format(ConventionKind.Email);
        format.Any.ShouldBeTrue();
    }

    [Test]
    [Arguments(1, "1")]
    [Arguments(1.5, "1.5")]
    [Arguments(0.1, "0.1")]
    [Arguments(1e20, "1E+20")]
    public void Bounds_are_written_with_the_invariant_culture(object value, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
        try
        {
            ConstraintState.Invariant(value).ShouldBe(expected);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
