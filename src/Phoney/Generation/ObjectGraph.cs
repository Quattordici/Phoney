using System.Collections;

namespace Phoney.Generation;

/// <summary>
/// A walk over a finished object graph, the way <see cref="BackReferences"/> and <see cref="ParentKeys"/> see it: through the members a model can read back
/// (settable ones and read-only collections that were filled), into each element of a collection (the values of a dictionary), past <see langword="null"/>
/// elements, with the models of the objects' runtime types so reflection and generated models agree. It keeps the objects above the one being visited, so a
/// member can be pointed at the nearest of them. A walk draws nothing.
/// </summary>
internal abstract class ObjectGraph
{
    // The objects above the one being visited, nearest last; the visited object is not among them, so it is never its own parent.
    private readonly List<object> _above = [];

    // How the walk got to the object being visited: the member of each object above it, and the element's position when the member is a collection (else -1).
    private readonly List<(string Member, int Element)> _steps = [];

    /// <summary>How many objects are above the one being visited: 0 for the root.</summary>
    protected int Depth => _above.Count;

    /// <summary>Visits one object of the graph.</summary>
    protected abstract void Visit(object node);

    /// <summary>The model of <paramref name="node"/>'s runtime type, or <see langword="null"/> for values, texts and types that have none.</summary>
    protected static IFakeModel? ModelOf(object node)
    {
        var type = node.GetType();
        return type.IsValueType || type == typeof(string) ? null : ModelRegistry.Get(type);
    }

    /// <summary>Whether the walk can read <paramref name="member"/> back and go on into what it holds.</summary>
    protected static bool IsWalkable(FakeMember member) => member.IsSettable || member.FillsExisting;

    /// <summary>
    /// Visits what <paramref name="owner"/>'s member holds, <paramref name="value"/> (each element that isn't <see langword="null"/> when the member is
    /// a collection), with <paramref name="owner"/> as the nearest object above it.
    /// </summary>
    protected void Descend(object owner, FakeMember member, object value)
    {
        _above.Add(owner);
        if (CollectionTypes.IsGeneratedCollection(member.Type))
        {
            var position = 0;
            foreach (var element in value is IDictionary dictionary ? dictionary.Values : (IEnumerable)value)
            {
                if (element is not null)
                {
                    _steps.Add((member.Name, position));
                    Visit(element);
                    _steps.RemoveAt(_steps.Count - 1);
                }

                position++;
            }
        }
        else
        {
            _steps.Add((member.Name, -1));
            Visit(value);
            _steps.RemoveAt(_steps.Count - 1);
        }

        _above.RemoveAt(_above.Count - 1);
    }

    /// <summary>
    /// The path from the root to <paramref name="member"/> of the object being visited: <c>Lines[2].Status</c>, with element positions, or without them
    /// (<c>Lines.Status</c>) when <paramref name="positions"/> is false.
    /// </summary>
    protected string PathTo(string member, bool positions)
    {
        if (_steps.Count == 0)
            return member;
        var path = new System.Text.StringBuilder();
        foreach (var (step, element) in _steps)
        {
            path.Append(step);
            if (positions && element >= 0)
                path.Append('[').Append(element.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            path.Append('.');
        }

        return path.Append(member).ToString();
    }

    /// <summary>The nearest object above the one being visited that is a <paramref name="type"/>, or <see langword="null"/>.</summary>
    protected object? Nearest(Type type)
    {
        for (var i = _above.Count - 1; i >= 0; i--)
        {
            if (type.IsInstanceOfType(_above[i]))
                return _above[i];
        }

        return null;
    }
}
