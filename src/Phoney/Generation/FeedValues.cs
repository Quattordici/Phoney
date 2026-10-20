using System.Globalization;

namespace Phoney.Generation;

/// <summary>Converts the text of a <see cref="DataFeed"/> value to the type of the member it fills.</summary>
internal static class FeedValues
{
    /// <summary>Converts <paramref name="text"/> to <paramref name="type"/> with the invariant culture; <see langword="null"/> text and empty text of a nullable type give <see langword="null"/>.</summary>
    /// <exception cref="FormatException">The text is not a value of the type, or the type can't be read from text.</exception>
    public static object? Convert(string? text, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var target = underlying ?? type;
        if (text is null)
        {
            if (underlying is not null || !target.IsValueType)
                return null;
            throw new FormatException("the value is null, but the member can't be null");
        }

        if (text.Length == 0)
        {
            if (underlying is not null)
                return null;
            if (target == typeof(string))
                return "";
            throw new FormatException("the value is empty");
        }

        var culture = CultureInfo.InvariantCulture;
        try
        {
            if (target.IsEnum)
                return EnumValue(text, target);

            return Type.GetTypeCode(target) switch
            {
                TypeCode.String => text,
                TypeCode.Char => text.Length == 1 ? text[0] : throw new FormatException("more than one character"),
                TypeCode.Boolean => Bool(text),
                TypeCode.SByte => sbyte.Parse(text, NumberStyles.Integer, culture),
                TypeCode.Byte => byte.Parse(text, NumberStyles.Integer, culture),
                TypeCode.Int16 => short.Parse(text, NumberStyles.Integer, culture),
                TypeCode.UInt16 => ushort.Parse(text, NumberStyles.Integer, culture),
                TypeCode.Int32 => int.Parse(text, NumberStyles.Integer, culture),
                TypeCode.UInt32 => uint.Parse(text, NumberStyles.Integer, culture),
                TypeCode.Int64 => long.Parse(text, NumberStyles.Integer, culture),
                TypeCode.UInt64 => ulong.Parse(text, NumberStyles.Integer, culture),
                TypeCode.Single => float.Parse(text, NumberStyles.Float, culture),
                TypeCode.Double => double.Parse(text, NumberStyles.Float, culture),
                TypeCode.Decimal => decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowExponent, culture),
                TypeCode.DateTime => DateTimeValue(text, culture),
                _ => Other(text, target, culture),
            };
        }
        catch (Exception e) when (e is OverflowException or ArgumentException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    /// <summary>A value of the enum by name (any case) or number; a number that names no value (or, for flags, no combination of values) is not one.</summary>
    private static object EnumValue(string text, Type target)
    {
        if (Enum.TryParse(target, text, ignoreCase: true, out var value) && IsDefined(target, value))
            return value;
        throw new FormatException($"not one of {string.Join(", ", Enum.GetNames(target))}");

        static bool IsDefined(Type target, object value)
        {
            if (Enum.IsDefined(target, value))
                return true;
            if (!target.IsDefined(typeof(FlagsAttribute), inherit: false))
                return false;
            // A combination of flags is defined when every bit belongs to a named value.
            var bits = Bits(value);
            foreach (var named in Enum.GetValuesAsUnderlyingType(target))
                bits &= ~Bits(named);
            return bits == 0;
        }

        static ulong Bits(object value) => System.Convert.GetTypeCode(value) is TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            ? unchecked((ulong)System.Convert.ToInt64(value, CultureInfo.InvariantCulture))
            : System.Convert.ToUInt64(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A <see cref="DateTime"/> that is the same on every machine: text with an offset (<c>Z</c> or <c>+02:00</c>) becomes UTC rather than the machine's local time,
    /// text without one keeps <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    private static DateTime DateTimeValue(string text, CultureInfo culture)
    {
        var value = DateTime.Parse(text, culture, DateTimeStyles.RoundtripKind);
        return value.Kind == DateTimeKind.Local ? DateTime.Parse(text, culture, DateTimeStyles.AdjustToUniversal) : value;
    }

    private static bool Bool(string text) => text.Trim().ToUpperInvariant() switch
    {
        "TRUE" or "1" or "YES" or "Y" => true,
        "FALSE" or "0" or "NO" or "N" => false,
        _ => throw new FormatException("not true/false, 1/0 or yes/no"),
    };

    private static object Other(string text, Type target, CultureInfo culture) =>
        TimeOrIdentifier(text, target, culture)
        ?? WrittenScalar(text, target, culture)
        ?? throw new FormatException($"{target.Name} can't be read from text; a feed fills strings, numbers, booleans, enums, dates, times, Guid, Uri, IPAddress and Version members");

    /// <summary>Dates, times, <see cref="Guid"/> and <see cref="Uri"/>, or <see langword="null"/> for another type.</summary>
    private static object? TimeOrIdentifier(string text, Type target, CultureInfo culture)
    {
        if (target == typeof(Guid))
            return Guid.Parse(text);
        if (target == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(text, culture, DateTimeStyles.AssumeUniversal); // without an offset: UTC, never the machine's own
        if (target == typeof(DateOnly))
            return DateOnly.Parse(text, culture);
        if (target == typeof(TimeOnly))
            return TimeOnly.Parse(text, culture);
        if (target == typeof(TimeSpan))
            return TimeSpan.Parse(text, culture);
        if (target == typeof(Uri))
            return new Uri(text, UriKind.RelativeOrAbsolute);
        return null;
    }

    /// <summary>The other scalars the writers write (<see cref="DataExport"/>), so whatever a generator writes reads back; <see langword="null"/> for another type.</summary>
    private static object? WrittenScalar(string text, Type target, CultureInfo culture)
    {
        if (target == typeof(Half))
            return Half.Parse(text, NumberStyles.Float, culture);
        if (target == typeof(Int128))
            return Int128.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(UInt128))
            return UInt128.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(System.Numerics.BigInteger))
            return System.Numerics.BigInteger.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(nint))
            return nint.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(nuint))
            return nuint.Parse(text, NumberStyles.Integer, culture);
        if (target == typeof(System.Text.Rune))
            return System.Text.Rune.DecodeFromUtf16(text, out var rune, out var length) == System.Buffers.OperationStatus.Done && length == text.Length ? rune : throw new FormatException("not one character");
        if (target == typeof(System.Net.IPAddress))
            return System.Net.IPAddress.Parse(text);
        if (target == typeof(Version))
            return Version.Parse(text);
        return null;
    }
}
