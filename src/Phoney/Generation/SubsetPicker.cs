namespace Phoney.Generation;

/// <summary>
/// Takes a random subset of a fixed list: distinct positions, returned in the order of the list. Without weights every value is equally likely (Floyd's algorithm: exactly one draw
/// per value, no work proportional to the list); with weights the values are taken one at a time, each time by weight among those left (one draw per value, work proportional to the list
/// for each of them), so a heavier value is more likely to be in the subset. A subset of a size range first draws its size. The list is copied when the picker is made.
/// </summary>
internal sealed class SubsetPicker<TValue>
{
    private const int LinearSearchLimit = 24;
    private const int StackLimit = 128;

    private readonly TValue[] _values;
    private readonly double[]? _weights; // aligned with _values, all above zero; null when every value is equally likely
    private readonly int _min;
    private readonly int _max;

    private SubsetPicker(TValue[] values, double[]? weights, SubsetSize size)
    {
        _values = values;
        _weights = weights;
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
        return new SubsetPicker<TValue>([.. values], null, size);
    }

    /// <summary>
    /// Checks and copies the list and its weights. A value with weight zero is never in a subset, so it is left out.
    /// </summary>
    /// <exception cref="ArgumentException">The lists differ in length, a weight is negative, not a number or infinite, or the size is larger than the values that can be taken.</exception>
    public static SubsetPicker<TValue> Create(IReadOnlyList<TValue> values, IReadOnlyList<double> weights, SubsetSize size, string parameter)
    {
        ArgumentNullException.ThrowIfNull(values, parameter);
        ArgumentNullException.ThrowIfNull(weights);
        if (values.Count != weights.Count)
            throw new ArgumentException($"Provide one weight per value: {values.Count} values and {weights.Count} weights.", nameof(weights));
        if (size.Max > values.Count)
            throw new ArgumentException($"Cannot take {size.Max} of the {values.Count} values: a subset is never larger than the list it is taken from.", parameter);

        var choices = WeightedPicker<TValue>.PositiveChoices(values, weights, nameof(weights));
        if (size.Max > choices.Values.Length)
        {
            throw new ArgumentException(
                $"Cannot take {size.Max} of the {choices.Values.Length} values with a weight above zero: a value with weight zero is never included.", parameter);
        }

        return new SubsetPicker<TValue>(choices.Values, choices.Weights, size);
    }

    /// <summary>A subset as a list.</summary>
    public List<TValue> List(SeededRandom random)
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
    public TValue[] Array(SeededRandom random)
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
    public HashSet<TValue> Set(SeededRandom random)
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
    private int DrawSize(SeededRandom random) => _min == _max ? _min : random.Int(_min, _max);

    /// <summary>Fills <paramref name="positions"/> with that many distinct positions of the list, ascending.</summary>
    private void DrawPositions(SeededRandom random, Span<int> positions)
    {
        var size = positions.Length;
        if (size == 0)
            return;

        if (_weights is not null)
        {
            DrawWeightedPositions(random, positions);
            return;
        }

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

    /// <summary>
    /// Takes the positions one at a time, each by weight among those not taken yet (one draw each); nothing is retried, so very uneven weights cost no more than even ones.
    /// </summary>
    private void DrawWeightedPositions(SeededRandom random, Span<int> positions)
    {
        var weights = _weights!;
        var n = weights.Length;
        Span<bool> taken = n <= StackLimit * 2 ? stackalloc bool[n] : new bool[n];
        taken.Clear(); // the module skips zeroing stack buffers
        for (var count = 0; count < positions.Length; count++)
        {
            var remaining = 0.0;
            for (var i = 0; i < n; i++)
            {
                if (!taken[i])
                    remaining += weights[i];
            }

            var target = random.Double() * remaining;
            var chosen = -1;
            var lastFree = -1;
            for (var i = 0; i < n; i++)
            {
                if (taken[i])
                    continue;
                lastFree = i;
                target -= weights[i];
                if (target < 0)
                {
                    chosen = i;
                    break;
                }
            }

            chosen = chosen < 0 ? lastFree : chosen; // rounding put the target exactly on the total: the last one left
            taken[chosen] = true;
            positions[count] = chosen;
        }

        positions.Sort();
    }
}
