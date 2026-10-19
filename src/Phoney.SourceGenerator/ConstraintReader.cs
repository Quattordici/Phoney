using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reads data annotations from symbols into a C# object initializer for <c>MemberConstraints</c>, mirroring
/// <c>MemberConstraints.From</c> in the runtime so generated and reflection models enforce the same rules.
/// </summary>
internal static class ConstraintReader
{
    private const string Ns = "System.ComponentModel.DataAnnotations.";

    /// <summary>The result of reading a member's annotations.</summary>
    /// <param name="Initializer">Object initializer body (<c>MaxLength = 10, Required = true</c>), or <see langword="null"/> when there are no supported annotations.</param>
    /// <param name="Format">Convention kind name forced by a format annotation (<c>Email</c>…), if any.</param>
    /// <param name="Required">Whether the member is <c>[Required]</c>.</param>
    /// <param name="DefinesValue">Whether the annotations alone define the value (format, pattern, range, allowed values or Base64), like <c>MemberConstraints.DefinesValue</c>.</param>
    public readonly record struct Result(string? Initializer, string? Format, bool Required, bool DefinesValue);

    public static Result Read(IEnumerable<AttributeData> attributes)
    {
        var parts = new Dictionary<string, string>();
        var order = new List<string>(); // the order the initializer is written in: first set, first written
        void Set(string key, string value)
        {
            if (!parts.ContainsKey(key))
                order.Add(key);
            parts[key] = value;
        }

        string? format = null;
        var required = false;

        foreach (var attribute in attributes)
        {
            var args = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case Ns + "StringLengthAttribute":
                    if (args.Length == 1 && args[0].Value is int max)
                        Set("MaxLength", Int(max));
                    foreach (var named in attribute.NamedArguments)
                    {
                        if (named.Key == "MinimumLength" && named.Value.Value is int min && min > 0)
                            Set("MinLength", Int(min));
                    }

                    break;
                case Ns + "MaxLengthAttribute":
                    if (args.Length == 1 && args[0].Value is int maxLength && maxLength > 0)
                        Set("MaxLength", Int(maxLength));
                    break;
                case Ns + "MinLengthAttribute":
                    if (args.Length == 1 && args[0].Value is int minLength)
                        Set("MinLength", Int(minLength));
                    break;
                case Ns + "LengthAttribute":
                    if (args.Length == 2 && args[0].Value is int lower && args[1].Value is int upper)
                    {
                        Set("MinLength", Int(lower));
                        Set("MaxLength", Int(upper));
                    }

                    break;
                case Ns + "RangeAttribute":
                    // Range(int, int), Range(double, double) or Range(Type, string, string)
                    var bounds = args.Length == 3 ? args.Skip(1).ToArray() : args.ToArray();
                    if (bounds.Length == 2)
                    {
                        Set("RangeMin", Literal(Invariant(bounds[0].Value)));
                        Set("RangeMax", Literal(Invariant(bounds[1].Value)));
                        foreach (var named in attribute.NamedArguments)
                        {
                            if (named.Key is "MinimumIsExclusive" or "MaximumIsExclusive" && named.Value.Value is true)
                                Set(named.Key == "MinimumIsExclusive" ? "RangeMinExclusive" : "RangeMaxExclusive", "true");
                        }
                    }

                    break;
                case Ns + "RegularExpressionAttribute":
                    if (args.Length == 1 && args[0].Value is string pattern)
                        Set("Pattern", Literal(pattern));
                    break;
                case Ns + "EmailAddressAttribute":
                    format = "Email";
                    break;
                case Ns + "PhoneAttribute":
                    format = "Phone";
                    break;
                case Ns + "UrlAttribute":
                    format = "Url";
                    break;
                case Ns + "CreditCardAttribute":
                    format = "CreditCardNumber";
                    break;
                case Ns + "Base64StringAttribute":
                    Set("Base64", "true");
                    break;
                case Ns + "RequiredAttribute":
                    required = true;
                    Set("Required", "true");
                    break;
                case Ns + "AllowedValuesAttribute":
                    Set("AllowedValues", Values(args));
                    break;
                case Ns + "DeniedValuesAttribute":
                    Set("DeniedValues", Values(args));
                    break;
            }
        }

        if (format is not null)
            Set("Format", "global::Phoney.Generation.ConventionKind." + format);
        var initializer = parts.Count == 0 ? null : string.Join(", ", order.Select(key => $"{key} = {parts[key]}"));
        var definesValue = format is not null || parts.ContainsKey("Pattern") || parts.ContainsKey("RangeMin") || parts.ContainsKey("AllowedValues") || parts.ContainsKey("Base64");
        return new Result(initializer, format, required, definesValue);
    }

    /// <summary><c>params object?[]</c> arguments as an <c>object[]</c> expression.</summary>
    private static string Values(System.Collections.Immutable.ImmutableArray<TypedConstant> args)
    {
        var items = args.Length == 1 && args[0].Kind == TypedConstantKind.Array ? args[0].Values : args;
        return "new object[] { " + string.Join(", ", items.Select(Constant)) + " }";
    }

    private static string Constant(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";
        if (constant.Type is { TypeKind: TypeKind.Enum } enumType)
            return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Invariant(constant.Value)}";
        return constant.ToCSharpString();
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Invariant(object? value) => value switch
    {
        null => null,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Literal(string? value) =>
        value is null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
