using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Text;
using Produce = System.Func<Phoney.Generation.FakeScope, System.Func<Phoney.Generation.FakeScope, object?>, object?>;

namespace Phoney.Generation;

/// <summary>
/// One way to break, or to sit exactly on, a limit of a member's data annotations. <paramref name="Produce"/> receives the
/// member's normal (valid) producer: it always runs it first, so everything drawn after the member matches a valid object.
/// </summary>
internal sealed record ConstraintCase(string Description, Produce Produce);

/// <summary>
/// Derives invalid values and boundary values from a member's <see cref="MemberConstraints"/>. Reflection and generated
/// models describe members with the same constraints, so both produce the same cases.
/// </summary>
internal static class ConstraintCases
{
    /// <summary>Strings and collections longer than this are not built: a limit like <c>[MaxLength(int.MaxValue)]</c> has no useful violation.</summary>
    private const int LongestFabricated = 100_000;

    /// <summary>Texts that break most formats; the first one an annotation rejects is used.</summary>
    private static readonly string[] BadTexts = ["not valid", "!", "@", "000", "x", "###", " "];

    private enum Shape { Other, String, Collection, Number, Date }

    private enum Side { Min, Max }

    /// <summary>Picks the producer of a <see cref="ConstraintRequest"/>: the requested case, or one per object when none was requested.</summary>
    /// <exception cref="InvalidOperationException">The member has nothing to break (or to sit on the edge of).</exception>
    public static Produce Resolve(string owner, FakeMember member, ConstraintRequest request)
    {
        var cases = request.Boundary ? Boundary(member) : Invalid(member);
        if (cases.Count == 0)
        {
            throw new InvalidOperationException(request.Boundary
                ? $"{owner}.{member.Name} has no limits to sit on: add [Range], [StringLength], [MinLength] or [MaxLength] to it."
                : $"{owner}.{member.Name} has no data annotations that can be broken: add [Range], [StringLength], [MinLength], [MaxLength], [Required], [RegularExpression], [EmailAddress], [AllowedValues] or [DeniedValues] to it.");
        }

        if (request.Index is { } index)
            return cases[index].Produce;
        return cases.Count == 1 ? cases[0].Produce : (scope, valid) => cases[scope.Faker.Random.Index(cases.Count)].Produce(scope, valid);
    }

    /// <summary>The ways to violate the member's annotations, in a fixed order (required, length, range, format, values).</summary>
    public static IReadOnlyList<ConstraintCase> Invalid(FakeMember member)
    {
        var cases = new List<ConstraintCase>();
        if (member.Constraints is not { } c)
            return cases;

        var type = Underlying(member.Type);
        var shape = ShapeOf(type);
        if (c.Required)
            Required(cases, member.Type, shape);

        switch (shape)
        {
            case Shape.String:
                InvalidLength(cases, c, "shorter than the minimum length", "longer than the maximum length", Text);
                InvalidText(cases, c);
                break;
            case Shape.Collection:
                InvalidLength(cases, c, "fewer elements than the minimum", "more elements than the maximum", Count);
                break;
            case Shape.Number or Shape.Date:
                InvalidRange(cases, type, shape, c);
                break;
        }

        if (shape == Shape.Number && c.AllowedValues is { Length: > 0 } allowed && NotAllowedNumber(type, allowed) is { } number)
            cases.Add(new("not one of the allowed values", Replace(number)));
        if (c.DeniedValues?.FirstOrDefault(d => d is not null && type.IsInstanceOfType(d)) is { } denied)
            cases.Add(new($"a denied value ({denied})", Replace(denied)));
        return cases;
    }

    /// <summary>The values exactly on the limits of the member's annotations: smallest and largest number, date, length or element count.</summary>
    public static IReadOnlyList<ConstraintCase> Boundary(FakeMember member)
    {
        var cases = new List<ConstraintCase>();
        if (member.Constraints is not { } c)
            return cases;

        var type = Underlying(member.Type);
        switch (ShapeOf(type))
        {
            // A pattern, format or list of values defines the text, so only its length can't be picked freely.
            case Shape.String when c.Pattern is null && c.Format is null && !c.Base64 && c.AllowedValues is null:
                BoundaryLength(cases, c, min: Math.Max(c.MinLength ?? 0, c.Required ? 1 : 0), "the minimum length", "the maximum length", Text);
                break;
            case Shape.Collection:
                BoundaryLength(cases, c, min: c.MinLength ?? 0, "the minimum element count", "the maximum element count", Count);
                break;
            case var shape and (Shape.Number or Shape.Date):
                BoundaryRange(cases, type, shape, Side.Min, c.RangeMin, c.RangeMinExclusive);
                BoundaryRange(cases, type, shape, Side.Max, c.RangeMax, c.RangeMaxExclusive);
                break;
        }

        return cases;
    }

    /// <summary>The value on one end of a <c>[Range]</c>: the bound itself, or the nearest value inside an excluded one.</summary>
    private static void BoundaryRange(List<ConstraintCase> cases, Type type, Shape shape, Side side, string? text, bool exclusive)
    {
        var name = side == Side.Min ? "minimum" : "maximum";
        if (TryBound(type, shape, text, side, outside: false, exclusive) is { } value)
            cases.Add(new(exclusive ? $"next to the excluded {name} ({text})" : $"the {name} ({text})", Replace(value)));
    }

    private static void Required(List<ConstraintCase> cases, Type memberType, Shape shape)
    {
        if (shape == Shape.String)
            cases.Add(new("required but empty", Replace("")));
        if (!memberType.IsValueType || Nullable.GetUnderlyingType(memberType) is not null)
            cases.Add(new("required but null", Replace(null)));
    }

    /// <summary>One below the minimum and one above the maximum length (or element count).</summary>
    private static void InvalidLength(List<ConstraintCase> cases, MemberConstraints c, string below, string above, Func<int, Produce> to)
    {
        if (c.MinLength is > 0 and var min)
            cases.Add(new($"{below} ({min})", to(min - 1)));
        if (c.MaxLength is { } max and < LongestFabricated)
            cases.Add(new($"{above} ({max})", to(max + 1)));
    }

    private static void BoundaryLength(List<ConstraintCase> cases, MemberConstraints c, int min, string smallest, string largest, Func<int, Produce> to)
    {
        if (c.MinLength is null && c.MaxLength is null && !c.Required)
            return;
        cases.Add(new($"{smallest} ({min})", to(min)));
        if (c.MaxLength is { } max and < LongestFabricated && max > min)
            cases.Add(new($"{largest} ({max})", to(max)));
    }

    /// <summary>Texts that the pattern, format or list of allowed values rejects, checked with the real validators.</summary>
    private static void InvalidText(List<ConstraintCase> cases, MemberConstraints c)
    {
        if (c.Pattern is { } pattern && Rejected(new RegularExpressionAttribute(pattern), c) is { } unmatched)
            cases.Add(new($"does not match the pattern {pattern}", Replace(unmatched)));

        if (c.Format is { } format && FormatOf(format) is var (attribute, name) && Rejected(attribute, c) is { } malformed)
            cases.Add(new($"not {name}", Replace(malformed)));

        if (c.Base64 && Rejected(new Base64StringAttribute(), c) is { } notBase64)
            cases.Add(new("not valid base64", Replace(notBase64)));

        if (c.AllowedValues is { Length: > 0 } allowed)
        {
            var taken = allowed.Select(a => a?.ToString()).ToHashSet(StringComparer.Ordinal);
            var other = "not allowed";
            var underscores = 0;
            while (taken.Contains(other))
                other = "not allowed" + new string('_', ++underscores);
            cases.Add(new("not one of the allowed values", Replace(other)));
        }
    }

    /// <summary>One unit below the minimum and above the maximum of a <c>[Range]</c>.</summary>
    private static void InvalidRange(List<ConstraintCase> cases, Type type, Shape shape, MemberConstraints c)
    {
        if (TryBound(type, shape, c.RangeMin, Side.Min, outside: true, c.RangeMinExclusive) is { } below)
            cases.Add(new(c.RangeMinExclusive ? $"the excluded minimum ({c.RangeMin})" : $"below the minimum ({c.RangeMin})", Replace(below)));
        if (TryBound(type, shape, c.RangeMax, Side.Max, outside: true, c.RangeMaxExclusive) is { } above)
            cases.Add(new(c.RangeMaxExclusive ? $"the excluded maximum ({c.RangeMax})" : $"above the maximum ({c.RangeMax})", Replace(above)));
    }

    /// <summary>
    /// The bound of a range, or the value one unit (a whole number, a day) outside it; <see langword="null"/> when the member's type can't hold it. An <paramref name="exclusive"/> bound is
    /// itself the value outside, and the nearest value inside is one step away from it (a whole number, a day, a tick, the next double, the last digit of a decimal).
    /// </summary>
    private static object? TryBound(Type type, Shape shape, string? text, Side side, bool outside, bool exclusive)
    {
        if (text is null)
            return null;
        var inward = side == Side.Min ? 1 : -1; // the direction of the inside of the range
        try
        {
            if (shape == Shape.Date)
                return DateBound(type, text, outside, exclusive, inward);
            if (type == typeof(double) || type == typeof(float) || type == typeof(Half))
                return FractionalBound(type, text, outside, exclusive, inward);

            var number = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (type == typeof(decimal)) // a whole unit outside an inclusive bound, the nearest decimal inside an excluded one
                return ToNumber(type, (outside, exclusive) switch { (true, false) => number - inward, (false, true) => NextDecimal(number, inward), _ => number });

            // Whole-number types: the nearest whole number inside the range, and the one next to it outside.
            var inside = (side, exclusive) switch
            {
                (Side.Min, true) => Math.Floor(number) + 1,
                (Side.Min, false) => Math.Ceiling(number),
                (_, true) => Math.Ceiling(number) - 1,
                _ => Math.Floor(number),
            };
            return ToNumber(type, outside ? inside - inward : inside);
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException or ArgumentOutOfRangeException)
        {
            return null; // e.g. one below zero for a byte
        }
    }

    /// <summary>The decimal nearest to <paramref name="value"/> in the <paramref name="direction"/>: the smallest power of ten (down to 1e-28) that a sum still holds exactly.</summary>
    private static decimal NextDecimal(decimal value, int direction)
    {
        for (var scale = (byte)28; scale > 0; scale--)
        {
            var step = new decimal(1, 0, 0, isNegative: direction < 0, scale);
            var next = value + step;
            if (next - value == step) // a sum with more digits than a decimal holds is rounded
                return next;
        }

        return value + direction;
    }

    /// <summary>A date bound of a <see cref="DateTime"/>, <see cref="DateTimeOffset"/> or <see cref="DateOnly"/> member: a day outside an inclusive bound, a day or a tick inside an excluded one.</summary>
    private static object DateBound(Type type, string text, bool outside, bool exclusive, int inward)
    {
        var bound = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        var date = (outside, exclusive) switch
        {
            (true, false) => bound.AddDays(-inward),
            (false, true) => type == typeof(DateOnly) ? bound.AddDays(inward) : bound.AddTicks(inward),
            _ => bound,
        };
        if (type == typeof(DateTime))
            return date.UtcDateTime;
        return type == typeof(DateTimeOffset) ? date : DateOnly.FromDateTime(date.UtcDateTime);
    }

    /// <summary>A bound of a <see cref="double"/>, <see cref="float"/> or <see cref="Half"/> member: a whole unit outside an inclusive bound, else the value at the limit.</summary>
    private static object FractionalBound(Type type, string text, bool outside, bool exclusive, int inward)
    {
        var limit = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (outside && !exclusive)
            return type == typeof(Half) ? (Half)(limit - inward) : Convert.ChangeType(limit - inward, type, CultureInfo.InvariantCulture);
        // Inside an excluded limit strictly; the inclusive bound itself and the excluded limit (the invalid case) may sit on it.
        var direction = outside ? -inward : inward;
        if (type == typeof(float))
            return AtLimit<float>(limit, direction, strict: exclusive && !outside);
        if (type == typeof(Half))
            return AtLimit<Half>(limit, direction, strict: exclusive && !outside);
        return AtLimit<double>(limit, direction, strict: exclusive && !outside);
    }

    /// <summary>
    /// The <typeparamref name="TFloat"/> nearest to <paramref name="limit"/> on its <paramref name="direction"/> side (beyond it when <paramref name="strict"/>), on that side whether
    /// the validator compares as a <see cref="double"/> or, with a float operand, as a <typeparamref name="TFloat"/> with the limit converted too.
    /// </summary>
    private static TFloat AtLimit<TFloat>(double limit, int direction, bool strict)
        where TFloat : IFloatingPointIeee754<TFloat>
    {
        var converted = TFloat.CreateSaturating(limit);
        var side = double.CreateSaturating(converted).CompareTo(limit) * direction; // below zero: the conversion moved it to the other side
        if (!strict && side >= 0)
            return converted;
        return direction > 0 ? TFloat.BitIncrement(converted) : TFloat.BitDecrement(converted);
    }

    /// <summary>A decimal as a number of <paramref name="type"/>, which may be one <see cref="Convert"/> does not support; throws <see cref="OverflowException"/> when it does not fit.</summary>
    private static object ToNumber(Type type, decimal value) => type switch
    {
        _ when type == typeof(Int128) => checked((Int128)value),
        _ when type == typeof(UInt128) => checked((UInt128)value),
        _ when type == typeof(System.Numerics.BigInteger) => new System.Numerics.BigInteger(value),
        _ when type == typeof(nint) => checked((nint)value),
        _ when type == typeof(nuint) => checked((nuint)value),
        _ => Convert.ChangeType(value, type, CultureInfo.InvariantCulture),
    };

    /// <summary>A number that is not in the list of allowed values: one more than the largest.</summary>
    private static object? NotAllowedNumber(Type type, object?[] allowed)
    {
        try
        {
            return ToNumber(type, allowed.Max(a => Convert.ToDecimal(a, CultureInfo.InvariantCulture)) + 1);
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>The first of the bad texts, fitted to the length limits, that <paramref name="attribute"/> rejects; <see langword="null"/> if it accepts them all.</summary>
    private static string? Rejected(ValidationAttribute attribute, MemberConstraints c)
    {
        foreach (var candidate in BadTexts)
        {
            var text = Fit(candidate, c);
            try
            {
                if (!attribute.IsValid(text))
                    return text;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or TimeoutException)
            {
                return null; // e.g. an invalid pattern: nothing to compare with
            }
        }

        return null;
    }

    private static string Fit(string text, MemberConstraints c)
    {
        var min = Math.Max(c.MinLength ?? 0, c.Required ? 1 : 0);
        return Resize(text.Length < min ? text.PadRight(min, 'x') : text, c.MaxLength ?? int.MaxValue, exact: false);
    }

    private static (ValidationAttribute Attribute, string Name)? FormatOf(ConventionKind format) => format switch
    {
        ConventionKind.Email => (new EmailAddressAttribute(), "an email address"),
        ConventionKind.Phone => (new PhoneAttribute(), "a phone number"),
        ConventionKind.Url => (new UrlAttribute(), "a URL"),
        ConventionKind.CreditCardNumber => (new CreditCardAttribute(), "a credit card number"),
        _ => null,
    };

    /// <summary>The member's valid text cut or repeated to an exact number of characters.</summary>
    private static Func<int, Produce> Text { get; } = length => (scope, valid) => Resize(valid(scope) as string ?? "", length, exact: true);

    /// <summary>The member's collection built with an exact number of elements.</summary>
    private static Func<int, Produce> Count { get; } = count => (scope, valid) =>
    {
        scope.ForceNextCollectionCount(count);
        try
        {
            return valid(scope);
        }
        finally
        {
            scope.ForceNextCollectionCount(null); // a member that stayed null never asked for the count
        }
    };

    /// <summary>Replaces the member's value, after producing the valid one so later members draw the same randomness as in a valid object.</summary>
    private static Produce Replace(object? value) => (scope, valid) =>
    {
        valid(scope);
        return value;
    };

    private static string Resize(string text, int length, bool exact)
    {
        if (length <= 0)
            return "";
        if (text.Length >= length)
            return text[..length];
        if (!exact)
            return text;

        // Repeat the text with spaces between, so a name that is too long still reads like words; never end on a space.
        var repeated = new StringBuilder(length + text.Length + 1);
        var unit = text.Length == 0 ? "x" : text;
        while (repeated.Length < length)
            repeated.Append(unit).Append(' ');
        repeated.Length = length;
        if (repeated[^1] == ' ')
            repeated[^1] = 'x';
        return repeated.ToString();
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Shape ShapeOf(Type type)
    {
        if (type == typeof(string))
            return Shape.String;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
            return Shape.Date;
        if (type.IsEnum)
            return Shape.Other;
        if (Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal || ConventionRules.IsNumber(FakeMember.ScalarCategory(type)))
            return Shape.Number; // also Half, Int128, UInt128, BigInteger and nint/nuint, which Convert does not know
        return CollectionTypes.IsGeneratedCollection(type) ? Shape.Collection : Shape.Other;
    }
}

/// <summary>What <c>Invalid</c> and <c>Boundary</c> ask for: <paramref name="Index"/> picks one case of the member, <see langword="null"/> lets each object pick.</summary>
internal sealed record ConstraintRequest(bool Boundary, int? Index);
