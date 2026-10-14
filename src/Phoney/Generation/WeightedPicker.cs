namespace Phoney.Generation;

/// <summary>Picks one of a fixed list of values by weight: the running totals are made once, a pick is one random number and a binary search, and allocates nothing.</summary>
internal sealed class WeightedPicker<TValue>
{
    private readonly TValue[] _values;
    private readonly double[] _cumulative;

    private WeightedPicker(TValue[] values, double[] cumulative)
    {
        _values = values;
        _cumulative = cumulative;
    }

    /// <summary>
    /// Checks and copies the choices. A weight of zero is never picked, so such choices are left out, which also keeps the running totals strictly increasing.
    /// </summary>
    /// <exception cref="ArgumentException">There are no choices, a weight is negative, not a number or infinite, or every weight is zero.</exception>
    public static WeightedPicker<TValue> Create(IEnumerable<TValue> values, IEnumerable<double> weights, string parameter)
    {
        var picked = new List<TValue>();
        var cumulative = new List<double>();
        var total = 0.0;
        var count = 0;
        using var valueEnumerator = values.GetEnumerator();
        using var weightEnumerator = weights.GetEnumerator();
        while (valueEnumerator.MoveNext() && weightEnumerator.MoveNext())
        {
            count++;
            var weight = weightEnumerator.Current;
            if (double.IsNaN(weight) || double.IsInfinity(weight))
                throw new ArgumentException($"The weight of choice {count} is {weight}; a weight must be a finite number.", parameter);
            if (weight < 0)
                throw new ArgumentException($"The weight of choice {count} is negative ({weight}); a weight is zero (never) or greater.", parameter);
            if (weight == 0)
                continue;

            total += weight;
            if (double.IsInfinity(total))
                throw new ArgumentException("The weights add up to more than a double can hold.", parameter);
            picked.Add(valueEnumerator.Current);
            cumulative.Add(total);
        }

        if (count == 0)
            throw new ArgumentException("Provide at least one choice.", parameter);
        if (picked.Count == 0)
            throw new ArgumentException("At least one weight must be greater than zero, or nothing can be picked.", parameter);
        return new WeightedPicker<TValue>(picked.ToArray(), cumulative.ToArray());
    }

    /// <summary>Picks a value; exactly one number is drawn.</summary>
    public TValue Pick(Randomizer random) => _values[random.WeightedIndex(_cumulative)];
}
