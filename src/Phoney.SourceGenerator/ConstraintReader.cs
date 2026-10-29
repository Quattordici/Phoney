using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reads data annotations from symbols into a C# object initializer for <c>MemberConstraints</c>. It merges them with the same <c>ConstraintState</c> as
/// <c>MemberConstraints.Annotations</c> in the runtime, so generated and reflection models enforce the same rules.
/// </summary>
internal static class ConstraintReader
{
    private const string Ns = "System.ComponentModel.DataAnnotations.";

    /// <summary>The result of reading a member's annotations.</summary>
    /// <param name="Expression">
    /// The <c>MemberConstraints</c> expression: an object initializer (<c>new() { MaxLength = 10, Required = true }</c>), or, when an annotation derives from a supported one
    /// and its values are only known once it is constructed, <c>MemberConstraints.Of(new SkuAttribute(), …)</c>; <see langword="null"/> when there are no supported annotations.
    /// </param>
    /// <param name="Format">Convention kind forced by a format annotation (<c>Email</c>…), if any.</param>
    /// <param name="DataType">The <c>DataType</c> of a <c>[DataType]</c> annotation, as a number, if any.</param>
    /// <param name="Required">Whether the member is <c>[Required]</c>.</param>
    /// <param name="DefinesValue">Whether the annotations alone define the value, like <c>MemberConstraints.DefinesValue</c>.</param>
    public readonly record struct Result(string? Expression, ConventionKind? Format, int? DataType, bool Required, bool DefinesValue)
    {
        /// <summary>The convention of a member, like <c>MemberAnnotations.Convention</c>: the one the annotations declare, else the one the name gives.</summary>
        public ConventionKind Convention(string name, TypeCategory category, string ownerName) =>
            ConstraintState.Declared(Format, DataType, category) ?? ConventionRules.Match(name, category, ownerName);
    }

    /// <summary>The annotations the runtime reads, by full name (<c>[DataType]</c> is read separately, as a hint).</summary>
    private static readonly HashSet<string> Supported = new(new[]
    {
        "StringLengthAttribute", "MaxLengthAttribute", "MinLengthAttribute", "LengthAttribute", "RangeAttribute", "RegularExpressionAttribute", "EmailAddressAttribute",
        "PhoneAttribute", "UrlAttribute", "CreditCardAttribute", "Base64StringAttribute", "RequiredAttribute", "AllowedValuesAttribute", "DeniedValuesAttribute",
        "CompareAttribute", "EnumDataTypeAttribute", "FileExtensionsAttribute",
    }.Select(n => Ns + n));

    /// <summary>Reads a member's annotations.</summary>
    /// <param name="attributes">The member's attributes.</param>
    /// <param name="accessible">Whether generated code can use a symbol (to construct an annotation that derives from a supported one).</param>
    public static Result Read(IEnumerable<AttributeData> attributes, Func<ISymbol, bool> accessible)
    {
        var builder = new Builder(accessible);
        foreach (var attribute in attributes)
            builder.Add(attribute);
        return builder.Build();
    }

    /// <summary>Reads the annotations into a <see cref="ConstraintState"/> and writes what it holds as the parts of the initializer.</summary>
    private sealed class Builder(Func<ISymbol, bool> accessible)
    {
        private readonly ConstraintState _state = new();
        private readonly List<AttributeData> _read = [];
        private bool _derived;
        private string? _allowed;
        private string? _denied;
        private string? _enumType;

        public void Add(AttributeData attribute)
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            if (name is not null && Supported.Contains(name))
                _read.Add(attribute);
            else if (SupportedBase(attribute.AttributeClass) is { } supported && Constructible(attribute))
            {
                // Its values come from its constructor, so the generated model constructs it, as reflection does; what it is (a pattern, a format…) is known now.
                _read.Add(attribute);
                _derived = true;
                Declare(supported);
                return;
            }

            var args = attribute.ConstructorArguments;
            switch (name)
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
                case Ns + "CompareAttribute" when args.Length == 1 && args[0].Value is string other:
                    _state.Compare(other);
                    break;
                case Ns + "EnumDataTypeAttribute" when args.Length == 1 && args[0].Value is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType:
                    _state.EnumDataType();
                    _enumType = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    break;
                case Ns + "FileExtensionsAttribute":
                    _state.FileExtensions(attribute.NamedArguments.Where(n => n.Key == "Extensions").Select(n => n.Value.Value as string).LastOrDefault());
                    break;
                case Ns + "DataTypeAttribute" when args.Length == 1 && args[0].Value is int dataType and not 0: // DataType.Custom names its type as text
                    _state.DataType(dataType);
                    break;
            }
        }

        public Result Build()
        {
            var parts = _state.SetKeys.Select(Part).Where(p => p is not null).Select(p => p!).ToList();
            if (_state.FormatKind is { } format)
                parts.Add("Format = global::Phoney.Generation.ConventionKind." + format);
            string? expression = null;
            if (_derived)
                expression = "global::Phoney.Generation.MemberConstraints.Of(" + string.Join(", ", _read.Select(Construct)) + ")";
            else if (parts.Count > 0)
                expression = "new() { " + string.Join(", ", parts) + " }";
            return new Result(expression, _state.FormatKind, _state.DataTypeValue, _state.IsRequired, _state.DefinesValue);
        }

        /// <summary>The supported annotation <paramref name="type"/> derives from, or <see langword="null"/>.</summary>
        private static string? SupportedBase(INamedTypeSymbol? type)
        {
            for (var t = type?.BaseType; t is not null; t = t.BaseType)
            {
                if (t.ToDisplayString() is var name && Supported.Contains(name))
                    return name;
            }

            return null;
        }

        /// <summary>Whether generated code can construct the annotation as it is written.</summary>
        private bool Constructible(AttributeData attribute) =>
            attribute.AttributeClass is { } type && accessible(type) && attribute.AttributeConstructor is { } constructor && accessible(constructor)
            && attribute.NamedArguments.All(n => Settable(type, n.Key) is { } member && accessible(member));

        /// <summary>The setter or field a named argument sets, declared on the attribute or a base class.</summary>
        private static ISymbol? Settable(INamedTypeSymbol type, string name)
        {
            for (var t = type; t is not null; t = t.BaseType)
            {
                switch (t.GetMembers(name).FirstOrDefault())
                {
                    case IPropertySymbol property:
                        return property.SetMethod;
                    case IFieldSymbol field:
                        return field;
                }
            }

            return null;
        }

        /// <summary>
        /// What an annotation derived from <paramref name="supported"/> tells the generator before it is constructed: the format it forces, whether it is required and whether it
        /// defines the value. The values themselves (the pattern, the limits…) are read when the generated model constructs it.
        /// </summary>
        private void Declare(string supported)
        {
            switch (supported.Substring(Ns.Length))
            {
                case "RegularExpressionAttribute":
                    _state.RegularExpression("");
                    break;
                case "RangeAttribute":
                    _state.Range("", "");
                    break;
                case "EmailAddressAttribute":
                    _state.Format(ConventionKind.Email);
                    break;
                case "PhoneAttribute":
                    _state.Format(ConventionKind.Phone);
                    break;
                case "UrlAttribute":
                    _state.Format(ConventionKind.Url);
                    break;
                case "CreditCardAttribute":
                    _state.Format(ConventionKind.CreditCardNumber);
                    break;
                case "Base64StringAttribute":
                    _state.Base64();
                    break;
                case "RequiredAttribute":
                    _state.Required();
                    break;
                case "AllowedValuesAttribute":
                    _state.AllowedValues();
                    break;
                case "CompareAttribute":
                    _state.Compare("");
                    break;
                case "EnumDataTypeAttribute":
                    _state.EnumDataType();
                    break;
                case "FileExtensionsAttribute":
                    _state.FileExtensions(null);
                    break;
            }
        }

        /// <summary>The attribute as a <c>new</c> expression with its arguments.</summary>
        private static string Construct(AttributeData attribute)
        {
            var arguments = string.Join(", ", attribute.ConstructorArguments.Select(Expression));
            var named = attribute.NamedArguments.Select(n => n.Key + " = " + Expression(n.Value)).ToList();
            return $"new {attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({arguments})" + (named.Count == 0 ? "" : " { " + string.Join(", ", named) + " }");
        }

        /// <summary>An attribute argument as C#: a constant, an enum value, <c>typeof</c> or an array.</summary>
        private static string Expression(TypedConstant constant) => constant.Kind switch
        {
            _ when constant.IsNull => constant.Type is { } type ? $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})null" : "null",
            TypedConstantKind.Type => $"typeof({((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})",
            TypedConstantKind.Array => $"new {((IArrayTypeSymbol)constant.Type!).ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}[] {{ {string.Join(", ", constant.Values.Select(Expression))} }}",
            _ => Constant(constant),
        };

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
            "Compare" => $"Compare = {Literal(_state.CompareWith)}",
            "EnumType" => $"EnumType = typeof({_enumType})",
            "FileExtensions" => $"FileExtensions = new string[] {{ {string.Join(", ", _state.Extensions!.Select(Literal))} }}",
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
