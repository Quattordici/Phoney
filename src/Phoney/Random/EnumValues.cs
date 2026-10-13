namespace Phoney;

/// <summary>The defined values of an enum, read once per type so picking one allocates nothing (<c>Enum.GetValues&lt;T&gt;()</c> copies the array on every call).</summary>
internal static class EnumValues<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>All defined values; shared, so never modified.</summary>
    public static readonly TEnum[] All = Enum.GetValues<TEnum>();
}
