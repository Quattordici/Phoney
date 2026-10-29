#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

// This file is compiled into the runtime and into the source generator (see Phoney.SourceGenerator.csproj), so both merge data annotations by the same rules.
// Keep it free of APIs newer than netstandard2.0.
namespace Phoney.Generation;

/// <summary>
/// What the data annotations of a member add up to. The reflection model reads annotations as attribute instances and the source generator as attribute data; both
/// tell this class what they found and read the merged result, so the rules (the strictest length wins, which annotations define the value) are written once.
/// </summary>
internal sealed class ConstraintState
{
    private readonly List<string> _keys = [];

    /// <summary>The names of the constraints that were set, in the order each was first set.</summary>
    public IReadOnlyList<string> SetKeys => _keys;

    /// <summary>Whether any supported annotation was read.</summary>
    public bool Any => _keys.Count > 0 || FormatKind is not null;

    /// <summary>The merged minimum length (or element count).</summary>
    public int? MinLengthValue { get; private set; }

    /// <summary>The merged maximum length (or element count).</summary>
    public int? MaxLengthValue { get; private set; }

    /// <summary>The lower bound of a range, as invariant text.</summary>
    public string? RangeMin { get; private set; }

    /// <summary>The upper bound of a range, as invariant text.</summary>
    public string? RangeMax { get; private set; }

    /// <summary>Whether the lower bound itself is outside the range.</summary>
    public bool RangeMinExclusive { get; private set; }

    /// <summary>Whether the upper bound itself is outside the range.</summary>
    public bool RangeMaxExclusive { get; private set; }

    /// <summary>The pattern of a regular expression.</summary>
    public string? Pattern { get; private set; }

    /// <summary>The format forced by <c>[EmailAddress]</c>, <c>[Phone]</c>, <c>[Url]</c> or <c>[CreditCard]</c>.</summary>
    public ConventionKind? FormatKind { get; private set; }

    /// <summary>Whether the value is base64 text.</summary>
    public bool IsBase64 { get; private set; }

    /// <summary>Whether the member is required.</summary>
    public bool IsRequired { get; private set; }

    /// <summary>Whether the value is one of a list of allowed values.</summary>
    public bool HasAllowedValues { get; private set; }

    /// <summary>Whether a list of denied values was given.</summary>
    public bool HasDeniedValues { get; private set; }

    /// <summary>The member that <c>[Compare]</c> names: this member repeats its value.</summary>
    public string? CompareWith { get; private set; }

    /// <summary>Whether <c>[EnumDataType]</c> was read (the enum type itself stays with the reader that has it).</summary>
    public bool HasEnumType { get; private set; }

    /// <summary>The extensions of <c>[FileExtensions]</c>, lower case and without dots, as the validator compares them.</summary>
    public IReadOnlyList<string>? Extensions { get; private set; }

    /// <summary>The <c>DataType</c> value of a <c>[DataType]</c> annotation, a hint about what the value is that validates nothing.</summary>
    public int? DataTypeValue { get; private set; }

    /// <summary>Whether the annotations alone define the value (format, pattern, range, allowed values, base64, enum names, file extensions or a comparison), which satisfies strict mode.</summary>
    public bool DefinesValue => Defines(FormatKind is not null, Pattern is not null, RangeMin is not null, HasAllowedValues || HasEnumType, IsBase64 || Extensions is not null || CompareWith is not null);

    /// <summary>The one rule for <see cref="DefinesValue"/>, for readers that hold the merged constraints in another shape.</summary>
    public static bool Defines(bool hasFormat, bool hasPattern, bool hasRange, bool hasAllowedValues, bool isGenerated) =>
        hasFormat || hasPattern || hasRange || hasAllowedValues || isGenerated;

    /// <summary>
    /// The convention the annotations declare for a member of <paramref name="category"/>: a format annotation, else what a <c>[DataType]</c> describes;
    /// <see langword="null"/> leaves it to the member's name.
    /// </summary>
    public ConventionKind? DeclaredConvention(TypeCategory category) => Declared(FormatKind, DataTypeValue, category);

    /// <inheritdoc cref="DeclaredConvention"/>
    public static ConventionKind? Declared(ConventionKind? format, int? dataType, TypeCategory category)
    {
        if (format is not null)
            return format;
        var text = category == TypeCategory.String;
        // The values of System.ComponentModel.DataAnnotations.DataType, which the source generator can't reference.
        return dataType switch
        {
            5 when text => ConventionKind.Phone,
            6 when ConventionRules.IsNumber(category) => ConventionKind.Price,
            9 when text => ConventionKind.Paragraph,
            10 when text => ConventionKind.Email,
            11 when text => ConventionKind.Password,
            12 when text || category == TypeCategory.Uri => ConventionKind.Url,
            13 when text || category == TypeCategory.Uri => ConventionKind.ImageUrl,
            14 when text => ConventionKind.CreditCardNumber,
            15 when text => ConventionKind.ZipCode,
            16 when text => ConventionKind.FileName,
            _ => null,
        };
    }

    /// <summary>A maximum length; several maximums all apply, so the smallest wins, whatever order they come in.</summary>
    public void MaxLength(int value)
    {
        MaxLengthValue = MaxLengthValue is { } current && current < value ? current : value;
        Touch("MaxLength");
    }

    /// <summary>A minimum length; several minimums all apply, so the largest wins.</summary>
    public void MinLength(int value)
    {
        MinLengthValue = MinLengthValue is { } current && current > value ? current : value;
        Touch("MinLength");
    }

    /// <summary><c>[StringLength(maximum, MinimumLength = minimum)]</c>: always a maximum, a minimum only when it is above zero.</summary>
    public void StringLength(int maximum, int minimum)
    {
        MaxLength(maximum);
        if (minimum > 0)
            MinLength(minimum);
    }

    /// <summary><c>[MaxLength(length)]</c>: a length of zero or less means no limit and is ignored.</summary>
    public void MaxLengthAnnotation(int length)
    {
        if (length > 0)
            MaxLength(length);
    }

    /// <summary><c>[Length(minimum, maximum)]</c>.</summary>
    public void Length(int minimum, int maximum)
    {
        MinLength(minimum);
        MaxLength(maximum);
    }

    /// <summary><c>[Range]</c> bounds; a later range replaces an earlier one.</summary>
    public void Range(string? minimum, string? maximum)
    {
        RangeMin = minimum;
        RangeMax = maximum;
        RangeMinExclusive = false;
        RangeMaxExclusive = false;
        Touch("RangeMin");
        Touch("RangeMax");
    }

    /// <summary><c>[Range(MinimumIsExclusive = true)]</c>.</summary>
    public void ExclusiveMinimum()
    {
        RangeMinExclusive = true;
        Touch("RangeMinExclusive");
    }

    /// <summary><c>[Range(MaximumIsExclusive = true)]</c>.</summary>
    public void ExclusiveMaximum()
    {
        RangeMaxExclusive = true;
        Touch("RangeMaxExclusive");
    }

    /// <summary><c>[RegularExpression]</c>.</summary>
    public void RegularExpression(string pattern)
    {
        Pattern = pattern;
        Touch("Pattern");
    }

    /// <summary>A format annotation; it overrides the name convention. The last one wins. Written after all other constraints.</summary>
    public void Format(ConventionKind kind) => FormatKind = kind;

    /// <summary><c>[Base64String]</c>.</summary>
    public void Base64()
    {
        IsBase64 = true;
        Touch("Base64");
    }

    /// <summary><c>[Required]</c>.</summary>
    public void Required()
    {
        IsRequired = true;
        Touch("Required");
    }

    /// <summary><c>[AllowedValues]</c> was read (the values themselves stay with the reader that has them).</summary>
    public void AllowedValues()
    {
        HasAllowedValues = true;
        Touch("AllowedValues");
    }

    /// <summary><c>[DeniedValues]</c> was read.</summary>
    public void DeniedValues()
    {
        HasDeniedValues = true;
        Touch("DeniedValues");
    }

    /// <summary><c>[Compare(otherMember)]</c>.</summary>
    public void Compare(string otherMember)
    {
        CompareWith = otherMember;
        Touch("Compare");
    }

    /// <summary><c>[EnumDataType]</c>.</summary>
    public void EnumDataType()
    {
        HasEnumType = true;
        Touch("EnumType");
    }

    /// <summary>
    /// <c>[FileExtensions(Extensions = …)]</c>: the list is read as the validator reads it (spaces and dots dropped, lower case, <c>png,jpg,jpeg,gif</c> when empty).
    /// </summary>
    public void FileExtensions(string? extensions)
    {
        var list = string.IsNullOrWhiteSpace(extensions) ? "png,jpg,jpeg,gif" : extensions!;
        Extensions = list.Replace(" ", "").Replace(".", "").ToLowerInvariant().Split([','], StringSplitOptions.RemoveEmptyEntries);
        Touch("FileExtensions");
    }

    /// <summary><c>[DataType(dataType)]</c>: a hint, so it sets no constraint (see <see cref="DeclaredConvention"/>).</summary>
    public void DataType(int dataType) => DataTypeValue = dataType;

    /// <summary>A bound or value as invariant text, so generated and reflected constraints read the same whatever the culture.</summary>
    public static string? Invariant(object? value) => value switch
    {
        null => null,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private void Touch(string key)
    {
        if (!_keys.Contains(key))
            _keys.Add(key);
    }
}
