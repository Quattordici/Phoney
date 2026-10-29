namespace Phoney.Generation;

/// <summary>
/// Points object members that are still <see langword="null"/> at the object that contains them, for <c>WithBackReferences</c>: a cycle such as
/// <c>Order → Lines → Line.Order</c> stops with <see langword="null"/> while objects are created, and this pass closes it afterwards.
/// </summary>
/// <remarks>
/// It walks the graph like every <see cref="ObjectGraph"/>; constructor-only members (positional records) can be neither read nor set and are not visited.
/// Only reference types take part, and an object reached twice is visited once.
/// </remarks>
internal sealed class BackReferences : ObjectGraph
{
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

    private BackReferences()
    {
    }

    /// <summary>Sets every <see langword="null"/> object member of the graph below <paramref name="root"/> that can hold one of the objects above it.</summary>
    public static void Apply(object root) => new BackReferences().Visit(root);

    /// <inheritdoc/>
    protected override void Visit(object node)
    {
        if (ModelOf(node) is not { } model || !_visited.Add(node))
            return;

        for (var i = 0; i < model.Members.Count; i++)
        {
            var member = model.Members[i];
            if (!IsWalkable(member))
                continue;

            var value = model.GetMember(node, i);
            if (value is null)
            {
                if (member.IsSettable && member.IsNested && member.Type != typeof(object) && Nearest(Nullable.GetUnderlyingType(member.Type) ?? member.Type) is { } parent)
                    model.SetMember(node, i, parent);
            }
            else if (member.IsNested || CollectionTypes.IsGeneratedCollection(member.Type))
            {
                Descend(node, member, value);
            }
        }
    }
}
