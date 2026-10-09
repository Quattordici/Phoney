using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Phoney.Generation;

/// <summary>
/// Validation rules of a member, taken from its data annotations (<c>[StringLength]</c>, <c>[Range]</c>,
/// <c>[RegularExpression]</c>, <c>[EmailAddress]</c>, <c>[Required]</c>…), so generated values pass validation.
/// The reflection model reads them at runtime; the source generator emits them as constants.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MemberConstraints
{
    /// <summary>Minimum length of a string, or minimum element count of a collection.</summary>
    public int? MinLength { get; init; }

    /// <summary>Maximum length of a string, or maximum element count of a collection.</summary>
    public int? MaxLength { get; init; }

    /// <summary>Lower bound from <c>[Range]</c>, as invariant text (a number or a date).</summary>
    public string? RangeMin { get; init; }

    /// <summary>Upper bound from <c>[Range]</c>, as invariant text (a number or a date).</summary>
    public string? RangeMax { get; init; }

    /// <summary>Pattern from <c>[RegularExpression]</c>; values are generated to match it.</summary>
    public string? Pattern { get; init; }

    /// <summary>Format from <c>[EmailAddress]</c>, <c>[Phone]</c>, <c>[Url]</c> or <c>[CreditCard]</c>; overrides the name convention.</summary>
    public ConventionKind? Format { get; init; }

    /// <summary>From <c>[Base64String]</c>: the value is base64 text.</summary>
    public bool Base64 { get; init; }

    /// <summary>From <c>[Required]</c>: never <see langword="null"/>, and strings are never empty.</summary>
    public bool Required { get; init; }

    /// <summary>From <c>[AllowedValues]</c>: the value is one of these.</summary>
    public object?[]? AllowedValues { get; init; }

    /// <summary>From <c>[DeniedValues]</c>: the value is never one of these.</summary>
    public object?[]? DeniedValues { get; init; }

    /// <summary>Whether the annotations alone define the value (format, pattern, range or allowed values), which satisfies strict mode.</summary>
    public bool DefinesValue => Format is not null || Pattern is not null || RangeMin is not null || AllowedValues is not null || Base64;

    /// <summary>Reads the supported data annotations; <see langword="null"/> when there are none.</summary>
    /// <param name="attributes">Attributes of the member (and, for constructor parameters, of the matching property).</param>
    internal static MemberConstraints? From(IEnumerable<Attribute> attributes)
    {
        int? min = null, max = null;
        string? rangeMin = null, rangeMax = null, pattern = null;
        ConventionKind? format = null;
        bool base64 = false, required = false;
        object?[]? allowed = null, denied = null;

        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case StringLengthAttribute a:
                    max = a.MaximumLength;
                    if (a.MinimumLength > 0)
                        min = a.MinimumLength;
                    break;
                case MaxLengthAttribute { Length: > 0 } a:
                    max = a.Length;
                    break;
                case MinLengthAttribute a:
                    min = a.Length;
                    break;
                case LengthAttribute a:
                    (min, max) = (a.MinimumLength, a.MaximumLength);
                    break;
                case RangeAttribute a:
                    rangeMin = Invariant(a.Minimum);
                    rangeMax = Invariant(a.Maximum);
                    break;
                case RegularExpressionAttribute a:
                    pattern = a.Pattern;
                    break;
                case EmailAddressAttribute:
                    format = ConventionKind.Email;
                    break;
                case PhoneAttribute:
                    format = ConventionKind.Phone;
                    break;
                case UrlAttribute:
                    format = ConventionKind.Url;
                    break;
                case CreditCardAttribute:
                    format = ConventionKind.CreditCardNumber;
                    break;
                case Base64StringAttribute:
                    base64 = true;
                    break;
                case RequiredAttribute:
                    required = true;
                    break;
                case AllowedValuesAttribute a:
                    allowed = a.Values;
                    break;
                case DeniedValuesAttribute a:
                    denied = a.Values;
                    break;
            }
        }

        var found = min is not null || max is not null || rangeMin is not null || pattern is not null || format is not null
            || base64 || required || allowed is not null || denied is not null;
        return found
            ? new MemberConstraints
            {
                MinLength = min,
                MaxLength = max,
                RangeMin = rangeMin,
                RangeMax = rangeMax,
                Pattern = pattern,
                Format = format,
                Base64 = base64,
                Required = required,
                AllowedValues = allowed,
                DeniedValues = denied,
            }
            : null;
    }

    /// <summary>Reads the annotations of a property or field, and for constructor parameters those of the matching property too.</summary>
    [RequiresUnreferencedCode("Reads attributes with reflection.")]
    internal static MemberConstraints? Read(ICustomAttributeProvider member, ICustomAttributeProvider? matchingProperty = null)
    {
        var attributes = member.GetCustomAttributes(inherit: true).OfType<Attribute>();
        if (matchingProperty is not null)
            attributes = attributes.Concat(matchingProperty.GetCustomAttributes(inherit: true).OfType<Attribute>());
        return From(attributes);
    }

    private static string? Invariant(object? value) => value switch
    {
        null => null,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
