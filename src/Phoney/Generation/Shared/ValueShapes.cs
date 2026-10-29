#nullable enable

// This file is compiled into the runtime and into the source generator (see Phoney.SourceGenerator.csproj), so the reflection model and generated models decide
// how a value of a member type is made in one place. Keep it free of APIs newer than netstandard2.0.
namespace Phoney.Generation;

/// <summary>How a value of a member type is made; the reflection model builds a producer for each and the source generator writes an expression for each.</summary>
internal enum ValueShape
{
    /// <summary>Nothing is generated: the type's default (arrays of three or more dimensions, delegates, classes and structs that refer to a type parameter).</summary>
    None,

    /// <summary>A scalar a convention value is made for (numbers, texts, dates, enums…), see <c>ConventionRules.CategoryOf</c>.</summary>
    Scalar,

    /// <summary><c>byte[]</c>, <c>Memory&lt;byte&gt;</c> or <c>ReadOnlyMemory&lt;byte&gt;</c>: random bytes.</summary>
    Bytes,

    /// <summary>A tuple or key/value pair, built part by part (see <c>MemberShapes.CompositePartNames</c>).</summary>
    Composite,

    /// <summary>A date type of an add-on package (NodaTime), made by the add-on when it is referenced and created like an object when it isn't.</summary>
    Adapted,

    /// <summary>A one-dimensional array.</summary>
    Array,

    /// <summary>A two-dimensional array, filled row by row.</summary>
    Grid,

    /// <summary>A generated collection (see <c>MemberShapes.TryCollection</c>).</summary>
    Collection,

    /// <summary>Another framework type: not created (a class stays <see langword="null"/>, a struct its default), because its constructors expect real data.</summary>
    Framework,

    /// <summary>A concrete class or struct of the application, created as a nested object.</summary>
    Object,

    /// <summary>An interface or abstract class of the application, created as one of its subtypes.</summary>
    Polymorphic,
}

/// <summary>What kind of type a member has, as far as <see cref="ValueShapes.Of(TypeFacts)"/> needs to know.</summary>
internal enum TypeForm
{
    /// <summary>Anything else: a delegate, a pointer, a type parameter.</summary>
    Other,

    /// <summary>A class that can be created.</summary>
    Class,

    /// <summary>A struct.</summary>
    Struct,

    /// <summary>An enum.</summary>
    Enum,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An abstract class.</summary>
    Abstract,

    /// <summary>An array.</summary>
    Array,
}

/// <summary>The facts about a (non-nullable) member type that decide its <see cref="ValueShape"/>, read from a <c>System.Type</c> or a Roslyn symbol.</summary>
internal readonly struct TypeFacts
{
    /// <summary>Creates the facts of a type.</summary>
    /// <param name="name">The metadata name of the type, of its open generic type for a generic one (<c>System.Collections.Generic.List`1</c>), as <c>Type.FullName</c> gives it.</param>
    /// <param name="ns">The namespace.</param>
    /// <param name="form">What kind of type it is.</param>
    /// <param name="arity">The number of type arguments.</param>
    /// <param name="arrayRank">The number of dimensions of an array; 0 for other types.</param>
    /// <param name="ofBytes">Whether the element type of an array, or the first type argument, is <c>byte</c>.</param>
    /// <param name="isOpen">Whether the type refers to a type parameter, so no value of it can be made.</param>
    public TypeFacts(string name, string ns, TypeForm form, int arity, int arrayRank, bool ofBytes, bool isOpen)
    {
        Name = name;
        Namespace = ns;
        Form = form;
        Arity = arity;
        ArrayRank = arrayRank;
        OfBytes = ofBytes;
        IsOpen = isOpen;
    }

    /// <summary>The metadata name of the type (of its open generic type for a generic one).</summary>
    public string Name { get; }

    /// <summary>The namespace.</summary>
    public string Namespace { get; }

    /// <summary>What kind of type it is.</summary>
    public TypeForm Form { get; }

    /// <summary>The number of type arguments.</summary>
    public int Arity { get; }

    /// <summary>The number of dimensions of an array; 0 for other types.</summary>
    public int ArrayRank { get; }

    /// <summary>Whether the element type of an array, or the first type argument, is <c>byte</c>.</summary>
    public bool OfBytes { get; }

    /// <summary>Whether the type refers to a type parameter.</summary>
    public bool IsOpen { get; }
}

/// <summary>
/// Decides how a value of a member type is made, the same for the reflection model and the source generator, so a new kind of type is added in one place.
/// The order of the checks is the order of the rules: scalars, bytes, tuples, add-on dates, arrays, collections, then framework types, application types.
/// </summary>
/// <remarks>The runtime reads the facts from a <c>System.Type</c> (<c>ValueShapes.Types.cs</c>), the generator from a Roslyn symbol (<c>ValueShapes.Symbols.cs</c>).</remarks>
internal static partial class ValueShapes
{
    /// <summary>The shape of a non-nullable type (look through <c>Nullable&lt;T&gt;</c> first).</summary>
    public static ValueShape Of(TypeFacts type) =>
        type.Form == TypeForm.Array ? ArrayShape(type) : KnownShape(type) ?? ApplicationShape(type);

    /// <summary>One dimension is an array (or bytes), two a grid; more are not generated.</summary>
    private static ValueShape ArrayShape(TypeFacts type) => type.ArrayRank switch
    {
        1 => type.OfBytes ? ValueShape.Bytes : ValueShape.Array,
        2 => ValueShape.Grid,
        _ => ValueShape.None,
    };

    /// <summary>The shape of a type Phoney knows by name: scalars, byte memory, tuples, add-on dates, collections; <see langword="null"/> for other types.</summary>
    private static ValueShape? KnownShape(TypeFacts type)
    {
        if (ConventionRules.CategoryOf(type.Name, type.Form == TypeForm.Enum) != TypeCategory.Other)
            return ValueShape.Scalar;
        if (type.Arity == 1 && type.OfBytes && type.Name is "System.Memory`1" or "System.ReadOnlyMemory`1")
            return ValueShape.Bytes;
        if (MemberShapes.CompositePartNames(type.Name, type.Arity) is not null)
            return ValueShape.Composite;
        if (ConventionRules.AdaptedCategoryOf(type.Name) != TypeCategory.Other)
            return ValueShape.Adapted;
        if (type.Arity > 0 && MemberShapes.TryCollection(type.Name, out _))
            return ValueShape.Collection;
        return null;
    }

    /// <summary>Any other type: left alone in the framework, created (or one of its subtypes) in the application.</summary>
    private static ValueShape ApplicationShape(TypeFacts type)
    {
        if (MemberShapes.IsFrameworkNamespace(type.Namespace))
            return ValueShape.Framework;

        return type.Form switch
        {
            TypeForm.Class or TypeForm.Struct => type.IsOpen ? ValueShape.None : ValueShape.Object,
            TypeForm.Interface or TypeForm.Abstract => ValueShape.Polymorphic,
            _ => ValueShape.None,
        };
    }

    /// <summary>Whether values of <paramref name="shape"/> are objects Phoney creates and populates recursively (<c>FakeMember.IsNested</c>).</summary>
    public static bool IsNested(ValueShape shape) => shape is ValueShape.Object or ValueShape.Polymorphic;
}
