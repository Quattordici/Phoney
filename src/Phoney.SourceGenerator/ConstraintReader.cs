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
        var builder = new Builder();
        foreach (var attribute in attributes)
            builder.Add(attribute);
        return builder.Build();
    }

    /// <summary>Collects the parts of the initializer, annotation by annotation.</summary>
    private sealed class Builder
    {
        private readonly Dictionary<string, string> _parts = new();
        private readonly List<string> _order = []; // the order the initializer is written in: first set, first written

        // Several length annotations all apply, so the strictest limits win, whatever order they are written in.
        private int? _minLength;
        private int? _maxLength;
        private string? _format;
        private bool _required;

        public void Add(AttributeData attribute)
        {
            var args = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case Ns + "StringLengthAttribute":
                    AddStringLength(attribute);
                    break;
                case Ns + "MaxLengthAttribute" when args.Length == 1 && args[0].Value is int max && max > 0:
                    Max(max);
                    break;
                case Ns + "MinLengthAttribute" when args.Length == 1 && args[0].Value is int min:
                    Min(min);
                    break;
                case Ns + "LengthAttribute" when args.Length == 2 && args[0].Value is int lower && args[1].Value is int upper:
                    Min(lower);
                    Max(upper);
                    break;
                case Ns + "RangeAttribute":
                    AddRange(attribute);
                    break;
                case Ns + "RegularExpressionAttribute" when args.Length == 1 && args[0].Value is string pattern:
                    Set("Pattern", Literal(pattern));
                    break;
                case Ns + "EmailAddressAttribute":
                    _format = "Email";
                    break;
                case Ns + "PhoneAttribute":
                    _format = "Phone";
                    break;
                case Ns + "UrlAttribute":
                    _format = "Url";
                    break;
                case Ns + "CreditCardAttribute":
                    _format = "CreditCardNumber";
                    break;
                case Ns + "Base64StringAttribute":
                    Set("Base64", "true");
                    break;
                case Ns + "RequiredAttribute":
                    _required = true;
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

        public Result Build()
        {
            if (_format is not null)
                Set("Format", "global::Phoney.Generation.ConventionKind." + _format);
            var initializer = _parts.Count == 0 ? null : string.Join(", ", _order.Select(key => $"{key} = {_parts[key]}"));
            var definesValue = _format is not null || _parts.ContainsKey("Pattern") || _parts.ContainsKey("RangeMin") || _parts.ContainsKey("AllowedValues") || _parts.ContainsKey("Base64");
            return new Result(initializer, _format, _required, definesValue);
        }

        private void AddStringLength(AttributeData attribute)
        {
            var args = attribute.ConstructorArguments;
            if (args.Length == 1 && args[0].Value is int max)
                Max(max);
            foreach (var named in attribute.NamedArguments.Where(n => n.Key == "MinimumLength" && n.Value.Value is int and > 0))
                Min((int)named.Value.Value!);
        }

        private void AddRange(AttributeData attribute)
        {
            // Two ints, two doubles, or a type and two strings: the bounds are the last two arguments.
            var args = attribute.ConstructorArguments;
            var bounds = args.Length == 3 ? args.Skip(1).ToArray() : args.ToArray();
            if (bounds.Length != 2)
                return;
            Set("RangeMin", Literal(Invariant(bounds[0].Value)));
            Set("RangeMax", Literal(Invariant(bounds[1].Value)));
            foreach (var named in attribute.NamedArguments.Where(n => n.Key is "MinimumIsExclusive" or "MaximumIsExclusive" && n.Value.Value is true))
                Set(named.Key == "MinimumIsExclusive" ? "RangeMinExclusive" : "RangeMaxExclusive", "true");
        }

        private void Set(string key, string value)
        {
            if (!_parts.ContainsKey(key))
                _order.Add(key);
            _parts[key] = value;
        }

        private void Min(int value)
        {
            _minLength = _minLength is { } current && current > value ? current : value;
            Set("MinLength", Int(_minLength.Value));
        }

        private void Max(int value)
        {
            _maxLength = _maxLength is { } current && current < value ? current : value;
            Set("MaxLength", Int(_maxLength.Value));
        }

        /// <summary><c>params object?[]</c> arguments as an <c>object[]</c> expression.</summary>
        private static string Values(System.Collections.Immutable.ImmutableArray<TypedConstant> args)
        {
            var items = args.Length == 1 && args[0].Kind == TypedConstantKind.Array ? args[0].Values : args;
            return "new object[] { " + string.Join(", ", items.Select(Constant)) + " }";
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Literal(string? value) =>
            value is null ? "null" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string Constant(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";
        if (constant.Type is { TypeKind: TypeKind.Enum } enumType)
            return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){Invariant(constant.Value)}";
        return constant.ToCSharpString();
    }

    private static string? Invariant(object? value) => value switch
    {
        null => null,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

}
