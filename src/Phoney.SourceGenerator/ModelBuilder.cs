using System;
using System.Collections.Generic;
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
}

/// <summary>
/// Walks the requested types and everything they contain, mirroring the runtime reflection model: constructor
/// choice, member order (base class first, declaration order) and value production are the same, so seeded
/// output does not depend on whether a model was generated.
/// </summary>
internal sealed class ModelBuilder
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _owner;
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly LocationInfo _location;
    private readonly Queue<INamedTypeSymbol> _queue = new();
    private readonly HashSet<INamedTypeSymbol> _seen = new(SymbolEqualityComparer.Default);

    public ModelBuilder(Compilation compilation, INamedTypeSymbol owner, List<DiagnosticInfo> diagnostics, LocationInfo location)
    {
        _compilation = compilation;
        _owner = owner;
        _diagnostics = diagnostics;
        _location = location;
    }

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
            return Skip(type, $"it is not accessible from '{_owner.Name}'");

        var writable = WritableMembers(type);
        var constructor = ChooseConstructor(type);
        if (constructor is null && type.TypeKind == TypeKind.Class)
            return Skip(type, "it has no public constructor");

        var members = new List<MemberModel>();
        foreach (var parameter in constructor?.Parameters ?? ImmutableEmpty())
        {
            var match = writable.FirstOrDefault(m => string.Equals(m.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                writable.Remove(match);
            var name = match?.Name ?? Capitalize(parameter.Name);
            // Annotations may sit on the parameter or (with [property: …]) on the generated property.
            var attributes = parameter.GetAttributes().Concat(match?.GetAttributes() ?? Enumerable.Empty<AttributeData>());
            var parameterMember = Member(type, name, parameter.Type, IsNullableReference(parameter.Type, parameter.NullableAnnotation), isConstructorParameter: true, isInitOnly: false, inInitializer: false, declaringType: type, isField: false, attributes, members.Count);
            // A record's positional property behind the parameter can be set on an existing object.
            if (match is IPropertySymbol parameterProperty)
            {
                parameterMember.Settable = true;
                parameterMember.SetterInitOnly = parameterProperty.SetMethod!.IsInitOnly;
                parameterMember.SetterDeclaringType = Fq(parameterProperty.ContainingType);
            }
            else if (match is IFieldSymbol parameterField)
            {
                parameterMember.Settable = true;
                parameterMember.SetterDeclaringType = Fq(parameterField.ContainingType);
            }

            members.Add(parameterMember);
        }

        foreach (var member in writable)
        {
            if (member is IPropertySymbol p)
            {
                var initOnly = p.SetMethod!.IsInitOnly;
                members.Add(Member(type, p.Name, p.Type, IsNullableReference(p.Type, p.NullableAnnotation), false, initOnly, initOnly || p.IsRequired, p.ContainingType, isField: false, p.GetAttributes(), members.Count));
            }
            else if (member is IFieldSymbol f)
            {
                members.Add(Member(type, f.Name, f.Type, IsNullableReference(f.Type, f.NullableAnnotation), false, false, f.IsRequired, f.ContainingType, isField: true, f.GetAttributes(), members.Count));
            }
        }

        // Read-only collections come last (their elements are drawn after everything else), as in the reflection model.
        var taken = new HashSet<string>(members.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var member in FilledMembers(type))
        {
            if (!taken.Add(member.Name))
                continue;
            var (memberType, annotation) = member is IPropertySymbol property ? (property.Type, property.NullableAnnotation) : (((IFieldSymbol)member).Type, ((IFieldSymbol)member).NullableAnnotation);
            var filled = Member(type, member.Name, memberType, IsNullableReference(memberType, annotation), false, false, false, member.ContainingType, isField: member is IFieldSymbol, member.GetAttributes(), members.Count);
            filled.Settable = false;
            filled.FillsExisting = true;
            filled.FillTypeArguments = string.Join(", ", ((INamedTypeSymbol)memberType).TypeArguments.Select(Fq));
            members.Add(filled);
        }

        var key = FakeForGenerator.Sanitize(type.ToDisplayString());
        return new TypeModel(fullName, key, type.IsValueType, constructor is not null, members);
    }

    /// <summary>No constructor parameters.</summary>
    private static IEnumerable<IParameterSymbol> ImmutableEmpty() => Array.Empty<IParameterSymbol>();

    /// <summary>Reports PHONEY002 for a type that cannot be generated.</summary>
    private TypeModel? Skip(INamedTypeSymbol type, string reason)
    {
        _diagnostics.Add(new DiagnosticInfo(Diagnostics.CannotGenerate, _location, type.ToDisplayString(), reason));
        return null;
    }

    /// <summary>Describes a member and computes its value expression.</summary>
    private MemberModel Member(INamedTypeSymbol owner, string name, ITypeSymbol type, bool nullableReference, bool isConstructorParameter, bool isInitOnly, bool inInitializer, INamedTypeSymbol declaringType, bool isField, IEnumerable<AttributeData> attributes, int index)
    {
        // Same rules as the reflection model: a format annotation overrides the name convention, [Required] is never null.
        var constraints = ConstraintReader.Read(attributes);
        var convention = constraints.Format is { } format
            ? (ConventionKind)Enum.Parse(typeof(ConventionKind), format)
            : ConventionRules.Match(name, ConventionCategory(type), owner.Name);
        var constraintsField = constraints.Initializer is null ? null : "__c" + index;
        return new MemberModel
        {
            Name = name,
            TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Convention = convention.ToString(),
            IsConstructorParameter = isConstructorParameter,
            IsInitOnly = isInitOnly,
            InInitializer = inInitializer,
            Value = Value(type, name, convention, nullableReference && !constraints.Required, 0, constraintsField, constraints.Required),
            DeclaringType = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsField = isField,
            Constraints = constraints.Initializer,
            ConstraintsField = constraintsField,
            Settable = !isConstructorParameter,
            SetterInitOnly = isInitOnly,
            SetterDeclaringType = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Type = type,
            IsScalar = Category(type) != TypeCategory.Other || AdapterCall(Unwrap(type)) is not null,
            DefinesValue = constraints.DefinesValue,
            IsNested = IsNestable(type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType ? nullableType.TypeArguments[0] : type) || IsPolymorphic(type),
        };
    }

    private const string Immutable = "global::System.Collections.Immutable.";
    private const string Frozen = "global::System.Collections.Frozen.";

    /// <summary>
    /// The collections that are built like a list ('L'), set ('S') or dictionary ('D') and converted: how to make one from the built collection <c>{p}</c>.
    /// Mirrors <c>CollectionTypes</c> and <c>CollectionConverters</c> in the runtime; <c>{type}</c> is the member's type, <c>{T}</c>/<c>{K}</c> and <c>{V}</c> its type arguments.
    /// </summary>
    private static readonly Dictionary<string, (char Kind, string Convert)> OtherCollections = new()
    {
        ["System.Collections.Immutable.ImmutableArray<T>"] = ('L', Immutable + "ImmutableArray.CreateRange<{T}>({p})"),
        ["System.Collections.Immutable.ImmutableList<T>"] = ('L', Immutable + "ImmutableList.CreateRange<{T}>({p})"),
        ["System.Collections.Immutable.IImmutableList<T>"] = ('L', Immutable + "ImmutableList.CreateRange<{T}>({p})"),
        ["System.Collections.ObjectModel.ReadOnlyCollection<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.ObjectModel.Collection<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.ObjectModel.ObservableCollection<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Generic.Queue<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Generic.Stack<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Generic.LinkedList<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Concurrent.ConcurrentBag<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Concurrent.ConcurrentQueue<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Concurrent.ConcurrentStack<T>"] = ('L', "new {type}({p})"),
        ["System.Collections.Immutable.ImmutableHashSet<T>"] = ('S', Immutable + "ImmutableHashSet.CreateRange<{T}>({p})"),
        ["System.Collections.Immutable.IImmutableSet<T>"] = ('S', Immutable + "ImmutableHashSet.CreateRange<{T}>({p})"),
        ["System.Collections.Immutable.ImmutableSortedSet<T>"] = ('S', Immutable + "ImmutableSortedSet.CreateRange<{T}>({p})"),
        ["System.Collections.Generic.SortedSet<T>"] = ('S', "new {type}({p})"),
        ["System.Collections.Frozen.FrozenSet<T>"] = ('S', Frozen + "FrozenSet.ToFrozenSet<{T}>({p})"),
        ["System.Collections.Immutable.ImmutableDictionary<TKey, TValue>"] = ('D', Immutable + "ImmutableDictionary.CreateRange<{K}, {V}>({p})"),
        ["System.Collections.Immutable.IImmutableDictionary<TKey, TValue>"] = ('D', Immutable + "ImmutableDictionary.CreateRange<{K}, {V}>({p})"),
        ["System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>"] = ('D', Immutable + "ImmutableSortedDictionary.CreateRange<{K}, {V}>({p})"),
        ["System.Collections.Generic.SortedDictionary<TKey, TValue>"] = ('D', "new {type}({p})"),
        ["System.Collections.Generic.SortedList<TKey, TValue>"] = ('D', "new {type}({p})"),
        ["System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>"] = ('D', "new {type}({p})"),
        ["System.Collections.Frozen.FrozenDictionary<TKey, TValue>"] = ('D', Frozen + "FrozenDictionary.ToFrozenDictionary<{K}, {V}>({p})"),
    };

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

    /// <summary>Value expression for a non-nullable <paramref name="type"/>: scalars, byte arrays, collections, nested objects.</summary>
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
        const string Values = "global::Phoney.Generation.ConventionValues.";
        var category = Category(type);
        switch (category)
        {
            case TypeCategory.String: return $"{Values}String({s}, {k}{extra})";
            case TypeCategory.Char: return $"{Values}Char({s}, {k}{extra})";
            case TypeCategory.Bool: return $"{Values}Bool({s}, {k}{extra})";
            case TypeCategory.Guid: return $"{Values}Guid({s}, {k}{extra})";
            case TypeCategory.DateTime: return $"{Values}DateTime({s}, {k}{extra})";
            case TypeCategory.DateTimeOffset: return $"{Values}DateTimeOffset({s}, {k}{extra})";
            case TypeCategory.DateOnly: return $"{Values}DateOnly({s}, {k}{extra})";
            case TypeCategory.TimeOnly: return $"{Values}TimeOnly({s}, {k}{extra})";
            case TypeCategory.TimeSpan: return $"{Values}TimeSpan({s}, {k}{extra})";
            case TypeCategory.Uri: return $"{Values}Uri({s}, {k}{extra})";
            case TypeCategory.Rune: return $"{Values}Rune({s}, {k}{extra})";
            case TypeCategory.IPAddress: return $"{Values}IPAddress({s}, {k}{extra})";
            case TypeCategory.Version: return $"{Values}Version({s}, {k}{extra})";
            case TypeCategory.Enum: return $"{Values}Enum<{fq}>({s}, {k}{extra})";
            case var c when ConventionRules.IsNumber(c): return $"{Values}Number<{fq}>({s}, {k}{extra})";
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, Rank: 1 })
            return $"{s}.Bytes()";
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } memory && memory.TypeArguments[0].SpecialType == SpecialType.System_Byte &&
            memory.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>")
        {
            return $"new {fq}({s}.Bytes())";
        }

        // Tuples and key/value pairs: every part is produced like a member of its own type, in order, by the name Item1… (Key, Value).
        if (type is INamedTypeSymbol composite && CompositeParts(composite) is { } parts)
        {
            var arguments = new List<string>();
            for (var i = 0; i < parts.Types.Count; i++)
                arguments.Add(Value(parts.Types[i], parts.Names[i], ConventionRules.Match(parts.Names[i], ConventionCategory(parts.Types[i]), ""), false, depth));
            return $"new {parts.TypeName}({string.Join(", ", arguments)})";
        }

        // Date and time types of add-on packages (NodaTime) are produced by the add-on from the member's convention.
        if (AdapterCall(type) is { } adapter)
            return $"{adapter}({s}, {k}{extra})";

        // Collections: elements use the singular member name for conventions ("Emails" → email addresses).
        var singular = memberName.EndsWith("s", StringComparison.Ordinal) ? memberName.Substring(0, memberName.Length - 1) : memberName;
        string Element(ITypeSymbol element) =>
            Value(element, singular, ConventionRules.Match(singular, ConventionCategory(element), ""), false, depth + 1);
        var next = "s" + (depth + 1);

        if (type is IArrayTypeSymbol { Rank: 1 } array)
            return $"{s}.Array<{Fq(array.ElementType)}>(static {next} => {Element(array.ElementType)}{extra})";

        // Two-dimensional arrays are filled row by row; more dimensions are not generated (left default, like other types without a generator).
        if (type is IArrayTypeSymbol { Rank: 2 } grid)
            return $"{s}.Array2<{Fq(grid.ElementType)}>(static {next} => {Element(grid.ElementType)})";

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            var definition = generic.OriginalDefinition.ToDisplayString();
            var args = generic.TypeArguments;
            // Other collections are built like a list, set or dictionary and converted, as the reflection model does (see CollectionTypes).
            if (OtherCollections.TryGetValue(definition, out var other))
            {
                var convert = "l" + depth;
                var body = other.Convert.Replace("{type}", fq).Replace("{T}", Fq(args[0])).Replace("{K}", Fq(args[0])).Replace("{V}", args.Length > 1 ? Fq(args[1]) : "").Replace("{p}", convert);
                switch (other.Kind)
                {
                    case 'L':
                        return $"{s}.ListAs<{fq}, {Fq(args[0])}>(static {convert} => {body}, static {next} => {Element(args[0])}{extra})";
                    case 'S':
                        return $"{s}.SetAs<{fq}, {Fq(args[0])}>(static {convert} => {body}, static {next} => {Element(args[0])}{extra})";
                    default:
                        var otherKey = Value(args[0], singular + "Key", ConventionKind.None, false, depth + 1);
                        return $"{s}.DictionaryAs<{fq}, {Fq(args[0])}, {Fq(args[1])}>(static {convert} => {body}, static {next} => {otherKey}, static {next} => {Element(args[1])}{extra})";
                }
            }

            switch (definition)
            {
                case "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.ICollection<T>"
                    or "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>":
                    return $"{s}.List<{Fq(args[0])}>(static {next} => {Element(args[0])}{extra})";
                case "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.ISet<T>" or "System.Collections.Generic.IReadOnlySet<T>":
                    return $"{s}.Set<{Fq(args[0])}>(static {next} => {Element(args[0])}{extra})";
                case "System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                    var key = Value(args[0], singular + "Key", ConventionKind.None, false, depth + 1);
                    return $"{s}.Dictionary<{Fq(args[0])}, {Fq(args[1])}>(static {next} => {key}, static {next} => {Element(args[1])}{extra})";
            }
        }

        // An adapted date type without its add-on is created like any other object, as before the add-on existed.
        if (IsNestable(type) || (type is INamedTypeSymbol { IsAbstract: false, TypeKind: TypeKind.Class or TypeKind.Struct } && ConventionRules.AdaptedCategoryOf(FullName(type)) != TypeCategory.Other))
        {
            Enqueue((INamedTypeSymbol)type);
            return $"{s}.Nested<{fq}>()";
        }

        // Interfaces and abstract classes are created as one of their subtypes at runtime (null when there are none).
        if (IsPolymorphic(type))
        {
            AddDerivedTypes((INamedTypeSymbol)type);
            return $"{s}.Nested<{fq}>()";
        }

        return $"default({fq})";
    }

    /// <summary>
    /// The parts of a <c>ValueTuple</c>, <c>Tuple</c> (up to seven) or <c>KeyValuePair</c>, the names their conventions are matched with (<c>Item1</c>… or <c>Key</c>, <c>Value</c>) and the
    /// type to construct (a tuple type written <c>(int, string)</c> is constructed as <c>ValueTuple&lt;int, string&gt;</c>); <see langword="null"/> for other types.
    /// Mirrors <c>ReflectionTypeModel.CompositeParts</c>.
    /// </summary>
    private static (List<ITypeSymbol> Types, string[] Names, string TypeName)? CompositeParts(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return null;
        var definition = FullName(type.OriginalDefinition); // the metadata name: a tuple type displays as "(T1, T2)", not as System.ValueTuple<T1, T2>
        var arguments = type.TypeArguments.ToList();
        string prefix;
        string[] names;
        if (definition == "System.Collections.Generic.KeyValuePair`2")
        {
            prefix = "global::System.Collections.Generic.KeyValuePair";
            names = new[] { "Key", "Value" };
        }
        else if (arguments.Count <= 7 && (definition.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || definition.StartsWith("System.Tuple`", StringComparison.Ordinal)))
        {
            prefix = "global::" + definition.Substring(0, definition.IndexOf('`'));
            names = Enumerable.Range(1, arguments.Count).Select(i => "Item" + i).ToArray();
        }
        else
        {
            return null;
        }

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

    /// <summary>User interfaces and abstract classes (also generic ones), which are generated as one of their subtypes.</summary>
    internal static bool IsPolymorphic(ITypeSymbol type) =>
        type is INamedTypeSymbol { SpecialType: SpecialType.None } named &&
        (named.TypeKind == TypeKind.Interface || (named.TypeKind == TypeKind.Class && named.IsAbstract)) &&
        !IsFrameworkType(named);

    /// <summary>Objects Phoney recurses into: concrete user types, closed generic ones included (framework types are left default, adapted date types are values).</summary>
    private static bool IsNestable(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false } named &&
        named.SpecialType == SpecialType.None &&
        !IsFrameworkType(named) &&
        !ContainsTypeParameter(named) &&
        ConventionRules.AdaptedCategoryOf(FullName(named)) == TypeCategory.Other;

    /// <summary>Whether a type is, or has among its type arguments, a type parameter (an open generic type, for which no model can be generated).</summary>
    private static bool ContainsTypeParameter(ITypeSymbol type) =>
        type is ITypeParameterSymbol || (type is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Any(ContainsTypeParameter));

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
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
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

    /// <summary>Public settable/init properties and non-readonly fields, base class first, in declaration order.</summary>
    /// <summary>The collections whose elements can be added to the instance a read-only member holds; mirrors <c>CollectionTypes.IsFillable</c> in the runtime.</summary>
    private static readonly HashSet<string> FillableCollections = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List<T>", "System.Collections.Generic.IList<T>", "System.Collections.Generic.ICollection<T>", "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.ISet<T>", "System.Collections.ObjectModel.Collection<T>", "System.Collections.ObjectModel.ObservableCollection<T>",
        "System.Collections.Generic.SortedSet<T>", "System.Collections.Generic.LinkedList<T>", "System.Collections.Generic.Dictionary<TKey, TValue>",
        "System.Collections.Generic.IDictionary<TKey, TValue>", "System.Collections.Generic.SortedDictionary<TKey, TValue>", "System.Collections.Generic.SortedList<TKey, TValue>",
        "System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue>",
    };

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

    private static bool IsFillable(ITypeSymbol type) => type is INamedTypeSymbol { IsGenericType: true } named && FillableCollections.Contains(named.OriginalDefinition.ToDisplayString());

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

    /// <summary>Upper-cases the first character (constructor parameter → property name).</summary>
    private static string Capitalize(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
}
