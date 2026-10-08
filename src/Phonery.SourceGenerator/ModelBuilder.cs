using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Phonery.Generation;

namespace Phonery.SourceGenerator;

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
            members.Add(Member(type, name, parameter.Type, IsNullableReference(parameter.Type, parameter.NullableAnnotation), isConstructorParameter: true, isInitOnly: false, inInitializer: false, declaringType: type, isField: false));
        }

        foreach (var member in writable)
        {
            if (member is IPropertySymbol p)
            {
                var initOnly = p.SetMethod!.IsInitOnly;
                members.Add(Member(type, p.Name, p.Type, IsNullableReference(p.Type, p.NullableAnnotation), false, initOnly, initOnly || p.IsRequired, p.ContainingType, isField: false));
            }
            else if (member is IFieldSymbol f)
            {
                members.Add(Member(type, f.Name, f.Type, IsNullableReference(f.Type, f.NullableAnnotation), false, false, f.IsRequired, f.ContainingType, isField: true));
            }
        }

        var key = FakeForGenerator.Sanitize(type.ToDisplayString());
        return new TypeModel(fullName, key, type.IsValueType, constructor is not null, members);
    }

    /// <summary>No constructor parameters.</summary>
    private static IEnumerable<IParameterSymbol> ImmutableEmpty() => Array.Empty<IParameterSymbol>();

    /// <summary>Reports PHONERY002 for a type that cannot be generated.</summary>
    private TypeModel? Skip(INamedTypeSymbol type, string reason)
    {
        _diagnostics.Add(new DiagnosticInfo(Diagnostics.CannotGenerate, _location, type.ToDisplayString(), reason));
        return null;
    }

    /// <summary>Describes a member and computes its value expression.</summary>
    private MemberModel Member(INamedTypeSymbol owner, string name, ITypeSymbol type, bool nullableReference, bool isConstructorParameter, bool isInitOnly, bool inInitializer, INamedTypeSymbol declaringType, bool isField)
    {
        var category = Category(type);
        return new MemberModel
        {
            Name = name,
            TypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Convention = ConventionRules.Match(name, category, owner.Name).ToString(),
            IsConstructorParameter = isConstructorParameter,
            IsInitOnly = isInitOnly,
            InInitializer = inInitializer,
            Value = Value(type, name, ConventionRules.Match(name, category, owner.Name), nullableReference, 0),
            DeclaringType = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsField = isField,
        };
    }

    /// <summary>The value expression for a member of <paramref name="type"/>; mirrors <c>ReflectionTypeModel.Producer</c>.</summary>
    private string Value(ITypeSymbol type, string memberName, ConventionKind kind, bool nullableReference, int depth)
    {
        var s = "s" + depth;
        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var inner = NonNull(nullable.TypeArguments[0], memberName, kind, depth);
            return $"({s}.MaybeNull() ? default({fq}) : ({fq}){inner})";
        }

        var value = NonNull(type, memberName, kind, depth);
        return nullableReference ? $"({s}.MaybeNull() ? null : {value})" : value;
    }

    /// <summary>Value expression for a non-nullable <paramref name="type"/>: scalars, byte arrays, collections, nested objects.</summary>
    private string NonNull(ITypeSymbol type, string memberName, ConventionKind kind, int depth)
    {
        var s = "s" + depth;
        var fq = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var k = "global::Phonery.Generation.ConventionKind." + kind;
        const string Values = "global::Phonery.Generation.ConventionValues.";
        var category = Category(type);
        switch (category)
        {
            case TypeCategory.String: return $"{Values}String({s}, {k})";
            case TypeCategory.Char: return $"{Values}Char({s}, {k})";
            case TypeCategory.Bool: return $"{Values}Bool({s}, {k})";
            case TypeCategory.Guid: return $"{Values}Guid({s}, {k})";
            case TypeCategory.DateTime: return $"{Values}DateTime({s}, {k})";
            case TypeCategory.DateTimeOffset: return $"{Values}DateTimeOffset({s}, {k})";
            case TypeCategory.DateOnly: return $"{Values}DateOnly({s}, {k})";
            case TypeCategory.TimeOnly: return $"{Values}TimeOnly({s}, {k})";
            case TypeCategory.TimeSpan: return $"{Values}TimeSpan({s}, {k})";
            case TypeCategory.Uri: return $"{Values}Uri({s}, {k})";
            case TypeCategory.Enum: return $"{Values}Enum<{fq}>({s}, {k})";
            case var c when ConventionRules.IsNumber(c): return $"{Values}Number<{fq}>({s}, {k})";
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, Rank: 1 })
            return $"{s}.Bytes()";

        // Collections: elements use the singular member name for conventions ("Emails" → email addresses).
        var singular = memberName.EndsWith("s", StringComparison.Ordinal) ? memberName.Substring(0, memberName.Length - 1) : memberName;
        string Element(ITypeSymbol element) =>
            Value(element, singular, ConventionRules.Match(singular, Category(element), ""), false, depth + 1);
        var next = "s" + (depth + 1);

        if (type is IArrayTypeSymbol { Rank: 1 } array)
            return $"{s}.Array<{Fq(array.ElementType)}>(static {next} => {Element(array.ElementType)})";

        if (type is INamedTypeSymbol { IsGenericType: true } generic)
        {
            var definition = generic.OriginalDefinition.ToDisplayString();
            var args = generic.TypeArguments;
            switch (definition)
            {
                case "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.ICollection<T>"
                    or "System.Collections.Generic.IEnumerable<T>" or "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>":
                    return $"{s}.List<{Fq(args[0])}>(static {next} => {Element(args[0])})";
                case "System.Collections.Generic.HashSet<T>" or "System.Collections.Generic.ISet<T>" or "System.Collections.Generic.IReadOnlySet<T>":
                    return $"{s}.Set<{Fq(args[0])}>(static {next} => {Element(args[0])})";
                case "System.Collections.Generic.Dictionary<TKey, TValue>" or "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                    var key = Value(args[0], singular + "Key", ConventionKind.None, false, depth + 1);
                    return $"{s}.Dictionary<{Fq(args[0])}, {Fq(args[1])}>(static {next} => {key}, static {next} => {Element(args[1])})";
            }
        }

        if (IsNestable(type))
        {
            Enqueue((INamedTypeSymbol)type);
            return $"{s}.Nested<{fq}>()";
        }

        return $"default({fq})";
    }

    /// <summary>Objects Phonery recurses into: concrete user types (framework types are left default).</summary>
    private static bool IsNestable(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false, IsGenericType: false } named &&
        named.SpecialType == SpecialType.None &&
        !IsFrameworkType(named);

    /// <summary>Types in System.* namespaces are not populated recursively (their constructors expect real data).</summary>
    internal static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    /// <summary>Fully qualified display name (<c>global::…</c>).</summary>
    private static string Fq(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>The convention type category, looking through <c>Nullable&lt;T&gt;</c>.</summary>
    private static TypeCategory Category(ITypeSymbol type)
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
    private static List<ISymbol> WritableMembers(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
            chain.Insert(0, t);

        var members = new List<ISymbol>();
        foreach (var t in chain)
        {
            foreach (var member in t.GetMembers())
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
