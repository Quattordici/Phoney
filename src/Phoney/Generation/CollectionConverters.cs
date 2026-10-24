using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Phoney.Generation;

/// <summary>
/// Turns the <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c> or <c>Dictionary&lt;TKey, TValue&gt;</c> that was built for a member into the member's own collection type
/// (<c>ImmutableArray&lt;T&gt;</c>, <c>ConcurrentDictionary&lt;TKey, TValue&gt;</c>…). One compiled delegate per member, so nothing is reflected per element or per collection.
/// </summary>
internal static class CollectionConverters
{
    /// <summary>
    /// A delegate that converts an instance of <paramref name="source"/> (what was built) into a <paramref name="target"/> (the member's type),
    /// or <see langword="null"/> when <paramref name="source"/> already is one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No factory method or constructor takes the built collection.</exception>
    [RequiresUnreferencedCode("Finds factory methods and constructors of collection types.")]
    [RequiresDynamicCode("Compiles an expression.")]
    public static Func<object, object>? Create(Type target, Type source)
    {
        if (target.IsAssignableFrom(source))
            return null;

        var input = Expression.Parameter(typeof(object), "built");
        var built = Expression.Convert(input, source);
        Expression create = CollectionTypes.ShapeOf(target) is { Conversion: CollectionConversion.Factory } factory
            ? FromFactory(Builder(target, factory), factory.Method!, target, built)
            : FromConstructor(target, built);
        return Expression.Lambda<Func<object, object>>(Expression.Convert(create, typeof(object)), input).Compile();
    }

    /// <summary>The static class that holds the factory, found next to the collection type (<c>ImmutableArray</c> for <c>ImmutableArray&lt;T&gt;</c>).</summary>
    [RequiresUnreferencedCode("Finds the builder class of a collection type.")]
    private static Type Builder(Type target, CollectionShape shape) =>
        target.Assembly.GetType(target.Namespace + "." + shape.Builder) ?? throw Unsupported(target);

    [RequiresUnreferencedCode("Finds factory methods of collection builders.")]
    [RequiresDynamicCode("Instantiates generic methods over runtime types.")]
    private static MethodCallExpression FromFactory(Type builder, string name, Type target, Expression built)
    {
        var typeArguments = target.GetGenericArguments();
        foreach (var method in builder.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != name || !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != typeArguments.Length)
                continue;
            var closed = method.MakeGenericMethod(typeArguments);
            if (Arguments(closed.GetParameters(), built) is { } arguments)
                return Expression.Call(closed, arguments);
        }

        throw Unsupported(target);
    }

    [RequiresUnreferencedCode("Finds constructors of collection types.")]
    private static NewExpression FromConstructor(Type target, Expression built)
    {
        foreach (var constructor in target.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Arguments(constructor.GetParameters(), built) is { } arguments)
                return Expression.New(constructor, arguments);
        }

        throw Unsupported(target);
    }

    /// <summary>The call arguments when the first parameter takes the built collection and every other one is optional (left at its default, e.g. a comparer); otherwise <see langword="null"/>.</summary>
    private static Expression[]? Arguments(ParameterInfo[] parameters, Expression built)
    {
        if (parameters.Length == 0 || !parameters[0].ParameterType.IsAssignableFrom(built.Type))
            return null;

        var arguments = new Expression[parameters.Length];
        arguments[0] = Expression.Convert(built, parameters[0].ParameterType);
        for (var i = 1; i < parameters.Length; i++)
        {
            if (!parameters[i].IsOptional)
                return null;
            arguments[i] = Expression.Constant(null, parameters[i].ParameterType);
        }

        return arguments;
    }

    private static InvalidOperationException Unsupported(Type target) =>
        new($"{target.Name} is a collection Phoney builds, but no factory method or constructor of it takes the generated elements. Use a List<T>, HashSet<T> or Dictionary<TKey, TValue> member, or give the member a value with .With(...).");
}
