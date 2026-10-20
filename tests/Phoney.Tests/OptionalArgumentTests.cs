namespace Phoney.Tests;

/// <summary>Optional arguments are checked only when given, and the error names the caller's parameter like the BCL helpers do.</summary>
public sealed class OptionalArgumentTests
{
    [Test]
    public void A_missing_value_is_not_checked()
    {
        int? count = null;

        Should.NotThrow(() => OptionalArgument.ThrowIfNegative(count));
        Should.NotThrow(() => OptionalArgument.ThrowIfNegativeOrZero(count));
        Should.NotThrow(() => OptionalArgument.ThrowIfGreaterThan(count, 3));
    }

    [Test]
    public void Values_in_range_pass()
    {
        int? count = 3;

        Should.NotThrow(() => OptionalArgument.ThrowIfNegative(count));
        Should.NotThrow(() => OptionalArgument.ThrowIfNegativeOrZero(count));
        Should.NotThrow(() => OptionalArgument.ThrowIfGreaterThan(count, 3));
        Should.NotThrow(() => OptionalArgument.ThrowIfNegative((int?)0));
    }

    [Test]
    public void Values_out_of_range_name_the_parameter_and_value()
    {
        int? count = -1;
        int? width = 0;
        int? digits = 16;

        var negative = Should.Throw<ArgumentOutOfRangeException>(() => OptionalArgument.ThrowIfNegative(count));
        negative.ParamName.ShouldBe("count");
        negative.ActualValue.ShouldBe(-1);
        Should.Throw<ArgumentOutOfRangeException>(() => OptionalArgument.ThrowIfNegativeOrZero(width)).ParamName.ShouldBe("width");
        Should.Throw<ArgumentOutOfRangeException>(() => OptionalArgument.ThrowIfGreaterThan(digits, 15)).ParamName.ShouldBe("digits");
    }

    [Test]
    public void The_messages_are_the_ones_of_the_runtime_helpers()
    {
        int? count = -1;
        var expected = Should.Throw<ArgumentOutOfRangeException>(() => ArgumentOutOfRangeException.ThrowIfNegative(-1, nameof(count))).Message;

        Should.Throw<ArgumentOutOfRangeException>(() => OptionalArgument.ThrowIfNegative(count)).Message.ShouldBe(expected);
    }
}
