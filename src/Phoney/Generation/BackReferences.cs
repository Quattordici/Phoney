using System.Collections;

namespace Phoney.Generation;

/// <summary>
/// Points object members that are still <see langword="null"/> at the object that contains them, for <c>WithBackReferences</c>: a cycle such as
/// <c>Order → Lines → Line.Order</c> stops with <see langword="null"/> while objects are created, and this pass closes it afterwards.
/// </summary>
/// <remarks>
/// It walks the graph through settable members (properties with a setter or <see langword="init"/>, public fields) with the models of the
/// objects' runtime types, so reflection and generated models see the same graph; constructor-only members (positional records) can be neither
/// read nor set and are not visited. Only reference types take part.
/// </remarks>
internal static class BackReferences
{
    /// <summary>Sets every <see langword="null"/> object member of the graph below <paramref name="root"/> that can hold one of the objects above it.</summary>
    public static void Apply(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root, [], visited);
    }

    private static void Visit(object node, List<object> ancestors, HashSet<object> visited)
    {
        var type = node.GetType();
        if (type.IsValueType || type == typeof(string) || !visited.Add(node) || ModelRegistry.Get(type) is not { } model)
            return;

        ancestors.Add(node);
        for (var i = 0; i < model.Members.Count; i++)
        {
            if (model.Members[i] is { IsSettable: true } or { FillsExisting: true })
                VisitMember(node, model, i, ancestors, visited);
        }

        ancestors.RemoveAt(ancestors.Count - 1);
    }

    /// <summary>Points a <see langword="null"/> object member at the nearest ancestor it can hold, or walks on into the object or collection it holds.</summary>
    private static void VisitMember(object node, IFakeModel model, int index, List<object> ancestors, HashSet<object> visited)
    {
        var member = model.Members[index];
        var value = model.GetMember(node, index);
        if (value is null)
        {
            if (member.IsSettable && member.IsNested && member.Type != typeof(object) && Nearest(ancestors, member.Type) is { } parent)
                model.SetMember(node, index, parent);
        }
        else if (member.IsNested)
        {
            Visit(value, ancestors, visited);
        }
        else if (CollectionTypes.IsGeneratedCollection(member.Type))
        {
            foreach (var element in Elements(value))
            {
                if (element is not null)
                    Visit(element, ancestors, visited);
            }
        }
    }

    /// <summary>The closest object above <paramref name="ancestors"/>' last one (the object itself is not its own parent) that a member of <paramref name="memberType"/> can hold.</summary>
    private static object? Nearest(List<object> ancestors, Type memberType)
    {
        var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
        for (var i = ancestors.Count - 2; i >= 0; i--)
        {
            if (type.IsInstanceOfType(ancestors[i]))
                return ancestors[i];
        }

        return null;
    }

    /// <summary>The elements of a collection; the values of a dictionary.</summary>
    private static IEnumerable Elements(object collection) => collection is IDictionary dictionary ? dictionary.Values : (IEnumerable)collection;
}
