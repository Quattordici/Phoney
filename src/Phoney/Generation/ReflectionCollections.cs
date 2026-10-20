using System.Diagnostics.CodeAnalysis;

namespace Phoney.Generation;

/// <summary>
/// Producers of arrays, sets, lists and dictionaries whose element type is known only at runtime (reflection model). A generic
/// builder is instantiated once per member; the producers then call the typed <see cref="FakeScope"/> helpers that generated
/// models call too, so both models share one algorithm and nothing is boxed or reflected per element.
/// </summary>
internal abstract class ReflectionCollections
{
    /// <summary>The builder for arrays, sets and lists of <paramref name="element"/>.</summary>
    [RequiresUnreferencedCode("Instantiates generic types over runtime types.")]
    [RequiresDynamicCode("Instantiates generic types over runtime types.")]
    public static ReflectionCollections Of(Type element) =>
        (ReflectionCollections)Activator.CreateInstance(typeof(Collections<>).MakeGenericType(element))!;

    /// <summary>The producer of dictionaries from <paramref name="key"/> to <paramref name="value"/>.</summary>
    [RequiresUnreferencedCode("Instantiates generic types over runtime types.")]
    [RequiresDynamicCode("Instantiates generic types over runtime types.")]
    public static Func<FakeScope, object?> DictionaryOf(Type key, Type value, Func<FakeScope, object?> keyProducer, Func<FakeScope, object?> valueProducer, MemberConstraints? constraints) =>
        ((ReflectionCollections)Activator.CreateInstance(typeof(Collections<,>).MakeGenericType(key, value))!).Dictionary(keyProducer, valueProducer, constraints);

    /// <summary>A producer of <c>T[]</c>.</summary>
    public abstract Func<FakeScope, object?> Array(Func<FakeScope, object?> element, MemberConstraints? constraints);

    /// <summary>A producer of <c>T[,]</c>.</summary>
    public abstract Func<FakeScope, object?> Array2(Func<FakeScope, object?> element);

    /// <summary>A producer of <c>HashSet&lt;T&gt;</c>.</summary>
    public abstract Func<FakeScope, object?> Set(Func<FakeScope, object?> element, MemberConstraints? constraints);

    /// <summary>A producer of <c>List&lt;T&gt;</c>.</summary>
    public abstract Func<FakeScope, object?> List(Func<FakeScope, object?> element, MemberConstraints? constraints);

    /// <summary>A producer of <c>Dictionary&lt;TKey, TValue&gt;</c>.</summary>
    public abstract Func<FakeScope, object?> Dictionary(Func<FakeScope, object?> key, Func<FakeScope, object?> value, MemberConstraints? constraints);

    /// <summary>A typed producer over an untyped one; a missing value (depth limit, cycle) is <see langword="default"/>, as in generated models.</summary>
    private static Func<FakeScope, T> Typed<T>(Func<FakeScope, object?> producer) =>
        s => producer(s) is { } value ? (T)value : default!;

    private sealed class Collections<T> : ReflectionCollections
    {
        public override Func<FakeScope, object?> Array(Func<FakeScope, object?> element, MemberConstraints? constraints)
        {
            var typed = Typed<T>(element);
            return s => s.Array(typed, constraints);
        }

        public override Func<FakeScope, object?> Array2(Func<FakeScope, object?> element)
        {
            var typed = Typed<T>(element);
            return s => s.Array2(typed);
        }

        public override Func<FakeScope, object?> Set(Func<FakeScope, object?> element, MemberConstraints? constraints)
        {
            var typed = Typed<T>(element);
            return s => s.Set(typed, constraints);
        }

        public override Func<FakeScope, object?> List(Func<FakeScope, object?> element, MemberConstraints? constraints)
        {
            var typed = Typed<T>(element);
            return s => s.List(typed, constraints);
        }

        public override Func<FakeScope, object?> Dictionary(Func<FakeScope, object?> key, Func<FakeScope, object?> value, MemberConstraints? constraints) =>
            throw new InvalidOperationException("Not a dictionary builder.");
    }

    private sealed class Collections<TKey, TValue> : ReflectionCollections
        where TKey : notnull
    {
        public override Func<FakeScope, object?> Dictionary(Func<FakeScope, object?> key, Func<FakeScope, object?> value, MemberConstraints? constraints)
        {
            var typedKey = Typed<TKey>(key);
            var typedValue = Typed<TValue>(value);
            return s => s.Dictionary(typedKey, typedValue, constraints);
        }

        public override Func<FakeScope, object?> Array(Func<FakeScope, object?> element, MemberConstraints? constraints) => throw NotAnElementBuilder();

        public override Func<FakeScope, object?> Array2(Func<FakeScope, object?> element) => throw NotAnElementBuilder();

        public override Func<FakeScope, object?> Set(Func<FakeScope, object?> element, MemberConstraints? constraints) => throw NotAnElementBuilder();

        public override Func<FakeScope, object?> List(Func<FakeScope, object?> element, MemberConstraints? constraints) => throw NotAnElementBuilder();

        private static InvalidOperationException NotAnElementBuilder() => new("Not a collection builder.");
    }
}
