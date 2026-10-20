using System.Runtime.CompilerServices;

namespace Phoney;

/// <summary>
/// Checks for optional (<see langword="null"/> = "pick one") arguments: a value is checked only when it is given, with the runtime's own helpers, so the
/// message and the parameter name are the ones a non-optional argument gets.
/// </summary>
internal static class OptionalArgument
{
    /// <summary>Throws when <paramref name="value"/> is given and negative.</summary>
    public static void ThrowIfNegative(int? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is { } given)
            ArgumentOutOfRangeException.ThrowIfNegative(given, paramName);
    }

    /// <summary>Throws when <paramref name="value"/> is given and zero or negative.</summary>
    public static void ThrowIfNegativeOrZero(int? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is { } given)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(given, paramName);
    }

    /// <summary>Throws when <paramref name="value"/> is given and greater than <paramref name="other"/>.</summary>
    public static void ThrowIfGreaterThan(int? value, int other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is { } given)
            ArgumentOutOfRangeException.ThrowIfGreaterThan(given, other, paramName);
    }
}
