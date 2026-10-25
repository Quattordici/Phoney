using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reads data annotations from symbols into a C# object initializer for <c>MemberConstraints</c>. It merges them with the same <c>ConstraintState</c> as
/// <c>MemberConstraints.From</c> in the runtime, so generated and reflection models enforce the same rules.
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

    /// <summary>Reads the annotations into a <see cref="ConstraintState"/> and writes what it holds as the parts of the initializer.</summary>
    private sealed class Builder
    {
        private readonly ConstraintState _state = new();
        private string? _allowed;
        private string? _denied;

        public void Add(AttributeData attribute)
        {
            var args = attribute.ConstructorArguments;
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                case Ns + "StringLengthAttribute":
                    AddStringLength(attribute);
                    break;
                case Ns + "MaxLengthAttribute" when args.Length == 1 && args[0].Value is int max:
                    _state.MaxLengthAnnotation(max);
                    break;
                case Ns + "MinLengthAttribute" when args.Length == 1 && args[0].Value is int min:
                    _state.MinLength(min);
                    break;
                case Ns + "LengthAttribute" when args.Length == 2 && args[0].Value is int lower && args[1].Value is int upper:
                    _state.Length(lower, upper);
                    break;
                case Ns + "RangeAttribute":
                    AddRange(attribute);
                    break;
                case Ns + "RegularExpressionAttribute" when args.Length == 1 && args[0].Value is string pattern:
                    _state.RegularExpression(pattern);
                    break;
                case Ns + "EmailAddressAttribute":
                    _state.Format(ConventionKind.Email);
                    break;
                case Ns + "PhoneAttribute":
                    _state.Format(ConventionKind.Phone);
                    break;
                case Ns + "UrlAttribute":
                    _state.Format(ConventionKind.Url);
                    break;
                case Ns + "CreditCardAttribute":
                    _state.Format(ConventionKind.CreditCardNumber);
                    break;
                case Ns + "Base64StringAttribute":
                    _state.Base64();
                    break;
                case Ns + "RequiredAttribute":
                    _state.Required();
                    break;
                case Ns + "AllowedValuesAttribute":
                    _state.AllowedValues();
                    _allowed = Values(args);
                    break;
                case Ns + "DeniedValuesAttribute":
                    _state.DeniedValues();
                    _denied = Values(args);
                    break;
            }
        }

        public Result Build()
        {
            var parts = _state.SetKeys.Select(Part).Where(p => p is not null).Select(p => p!).ToList();
            if (_state.FormatKind is { } format)
                parts.Add("Format = global::Phoney.Generation.ConventionKind." + format);
            return new Result(parts.Count == 0 ? null : string.Join(", ", parts), _state.FormatKind?.ToString(), _state.IsRequired, _state.DefinesValue);
        }

        /// <summary>The initializer part for a constraint that was set, or <see langword="null"/> for a flag that was set and then cleared.</summary>
        private string? Part(string key) => key switch
        {
            "MinLength" => $"MinLength = {Int(_state.MinLengthValue!.Value)}",
            "MaxLength" => $"MaxLength = {Int(_state.MaxLengthValue!.Value)}",
            "RangeMin" => $"RangeMin = {Literal(_state.RangeMin)}",
            "RangeMax" => $"RangeMax = {Literal(_state.RangeMax)}",
            "RangeMinExclusive" => _state.RangeMinExclusive ? "RangeMinExclusive = true" : null,
            "RangeMaxExclusive" => _state.RangeMaxExclusive ? "RangeMaxExclusive = true" : null,
            "Pattern" => $"Pattern = {Literal(_state.Pattern)}",
            "Base64" => "Base64 = true",
            "Required" => "Required = true",
            "AllowedValues" => $"AllowedValues = {_allowed}",
            "DeniedValues" => $"DeniedValues = {_denied}",
            _ => null,
        };

        private void AddStringLength(AttributeData attribute)
        {
            var args = attribute.ConstructorArguments;
            if (args.Length != 1 || args[0].Value is not int max)
                return;
            var minimum = attribute.NamedArguments.Where(n => n.Key == "MinimumLength" && n.Value.Value is int).Select(n => (int)n.Value.Value!).LastOrDefault();
            _state.StringLength(max, minimum);
        }

        private void AddRange(AttributeData attribute)
        {
            // Two ints, two doubles, or a type and two strings: the bounds are the last two arguments.
            var args = attribute.ConstructorArguments;
            var bounds = args.Length == 3 ? args.Skip(1).ToArray() : args.ToArray();
            if (bounds.Length != 2)
                return;
            _state.Range(ConstraintState.Invariant(bounds[0].Value), ConstraintState.Invariant(bounds[1].Value));
            foreach (var named in attribute.NamedArguments.Where(n => n.Key is "MinimumIsExclusive" or "MaximumIsExclusive" && n.Value.Value is true))
            {
                if (named.Key == "MinimumIsExclusive")
                    _state.ExclusiveMinimum();
                else
                    _state.ExclusiveMaximum();
            }
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

        private static string Constant(TypedConstant constant)
        {
            if (constant.IsNull)
                return "null";
            if (constant.Type is { TypeKind: TypeKind.Enum } enumType)
                return $"({enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){ConstraintState.Invariant(constant.Value)}";
            return constant.ToCSharpString();
        }
    }
}
