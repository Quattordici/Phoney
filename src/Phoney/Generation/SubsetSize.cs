namespace Phoney;

/// <summary>
/// How many values a random subset has: exactly one number (<c>3</c>) or any number in a range (<c>(1, 4)</c>, both inclusive). Both convert implicitly, so
/// <c>.WithOneOf(x =&gt; x.Roles, roles, 2)</c> and <c>.WithOneOf(x =&gt; x.Roles, roles, (1, 4))</c> read naturally.
/// </summary>
public readonly record struct SubsetSize
{
    /// <summary>Creates a size from the smallest and largest number of values.</summary>
    /// <param name="min">The fewest values; zero gives an empty subset.</param>
    /// <param name="max">The most values.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public SubsetSize(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        if (max < min)
            throw new ArgumentException($"The smallest subset size ({min}) must be at most the largest ({max}).", nameof(max));
        Min = min;
        Max = max;
    }

    /// <summary>The fewest values.</summary>
    public int Min { get; }

    /// <summary>The most values.</summary>
    public int Max { get; }

    /// <summary>A subset of exactly <paramref name="count"/> values.</summary>
    public static implicit operator SubsetSize(int count) => new(count, count);

    /// <summary>A subset of <c>Min</c> to <c>Max</c> values, both inclusive.</summary>
    public static implicit operator SubsetSize((int Min, int Max) range) => new(range.Min, range.Max);

    /// <summary>A subset of exactly <paramref name="count"/> values.</summary>
    public static SubsetSize FromInt32(int count) => count;

    /// <summary>A subset of <c>Min</c> to <c>Max</c> values, both inclusive.</summary>
    public static SubsetSize FromTuple((int Min, int Max) range) => range;
}
