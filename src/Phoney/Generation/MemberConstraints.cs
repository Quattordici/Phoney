using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
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

    /// <summary>From <c>[Range(MinimumIsExclusive = true)]</c>: <see cref="RangeMin"/> itself is outside the range.</summary>
    public bool RangeMinExclusive { get; init; }

    /// <summary>From <c>[Range(MaximumIsExclusive = true)]</c>: <see cref="RangeMax"/> itself is outside the range.</summary>
    public bool RangeMaxExclusive { get; init; }

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
    public bool DefinesValue => ConstraintState.Defines(Format is not null, Pattern is not null, RangeMin is not null, AllowedValues is not null, Base64);

    /// <summary>Reads the supported data annotations; <see langword="null"/> when there are none.</summary>
    /// <param name="attributes">Attributes of the member (and, for constructor parameters, of the matching property).</param>
    internal static MemberConstraints? From(IEnumerable<Attribute> attributes)
    {
        var state = new ConstraintState();
        object?[]? allowed = null, denied = null;

        foreach (var attribute in attributes)
        {
            switch (attribute)
            {
                case StringLengthAttribute a:
                    state.StringLength(a.MaximumLength, a.MinimumLength);
                    break;
                case MaxLengthAttribute a:
                    state.MaxLengthAnnotation(a.Length);
                    break;
                case MinLengthAttribute a:
                    state.MinLength(a.Length);
                    break;
                case LengthAttribute a:
                    state.Length(a.MinimumLength, a.MaximumLength);
                    break;
                case RangeAttribute a:
                    state.Range(ConstraintState.Invariant(a.Minimum), ConstraintState.Invariant(a.Maximum));
                    if (a.MinimumIsExclusive)
                        state.ExclusiveMinimum();
                    if (a.MaximumIsExclusive)
                        state.ExclusiveMaximum();
                    break;
                case RegularExpressionAttribute a:
                    state.RegularExpression(a.Pattern);
                    break;
                case EmailAddressAttribute:
                    state.Format(ConventionKind.Email);
                    break;
                case PhoneAttribute:
                    state.Format(ConventionKind.Phone);
                    break;
                case UrlAttribute:
                    state.Format(ConventionKind.Url);
                    break;
                case CreditCardAttribute:
                    state.Format(ConventionKind.CreditCardNumber);
                    break;
                case Base64StringAttribute:
                    state.Base64();
                    break;
                case RequiredAttribute:
                    state.Required();
                    break;
                case AllowedValuesAttribute a:
                    state.AllowedValues();
                    allowed = a.Values;
                    break;
                case DeniedValuesAttribute a:
                    state.DeniedValues();
                    denied = a.Values;
                    break;
            }
        }

        return state.Any
            ? new MemberConstraints
            {
                MinLength = state.MinLengthValue,
                MaxLength = state.MaxLengthValue,
                RangeMin = state.RangeMin,
                RangeMax = state.RangeMax,
                RangeMinExclusive = state.RangeMinExclusive,
                RangeMaxExclusive = state.RangeMaxExclusive,
                Pattern = state.Pattern,
                Format = state.FormatKind,
                Base64 = state.IsBase64,
                Required = state.IsRequired,
                AllowedValues = allowed,
                DeniedValues = denied,
            }
            : null;
    }

    /// <summary>Reads the annotations of a property or field, and for constructor parameters those of the matching property too.</summary>
    [RequiresUnreferencedCode("Reads attributes with reflection.")]
    internal static MemberConstraints? Read(ICustomAttributeProvider member, ICustomAttributeProvider? matchingProperty = null)
    {
        var attributes = AttributesOf(member);
        if (matchingProperty is not null)
            attributes = attributes.Concat(AttributesOf(matchingProperty));
        return From(attributes);
    }

    /// <summary>
    /// The attributes of <paramref name="provider"/> with those an override inherits, as the validator sees them: <see cref="MemberInfo.GetCustomAttributes(bool)"/>
    /// ignores <c>inherit</c> for properties, <see cref="Attribute.GetCustomAttributes(MemberInfo, bool)"/> walks the members it overrides.
    /// </summary>
    [RequiresUnreferencedCode("Reads attributes with reflection.")]
    private static IEnumerable<Attribute> AttributesOf(ICustomAttributeProvider provider) => provider switch
    {
        MemberInfo member => Attribute.GetCustomAttributes(member, inherit: true),
        ParameterInfo parameter => Attribute.GetCustomAttributes(parameter, inherit: true),
        _ => provider.GetCustomAttributes(inherit: true).OfType<Attribute>(),
    };
}
