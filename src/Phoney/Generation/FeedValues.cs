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
            {
                return Enum.TryParse(target, text, ignoreCase: true, out var named) ? named : throw new FormatException($"not one of {string.Join(", ", Enum.GetNames(target))}");
            }

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
                TypeCode.DateTime => DateTime.Parse(text, culture, DateTimeStyles.RoundtripKind),
                _ => Other(text, target, culture),
            };
        }
        catch (Exception e) when (e is OverflowException or ArgumentException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static bool Bool(string text) => text.ToUpperInvariant() switch
    {
        "TRUE" or "1" or "YES" or "Y" => true,
        "FALSE" or "0" or "NO" or "N" => false,
        _ => throw new FormatException("not true/false, 1/0 or yes/no"),
    };

    private static object Other(string text, Type target, CultureInfo culture)
    {
        if (target == typeof(Guid))
            return Guid.Parse(text);
        if (target == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(text, culture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateOnly))
            return DateOnly.Parse(text, culture);
        if (target == typeof(TimeOnly))
            return TimeOnly.Parse(text, culture);
        if (target == typeof(TimeSpan))
            return TimeSpan.Parse(text, culture);
        if (target == typeof(Uri))
            return new Uri(text, UriKind.RelativeOrAbsolute);
        throw new FormatException($"{target.Name} can't be read from text; a feed fills strings, numbers, booleans, enums, dates, times, Guid and Uri members");
    }
}
