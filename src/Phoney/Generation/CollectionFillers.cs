using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Phoney.Generation;

/// <summary>
/// Adds generated elements to the collection a read-only member already holds, for the reflection model: one compiled delegate per member that calls the typed
/// <see cref="FakeScope.AddAll{TElement}"/> helpers generated models call too, so nothing is reflected per element.
/// </summary>
internal static class CollectionFillers
{
    private static readonly MethodInfo AddCollection = typeof(FakeScope).GetMethods().Single(m => m.Name == nameof(FakeScope.AddAll) && m.GetGenericArguments().Length == 1);
    private static readonly MethodInfo AddDictionary = typeof(FakeScope).GetMethods().Single(m => m.Name == nameof(FakeScope.AddAll) && m.GetGenericArguments().Length == 2);

    /// <summary>A delegate that adds (or, with the flag, replaces by) the elements of a generated collection to the collection of a member of <paramref name="type"/>.</summary>
    [RequiresUnreferencedCode("Instantiates generic helpers over runtime types.")]
    [RequiresDynamicCode("Compiles an expression.")]
    public static Action<object?, object?, bool> Create(Type type)
    {
        var element = CollectionTypes.ElementType(type, out var key)!;
        var target = Expression.Parameter(typeof(object), "target");
        var values = Expression.Parameter(typeof(object), "values");
        var replace = Expression.Parameter(typeof(bool), "replace");

        Expression call;
        if (key is null)
        {
            call = Expression.Call(
                AddCollection.MakeGenericMethod(element),
                Expression.Convert(target, typeof(ICollection<>).MakeGenericType(element)),
                Expression.Convert(values, typeof(IEnumerable<>).MakeGenericType(element)),
                replace);
        }
        else
        {
            call = Expression.Call(
                AddDictionary.MakeGenericMethod(key, element),
                Expression.Convert(target, typeof(IDictionary<,>).MakeGenericType(key, element)),
                Expression.Convert(values, typeof(IEnumerable<>).MakeGenericType(typeof(KeyValuePair<,>).MakeGenericType(key, element))),
                replace);
        }

        return Expression.Lambda<Action<object?, object?, bool>>(call, target, values, replace).Compile();
    }
}
