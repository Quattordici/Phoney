namespace Phoney.Generation;

/// <summary>
/// Takes a random subset of a fixed list: distinct positions (Floyd's algorithm: exactly one draw per value, no work proportional to the list), returned in the order of the list.
/// A subset of a size range first draws its size. The list is copied when the picker is made.
/// </summary>
internal sealed class SubsetPicker<TValue>
{
    private const int LinearSearchLimit = 24;
    private const int StackLimit = 128;

    private readonly TValue[] _values;
    private readonly int _min;
    private readonly int _max;

    private SubsetPicker(TValue[] values, SubsetSize size)
    {
        _values = values;
        _min = size.Min;
        _max = size.Max;
    }

    /// <summary>Checks and copies the list.</summary>
    /// <exception cref="ArgumentException">The size is larger than the list.</exception>
    public static SubsetPicker<TValue> Create(IReadOnlyList<TValue> values, SubsetSize size, string parameter)
    {
        ArgumentNullException.ThrowIfNull(values, parameter);
        if (size.Max > values.Count)
            throw new ArgumentException($"Cannot take {size.Max} of the {values.Count} values: a subset is never larger than the list it is taken from.", parameter);
        return new SubsetPicker<TValue>([.. values], size);
    }

    /// <summary>A subset as a list.</summary>
    public List<TValue> List(Randomizer random)
    {
        var size = DrawSize(random);
        Span<int> positions = size <= StackLimit ? stackalloc int[size] : new int[size];
        DrawPositions(random, positions);
        var list = new List<TValue>(size);
        foreach (var position in positions)
            list.Add(_values[position]);
        return list;
    }

    /// <summary>A subset as an array.</summary>
    public TValue[] Array(Randomizer random)
    {
        var size = DrawSize(random);
        Span<int> positions = size <= StackLimit ? stackalloc int[size] : new int[size];
        DrawPositions(random, positions);
        var array = new TValue[size];
        for (var i = 0; i < array.Length; i++)
            array[i] = _values[positions[i]];
        return array;
    }

    /// <summary>A subset as a set (equal values in the list make the set smaller).</summary>
    public HashSet<TValue> Set(Randomizer random)
    {
        var size = DrawSize(random);
        Span<int> positions = size <= StackLimit ? stackalloc int[size] : new int[size];
        DrawPositions(random, positions);
        var set = new HashSet<TValue>(size);
        foreach (var position in positions)
            set.Add(_values[position]);
        return set;
    }

    /// <summary>The size of the next subset: fixed, or drawn from the range.</summary>
    private int DrawSize(Randomizer random) => _min == _max ? _min : random.Int(_min, _max);

    /// <summary>Fills <paramref name="positions"/> with that many distinct positions of the list, ascending.</summary>
    private void DrawPositions(Randomizer random, Span<int> positions)
    {
        var size = positions.Length;
        if (size == 0)
            return;

        var n = _values.Length;
        HashSet<int>? taken = size > LinearSearchLimit ? new HashSet<int>(size) : null;
        var count = 0;
        for (var j = n - size; j < n; j++)
        {
            var t = random.Index(j + 1);
            var already = taken is not null ? taken.Contains(t) : positions[..count].Contains(t);
            var chosen = already ? j : t; // j is not taken yet, since only positions below j + 1 have been offered
            positions[count++] = chosen;
            taken?.Add(chosen);
        }

        positions.Sort();
    }
}
