using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Phoney.Generation;

namespace Phoney.SourceGenerator;

/// <summary>Everything the emitter needs to write the model of one type.</summary>
internal sealed class TypeModel
{
    public TypeModel(string fullName, string key, bool isValueType, bool hasConstructor, List<MemberModel> members)
    {
        FullName = fullName;
        Key = key;
        IsValueType = isValueType;
        HasConstructor = hasConstructor;
        Members = members;
    }

    /// <summary>Fully qualified name, e.g. <c>global::Shop.Customer</c>.</summary>
    public string FullName { get; }

    /// <summary>Identifier-safe unique name used for the generated model class and field.</summary>
    public string Key { get; }

    /// <summary>Whether the type is a struct (init-only accessors then take it by <c>ref</c>).</summary>
    public bool IsValueType { get; }

    /// <summary>False for structs created with <c>default</c> semantics (no public constructor).</summary>
    public bool HasConstructor { get; }

    /// <summary>Constructor parameters first, then writable members; the index is the member id.</summary>
    public List<MemberModel> Members { get; }
}

/// <summary>A populated member of a <see cref="TypeModel"/>.</summary>
internal sealed class MemberModel
{
    /// <summary>Member name; constructor parameters use the matching property's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Fully qualified member type.</summary>
    public string TypeName { get; set; } = "";

    /// <summary>Name of the <c>ConventionKind</c> chosen for the member.</summary>
    public string Convention { get; set; } = "None";

    /// <summary>Whether the value is passed to the constructor.</summary>
    public bool IsConstructorParameter { get; set; }

    /// <summary>Whether the member has an <c>init</c> accessor.</summary>
    public bool IsInitOnly { get; set; }

    /// <summary>Must be assigned in the object initializer (init-only or <c>required</c>).</summary>
    public bool InInitializer { get; set; }

    /// <summary>C# expression producing the value from a <c>FakeScope s0</c>.</summary>
    public string Value { get; set; } = "";

    /// <summary>Type declaring the member; init-only members are set later through an <c>UnsafeAccessor</c> on it.</summary>
    public string DeclaringType { get; set; } = "";

    /// <summary>Whether the member is a field rather than a property.</summary>
    public bool IsField { get; set; }

    /// <summary>Whether the member can be set on an existing object: it has a public setter or <c>init</c> accessor, also behind a constructor parameter.</summary>
    public bool Settable { get; set; }

    /// <summary>Whether that accessor is <c>init</c>, which is reached through an <c>UnsafeAccessor</c> after construction.</summary>
    public bool SetterInitOnly { get; set; }

    /// <summary>Type declaring the member that is set on an existing object.</summary>
    public string SetterDeclaringType { get; set; } = "";

    /// <summary>Whether the member is an object the generator creates and populates recursively.</summary>
    public bool IsNested { get; set; }

    /// <summary>Whether the member is a read-only collection (getter only, private setter, readonly field) whose generated elements are added to the collection the object holds.</summary>
    public bool FillsExisting { get; set; }

    /// <summary>The type arguments of a <see cref="FillsExisting"/> collection (<c>string</c> or <c>string, int</c>), for the typed <c>AddAll</c> helper.</summary>
    public string FillTypeArguments { get; set; } = "";

    /// <summary>Object initializer body for the member's <c>MemberConstraints</c> (from data annotations), or <see langword="null"/>.</summary>
    public string? Constraints { get; set; }

    /// <summary>Name of the static field holding those constraints, or <see langword="null"/>.</summary>
    public string? ConstraintsField { get; set; }

    /// <summary>Fully qualified member type without nullable annotation, for matching <c>WithConvention&lt;TValue&gt;</c>.</summary>
    public ITypeSymbol? Type { get; set; }

    /// <summary>Whether the member holds a scalar value (string, number, date…), like <c>FakeMember.IsScalar</c>.</summary>
    public bool IsScalar { get; set; }

    /// <summary>Whether data annotations alone define the value, like <c>MemberConstraints.DefinesValue</c>.</summary>
    public bool DefinesValue { get; set; }

    /// <summary>C# expression of an optional constructor parameter's declared default, like <c>FakeMember.DefaultValue</c>, or <see langword="null"/>.</summary>
    public string? DefaultValue { get; set; }
}

/// <summary>
/// Walks the requested types and everything they contain, mirroring the runtime reflection model: constructor
/// choice, member order (base class first, declaration order) and value production are the same, so seeded
/// output does not depend on whether a model was generated.
/// </summary>
internal sealed class ModelBuilder
{
    private readonly Compilation _compilation;
    private readonly ISymbol _owner;
    private readonly string _ownerName;
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly Queue<INamedTypeSymbol> _queue = new();
    private readonly HashSet<INamedTypeSymbol> _seen = new(SymbolEqualityComparer.Default);

    public ModelBuilder(Compilation compilation, INamedTypeSymbol owner, List<DiagnosticInfo> diagnostics, LocationInfo location)
        : this(compilation, owner, owner.Name, diagnostics, location)
    {
    }

    /// <summary>Builds models that must be accessible within <paramref name="owner"/>: a type, or an assembly for models emitted at its top level.</summary>
    public ModelBuilder(Compilation compilation, ISymbol owner, string ownerName, List<DiagnosticInfo> diagnostics, LocationInfo location)
    {
        _compilation = compilation;
        _owner = owner;
        _ownerName = ownerName;
        _diagnostics = diagnostics;
        DiagnosticLocation = location;
    }

    /// <summary>Where diagnostics for the types built next are reported.</summary>
    public LocationInfo DiagnosticLocation { get; set; }

    /// <summary>Models built so far, requested types first.</summary>
    public List<TypeModel> Models { get; } = new();

    /// <summary>
    /// Interfaces and abstract classes met as member types with the <c>[JsonDerivedType]</c> types listed on them
    /// (fully qualified, sorted), registered at startup as their default subtypes.
    /// </summary>
    public SortedDictionary<string, List<string>> DerivedTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The members Phoney populates on <paramref name="type"/>, as the generator and the reflection model see them, or
    /// <see langword="null"/> when the type cannot be generated. Used by analyzers.
    /// </summary>
    public static List<MemberModel>? DescribeMembers(Compilation compilation, INamedTypeSymbol type) =>
        new ModelBuilder(compilation, type, new List<DiagnosticInfo>(), LocationInfo.From(Location.None)).Build(type)?.Members;

    /// <summary>Schedules a type for model generation (once).</summary>
    public void Enqueue(INamedTypeSymbol type)
    {
        if (_seen.Add(type))
            _queue.Enqueue(type);
    }

    /// <summary>Builds models for all queued types, including types discovered while building.</summary>
    public void BuildAll(CancellationToken cancellationToken)
    {
        while (_queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = _queue.Dequeue();
            var model = Build(type);
            if (model is not null)
                Models.Add(model);
        }
    }

    /// <summary>Analyzes one type; returns <see langword="null"/> (with a diagnostic) when it cannot be generated.</summary>
    private TypeModel? Build(INamedTypeSymbol type)
    {
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return Skip(type, "only concrete classes, records and structs can be generated");
        if (!_compilation.IsSymbolAccessibleWithin(type, _owner))
            return Skip(type, $"it is not accessible from '{_ownerName}'");

        var writable = WritableMembers(type);
        var constructor = ChooseConstructor(type);
        if (constructor is null && type.TypeKind == TypeKind.Class)
            return Skip(type, "it has no public constructor");

        var members = new List<MemberModel>();
        foreach (var parameter in constructor?.Parameters ?? ImmutableEmpty())
            members.Add(ParameterMember(type, parameter, writable, members.Count));

        foreach (var member in writable)
        {
            if (WritableMember(type, member, members.Count) is { } writableMember)
                members.Add(writableMember);
        }

        // Read-only collections come last (their elements are drawn after everything else), as in the reflection model.
        var taken = new HashSet<string>(members.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var member in FilledMembers(type).Where(m => taken.Add(m.Name)))
            members.Add(FilledMember(type, member, members.Count));

        var key = FakeForGenerator.Sanitize(type.ToDisplayString());
        return new TypeModel(fullName, key, type.IsValueType, constructor is not null, members);
    }

    /// <summary>Describes a constructor parameter and takes the writable member it initializes out of <paramref name="writable"/>.</summary>
    private MemberModel ParameterMember(INamedTypeSymbol type, IParameterSymbol parameter, List<ISymbol> writable, int index)
    {
        var match = writable.FirstOrDefault(m => string.Equals(m.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            writable.Remove(match);
        var name = match?.Name ?? Capitalize(parameter.Name);
        // Annotations may sit on the parameter or (with [property: …]) on the generated property.
        var attributes = parameter.GetAttributes().Concat(match is null ? Enumerable.Empty<AttributeData>() : AttributesOf(match));
        var member = Member(type, new MemberSource(name, parameter.Type, IsNullableReference(parameter.Type, parameter.NullableAnnotation), type, IsField: false), MemberRoles.ConstructorParameter, attributes, index);
        if (parameter.HasExplicitDefaultValue)
            member.DefaultValue = DefaultLiteral(parameter.Type, parameter.ExplicitDefaultValue);
        // A record's positional property behind the parameter can be set on an existing object.
        if (match is IPropertySymbol property)
        {
            member.Settable = true;
            member.SetterInitOnly = property.SetMethod!.IsInitOnly;
            member.SetterDeclaringType = Fq(property.ContainingType);
        }
        else if (match is IFieldSymbol field)
        {
            member.Settable = true;
            member.SetterDeclaringType = Fq(field.ContainingType);
        }

        return member;
    }

    /// <summary>
    /// A parameter's default as an expression of its type. Metadata keeps <c>default</c> of a struct as <see langword="null"/> and an enum as its number,
    /// so both are cast to the type, the same values the reflection model reads from <c>ParameterInfo.DefaultValue</c>.
    /// </summary>
    private static string DefaultLiteral(ITypeSymbol type, object? value)
    {
        if (value is null)
            return $"default({Fq(type)})";
        var literal = value switch
        {
            double d when double.IsNaN(d) => "double.NaN",
            double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
            double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            float f when float.IsNaN(f) => "float.NaN",
            float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
            float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
            _ => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false),
        };
        return $"({Fq(type)})({literal})";
    }

    /// <summary>Describes a settable or init-only property or field.</summary>
    private MemberModel? WritableMember(INamedTypeSymbol type, ISymbol member, int index)
    {
        if (member is IPropertySymbol p)
        {
            var initOnly = p.SetMethod!.IsInitOnly;
            var role = MemberRoles.None;
            if (initOnly)
                role |= MemberRoles.InitOnly;
            if (initOnly || p.IsRequired)
                role |= MemberRoles.InInitializer;
            return Member(type, new MemberSource(p.Name, p.Type, IsNullableReference(p.Type, p.NullableAnnotation), p.ContainingType, IsField: false), role, AttributesOf(p), index);
        }

        if (member is not IFieldSymbol f)
            return null;
        var fieldRole = f.IsRequired ? MemberRoles.InInitializer : MemberRoles.None;
        return Member(type, new MemberSource(f.Name, f.Type, IsNullableReference(f.Type, f.NullableAnnotation), f.ContainingType, IsField: true), fieldRole, f.GetAttributes(), index);
    }

    /// <summary>Describes a read-only collection that is filled in place.</summary>
    private MemberModel FilledMember(INamedTypeSymbol type, ISymbol member, int index)
    {
        var (memberType, annotation) = member is IPropertySymbol property ? (property.Type, property.NullableAnnotation) : (((IFieldSymbol)member).Type, ((IFieldSymbol)member).NullableAnnotation);
        var filled = Member(type, new MemberSource(member.Name, memberType, IsNullableReference(memberType, annotation), member.ContainingType, member is IFieldSymbol), MemberRoles.None, AttributesOf(member), index);
        filled.Settable = false;
        filled.FillsExisting = true;
        filled.FillTypeArguments = string.Join(", ", ((INamedTypeSymbol)memberType).TypeArguments.Select(Fq));
        return filled;
    }

    /// <summary>No constructor parameters.</summary>
    private static IEnumerable<IParameterSymbol> ImmutableEmpty() => Array.Empty<IParameterSymbol>();

    /// <summary>Reports PHONEY002 for a type that cannot be generated.</summary>
    private TypeModel? Skip(INamedTypeSymbol type, string reason)
    {
        _diagnostics.Add(new DiagnosticInfo(Diagnostics.CannotGenerate, DiagnosticLocation, type.ToDisplayString(), reason));
        return null;
    }

    /// <summary>What a member is: its name, type and nullability, and where it is declared.</summary>
    private readonly record struct MemberSource(string Name, ITypeSymbol Type, bool NullableReference, INamedTypeSymbol DeclaringType, bool IsField);

    /// <summary>How a member is set.</summary>
    [Flags]
    private enum MemberRoles
    {
        /// <summary>A settable member, set after construction.</summary>
        None = 0,

        /// <summary>A constructor parameter.</summary>
        ConstructorParameter = 1,

        /// <summary>An init-only property.</summary>
        InitOnly = 2,

        /// <summary>Set in the object initializer (init-only or <c>required</c>).</summary>
        InInitializer = 4,
    }

    /// <summary>Describes a member and computes its value expression.</summary>
    private MemberModel Member(INamedTypeSymbol owner, MemberSource source, MemberRoles role, IEnumerable<AttributeData> attributes, int index)
    {
        var (name, type, nullableReference, declaringType, isField) = source;
        var isConstructorParameter = (role & MemberRoles.ConstructorParameter) != 0;
        var isInitOnly = (role & MemberRoles.InitOnly) != 0;
        var inInitializer = (role & MemberRoles.InInitializer) != 0;
        // Same rules as the reflection model: a format annotation or a [DataType] overrides the name convention, [Required] is never null.
        var constraints = ConstraintReader.Read(attributes, symbol => _compilation.IsSymbolAccessibleWithin(symbol, _compilation.Assembly));
        var convention = constraints.Convention(name, ConventionCategory(type), owner.Name);
        var constraintsField = constraints.Expression is null ? null : "__c" + index;
        return new MemberModel
        {
            Name = name,
            TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Convention = convention.ToString(),
            IsConstructorParameter = isConstructorParameter,
            IsInitOnly = isInitOnly,
            InInitializer = inInitializer,
            Value = Validated(type, constraintsField, constraints.HasValidators, Value(type, name, convention, nullableReference && !constraints.Required, 0, constraintsField, constraints.Required)),
            DeclaringType = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsField = isField,
            Constraints = constraints.Expression,
            ConstraintsField = constraintsField,
            Settable = !isConstructorParameter,
            SetterInitOnly = isInitOnly,
            SetterDeclaringType = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Type = type,
            IsScalar = Category(type) != TypeCategory.Other || AdapterCall(Unwrap(type)) is not null,
            DefinesValue = constraints.DefinesValue,
            IsNested = ValueShapes.IsNested(ValueShapes.Of(Unwrap(type))),
        };
    }

    /// <summary>The member's value, drawn again while a validation attribute Phoney has no generator for rejects it; mirrors <c>ReflectionTypeModel.Producer</c>.</summary>
    private static string Validated(ITypeSymbol type, string? constraints, bool hasValidators, string value) =>
        hasValidators && constraints is not null
            ? $"global::Phoney.Generation.ConventionValues.Validated<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>(s0, {constraints}, static s0 => {value})"
            : value;

    /// <summary>The value expression for a member of <paramref name="type"/>; mirrors <c>ReflectionTypeModel.Producer</c>.</summary>
    private string Value(ITypeSymbol type, string memberName, ConventionKind kind, bool nullableReference, int depth, string? constraints = null, bool required = false)
    {
        var s = "s" + depth;
        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = NonNull(nullable.TypeArguments[0], memberName, kind, depth, constraints);
            return required ? $"({fq}){inner}" : $"({s}.MaybeNull() ? default({fq}) : ({fq}){inner})";
        }

        var value = NonNull(type, memberName, kind, depth, constraints);
        return nullableReference ? $"({s}.MaybeNull() ? null : {value})" : value;
    }

    /// <summary>Value expression for a non-nullable <paramref name="type"/>, by its <see cref="ValueShape"/>; mirrors <c>ReflectionTypeModel.NonNullProducer</c>.</summary>
    /// <param name="type">The member type.</param>
    /// <param name="memberName">The member name (singularized for collection elements).</param>
    /// <param name="kind">The convention.</param>
    /// <param name="depth">Lambda nesting depth, used to name the scope parameter (<c>s0</c>, <c>s1</c>…).</param>
    /// <param name="constraints">Name of the member's <c>MemberConstraints</c> field, or <see langword="null"/>.</param>
    private string NonNull(ITypeSymbol type, string memberName, ConventionKind kind, int depth, string? constraints = null)
    {
        var extra = constraints is null ? "" : ", " + constraints;
        var s = "s" + depth;
        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var k = "global::Phoney.Generation.ConventionKind." + kind;

        // Collections: elements use the singular member name for conventions ("Emails" → email addresses).
        var singular = memberName.EndsWith("s", StringComparison.Ordinal) ? memberName.Substring(0, memberName.Length - 1) : memberName;
        var next = "s" + (depth + 1);
        switch (ValueShapes.Of(type))
        {
            case ValueShape.Scalar:
                return ScalarValue(Category(type), fq, $"({s}, {k}{extra})") ?? $"default({fq})";
            case ValueShape.Bytes:
                return BytesValue(type, fq, s, constraints);
            case ValueShape.Composite:
                // Every part is produced like a member of its own type, in order, by the name Item1… (Key, Value).
                var parts = CompositeParts((INamedTypeSymbol)type)!.Value;
                var arguments = new List<string>();
                for (var i = 0; i < parts.Types.Count; i++)
                    arguments.Add(Value(parts.Types[i], parts.Names[i], ConventionRules.Match(parts.Names[i], ConventionCategory(parts.Types[i]), ""), false, depth));
                return $"new {parts.TypeName}({string.Join(", ", arguments)})";
            case ValueShape.Adapted:
                // Produced by the add-on (NodaTime) from the member's convention; without the add-on it is created like any other object.
                return AdapterCall(type) is { } adapter ? $"{adapter}({s}, {k}{extra})" : NestedValue(type, fq, s);
            case ValueShape.Array:
                var array = (IArrayTypeSymbol)type;
                return $"{s}.Array<{Fq(array.ElementType)}>(static {next} => {Element(array.ElementType, singular, depth)}{extra})";
            case ValueShape.Grid:
                // Filled row by row; more dimensions are not generated (left default, like other types without a generator).
                var grid = (IArrayTypeSymbol)type;
                return $"{s}.Array2<{Fq(grid.ElementType)}>(static {next} => {Element(grid.ElementType, singular, depth)})";
            case ValueShape.Collection:
                return CollectionValue((INamedTypeSymbol)type, fq, singular, depth, extra)!;
            case ValueShape.Object:
                return NestedValue(type, fq, s);
            case ValueShape.Polymorphic:
                // Interfaces and abstract classes are created as one of their subtypes at runtime (null when there are none).
                AddDerivedTypes((INamedTypeSymbol)type);
                return $"{s}.Nested<{fq}>()";
            default:
                return $"default({fq})";
        }
    }

    /// <summary>The value expression of <c>byte[]</c>, <c>Memory&lt;byte&gt;</c> and <c>ReadOnlyMemory&lt;byte&gt;</c>.</summary>
    private static string BytesValue(ITypeSymbol type, string fq, string s, string? constraints)
    {
        if (type is IArrayTypeSymbol)
            return constraints is null ? $"{s}.Bytes()" : $"{s}.Bytes({constraints})";
        return $"new {fq}({s}.Bytes())";
    }

    /// <summary>The value expression of a nested object, whose model is generated too.</summary>
    private string NestedValue(ITypeSymbol type, string fq, string s)
    {
        Enqueue((INamedTypeSymbol)type);
        return $"{s}.Nested<{fq}>()";
    }

    /// <summary>The call that produces a scalar of <paramref name="category"/> with <paramref name="arguments"/>, or <see langword="null"/> for other types.</summary>
    private static string? ScalarValue(TypeCategory category, string fq, string arguments)
    {
        const string Values = "global::Phoney.Generation.ConventionValues.";
        return category switch
        {
            TypeCategory.String => $"{Values}String{arguments}",
            TypeCategory.Char => $"{Values}Char{arguments}",
            TypeCategory.Bool => $"{Values}Bool{arguments}",
            TypeCategory.Guid => $"{Values}Guid{arguments}",
            TypeCategory.DateTime => $"{Values}DateTime{arguments}",
            TypeCategory.DateTimeOffset => $"{Values}DateTimeOffset{arguments}",
            TypeCategory.DateOnly => $"{Values}DateOnly{arguments}",
            TypeCategory.TimeOnly => $"{Values}TimeOnly{arguments}",
            TypeCategory.TimeSpan => $"{Values}TimeSpan{arguments}",
            TypeCategory.Uri => $"{Values}Uri{arguments}",
            TypeCategory.Rune => $"{Values}Rune{arguments}",
            TypeCategory.IPAddress => $"{Values}IPAddress{arguments}",
            TypeCategory.Version => $"{Values}Version{arguments}",
            TypeCategory.Enum => $"{Values}Enum<{fq}>{arguments}",
            var c when ConventionRules.IsNumber(c) => $"{Values}Number<{fq}>{arguments}",
            _ => null,
        };
    }

    /// <summary>The value expression of a collection element, whose conventions use the singular member name.</summary>
    private string Element(ITypeSymbol element, string singular, int depth) =>
        Value(element, singular, ConventionRules.Match(singular, ConventionCategory(element), ""), false, depth + 1);

    /// <summary>The value expression of a generic collection, or <see langword="null"/> when <paramref name="generic"/> is not one.</summary>
    private string? CollectionValue(INamedTypeSymbol generic, string fq, string singular, int depth, string extra)
    {
        var s = "s" + depth;
        var next = "s" + (depth + 1);
        if (!MemberShapes.TryCollection(FullName(generic.OriginalDefinition), out var shape))
            return null;

        var args = generic.TypeArguments;
        // A dictionary's key is written before its value, as the reflection model draws them.
        var key = shape.Kind == CollectionKind.Dictionary ? Value(args[0], singular + "Key", ConventionKind.None, false, depth + 1) : "";
        var element = Element(shape.Kind == CollectionKind.Dictionary ? args[1] : args[0], singular, depth);
        if (shape.Conversion == CollectionConversion.None)
        {
            return shape.Kind switch
            {
                CollectionKind.List => $"{s}.List<{Fq(args[0])}>(static {next} => {element}{extra})",
                CollectionKind.Set => $"{s}.Set<{Fq(args[0])}>(static {next} => {element}{extra})",
                _ => $"{s}.Dictionary<{Fq(args[0])}, {Fq(args[1])}>(static {next} => {key}, static {next} => {element}{extra})",
            };
        }

        // Other collections are built like a list, set or dictionary and converted, as the reflection model does (see MemberShapes).
        var convert = "l" + depth;
        var typeArguments = shape.Kind == CollectionKind.Dictionary ? Fq(args[0]) + ", " + Fq(args[1]) : Fq(args[0]);
        var body = shape.Conversion == CollectionConversion.Factory
            ? $"global::{generic.OriginalDefinition.ContainingNamespace.ToDisplayString()}.{shape.Builder}.{shape.Method}<{typeArguments}>({convert})"
            : $"new {fq}({convert})";
        return shape.Kind switch
        {
            CollectionKind.List => $"{s}.ListAs<{fq}, {Fq(args[0])}>(static {convert} => {body}, static {next} => {element}{extra})",
            CollectionKind.Set => $"{s}.SetAs<{fq}, {Fq(args[0])}>(static {convert} => {body}, static {next} => {element}{extra})",
            _ => $"{s}.DictionaryAs<{fq}, {Fq(args[0])}, {Fq(args[1])}>(static {convert} => {body}, static {next} => {key}, static {next} => {element}{extra})",
        };
    }

    /// <summary>
    /// The parts of a <c>ValueTuple</c>, <c>Tuple</c> (up to seven) or <c>KeyValuePair</c>, the names their conventions are matched with (<c>Item1</c>… or <c>Key</c>, <c>Value</c>) and the
    /// type to construct (a tuple type written <c>(int, string)</c> is constructed as <c>ValueTuple&lt;int, string&gt;</c>); <see langword="null"/> for other types.
    /// The part names come from <c>MemberShapes.CompositePartNames</c>, which <c>ReflectionTypeModel.CompositeParts</c> uses too.
    /// </summary>
    private static (List<ITypeSymbol> Types, string[] Names, string TypeName)? CompositeParts(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return null;
        var definition = FullName(type.OriginalDefinition); // the metadata name: a tuple type displays as "(T1, T2)", not as System.ValueTuple<T1, T2>
        var arguments = type.TypeArguments.ToList();
        if (MemberShapes.CompositePartNames(definition, arguments.Count) is not { } names)
            return null;

        var prefix = "global::" + definition.Substring(0, definition.IndexOf('`'));
        return (arguments, names, prefix + "<" + string.Join(", ", arguments.Select(Fq)) + ">");
    }

    /// <summary>Generates models for the <c>[JsonDerivedType]</c> types of a polymorphic type and records them as its defaults.</summary>
    private void AddDerivedTypes(INamedTypeSymbol baseType)
    {
        var key = Fq(baseType);
        if (DerivedTypes.ContainsKey(key) || !_compilation.IsSymbolAccessibleWithin(baseType, _owner))
            return;
        var derived = new List<string>();
        foreach (var attribute in baseType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "System.Text.Json.Serialization.JsonDerivedTypeAttribute" ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol type ||
                type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsUnboundGenericType ||
                !_compilation.IsSymbolAccessibleWithin(type, _owner))
            {
                continue;
            }

            Enqueue(type);
            derived.Add(Fq(type));
        }

        if (derived.Count > 0)
        {
            derived.Sort(StringComparer.Ordinal);
            DerivedTypes[key] = derived;
        }
    }

    /// <summary>
    /// Whether a type refers to a type parameter anywhere: itself, its type arguments, array element types or containing types (an open generic type, for which no model
    /// can be generated).
    /// </summary>
    internal static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter) ||
                                  (named.ContainingType is { } containing && ContainsTypeParameter(containing)),
        _ => false,
    };

    /// <summary>
    /// The add-on method producing an adapted type (<c>global::Phoney.NodaTime.NodaTimeValues.Instant</c>), or
    /// <see langword="null"/> when the type is not adapted or the add-on is not referenced (the type is then handled as before).
    /// </summary>
    private string? AdapterCall(ITypeSymbol type)
    {
        if (ConventionRules.AdaptedCategoryOf(FullName(type)) == TypeCategory.Other)
            return null;
        var values = _compilation.GetTypeByMetadataName("Phoney.NodaTime.NodaTimeValues");
        return values is not null && values.GetMembers(type.Name).Any(m => m is IMethodSymbol { IsStatic: true, DeclaredAccessibility: Accessibility.Public })
            ? Fq(values) + "." + type.Name
            : null;
    }

    /// <summary>Namespace-qualified metadata name, as <c>Type.FullName</c> gives it for non-nested types.</summary>
    private static string FullName(ITypeSymbol type) => (type.ContainingNamespace?.ToDisplayString() ?? "") + "." + type.MetadataName;

    /// <summary>The underlying type of <c>Nullable&lt;T&gt;</c>, else the type.</summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    /// <summary>The category conventions are matched against, including adapted date types (an <c>Instant</c> is a date).</summary>
    private static TypeCategory ConventionCategory(ITypeSymbol type)
    {
        type = Unwrap(type);
        return ConventionRules.ConventionCategoryOf(FullName(type), type.TypeKind == TypeKind.Enum);
    }

    /// <summary>Types in System.* namespaces are not populated recursively (their constructors expect real data).</summary>
    internal static bool IsFrameworkType(INamedTypeSymbol type)
    {
        return MemberShapes.IsFrameworkNamespace(type.ContainingNamespace?.ToDisplayString() ?? "");
    }

    /// <summary>Fully qualified display name (<c>global::…</c>).</summary>
    private static string Fq(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>The convention type category, looking through <c>Nullable&lt;T&gt;</c>.</summary>
    internal static TypeCategory Category(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ConventionRules.CategoryOf(ns + "." + type.MetadataName, type.TypeKind == TypeKind.Enum);
    }

    /// <summary>Whether a reference type is annotated nullable (<c>string?</c>).</summary>
    private static bool IsNullableReference(ITypeSymbol type, NullableAnnotation annotation) =>
        type.IsReferenceType && annotation == NullableAnnotation.Annotated;

    /// <summary>Public properties without a public setter and readonly fields that hold a collection that can be added to, base class members first, in declaration order.</summary>
    private static List<ISymbol> FilledMembers(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
            chain.Insert(0, t);

        var members = new List<ISymbol>();
        foreach (var t in chain)
        {
            foreach (var member in InReflectionOrder(t))
            {
                var include = member switch
                {
                    IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, GetMethod.DeclaredAccessibility: Accessibility.Public } p =>
                        p.SetMethod is not { DeclaredAccessibility: Accessibility.Public } && IsFillable(p.Type),
                    IFieldSymbol { IsStatic: false, IsReadOnly: true, IsConst: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } f => IsFillable(f.Type),
                    _ => false,
                };
                if (!include)
                    continue;

                members.RemoveAll(m => m.Name == member.Name);
                members.Add(member);
            }
        }

        return members;
    }

    /// <summary>
    /// The members declared by <paramref name="type"/> in the order the reflection model sorts them (by metadata token): all fields, then all properties, each in
    /// declaration order. The two models draw members in this order, so they must agree on it for a seed to give the same data.
    /// </summary>
    private static IEnumerable<ISymbol> InReflectionOrder(INamedTypeSymbol type) => type.GetMembers().OrderBy(m => m is IFieldSymbol ? 0 : 1);

    private static bool IsFillable(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: true } named && MemberShapes.TryCollection(FullName(named.OriginalDefinition), out var shape) && shape.Fillable;

    /// <summary>Public settable/init properties and non-readonly fields, base class first, in declaration order.</summary>
    private static List<ISymbol> WritableMembers(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
            chain.Insert(0, t);

        var members = new List<ISymbol>();
        foreach (var t in chain)
        {
            foreach (var member in InReflectionOrder(t))
            {
                var include = member switch
                {
                    IPropertySymbol { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, SetMethod.DeclaredAccessibility: Accessibility.Public } => true,
                    IFieldSymbol { IsStatic: false, IsReadOnly: false, IsConst: false, IsImplicitlyDeclared: false, DeclaredAccessibility: Accessibility.Public } => true,
                    _ => false,
                };
                if (!include)
                    continue;

                // An override replaces the base member and takes the derived position, like reflection reports it.
                members.RemoveAll(m => m.Name == member.Name);
                members.Add(member);
            }
        }

        return members;
    }

    /// <summary>
    /// A public parameterless constructor, otherwise the public one with the most parameters (not a copy constructor).
    /// Structs are always default-constructed and then populated, as in the reflection model.
    /// </summary>
    private static IMethodSymbol? ChooseConstructor(INamedTypeSymbol type)
    {
        if (type.IsValueType)
            return null;
        var constructors = type.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        var parameterless = constructors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterless is not null)
            return parameterless;
        return constructors
            .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, type)))
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// The attributes of <paramref name="member"/> with those it inherits from the properties it overrides, as <c>Attribute.GetCustomAttributes(member, inherit: true)</c>
    /// returns them to the reflection model and the validator: an inherited attribute is added unless its usage says <c>Inherited = false</c>, or it allows one use only and the override has its own.
    /// </summary>
    private static IEnumerable<AttributeData> AttributesOf(ISymbol member)
    {
        var attributes = member.GetAttributes().ToList();
        for (var overridden = (member as IPropertySymbol)?.OverriddenProperty; overridden is not null; overridden = overridden.OverriddenProperty)
        {
            var declared = attributes.Select(a => a.AttributeClass).ToList();
            foreach (var attribute in overridden.GetAttributes())
            {
                var (inherited, allowMultiple) = Usage(attribute.AttributeClass);
                if (inherited && (allowMultiple || !declared.Any(c => SymbolEqualityComparer.Default.Equals(c, attribute.AttributeClass))))
                    attributes.Add(attribute);
            }
        }

        return attributes;
    }

    /// <summary>The <c>Inherited</c> and <c>AllowMultiple</c> of an attribute class's <c>[AttributeUsage]</c> (its own or a base class's), with their defaults.</summary>
    private static (bool Inherited, bool AllowMultiple) Usage(INamedTypeSymbol? attributeClass)
    {
        for (var type = attributeClass; type is not null; type = type.BaseType)
        {
            var usage = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
            if (usage is null)
                continue;
            var named = usage.NamedArguments.ToDictionary(a => a.Key, a => a.Value.Value);
            return (!named.TryGetValue("Inherited", out var i) || i is not false, named.TryGetValue("AllowMultiple", out var m) && m is true);
        }

        return (true, false);
    }

    /// <summary>Upper-cases the first character (constructor parameter → property name).</summary>
    private static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
}
