using System.Numerics;
using System.Runtime.CompilerServices;

namespace Phoney;

/// <summary>
/// Fast, seedable source of randomness used by every Phoney module.
/// </summary>
/// <remarks>
/// Uses the xoshiro256** algorithm seeded through SplitMix64, so a given seed produces the same
/// sequence on every platform and .NET version. Instances are not thread-safe; create one per thread
/// (every <see cref="Faker"/> owns its own) instead of sharing.
/// </remarks>
public sealed class SeededRandom
{
    private const string UpperAlpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerAlpha = "abcdefghijklmnopqrstuvwxyz";

    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Creates a randomizer with a random seed.</summary>
#pragma warning disable RS0030 // An unseeded randomizer is random by definition; seeded ones never reach this.
    public SeededRandom() : this(System.Random.Shared.NextInt64())
#pragma warning restore RS0030
    {
    }

    /// <summary>Creates a randomizer that produces a reproducible sequence for <paramref name="seed"/>.</summary>
    public SeededRandom(long seed) => Reseed(seed);

    /// <summary>The seed this randomizer was last (re)seeded with.</summary>
    public long Seed { get; private set; }

    /// <summary>Restarts the sequence from <paramref name="seed"/>.</summary>
    public void Reseed(long seed)
    {
        Seed = seed;
        var x = (ulong)seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    /// <summary>Derives an independent, well-mixed seed for item <paramref name="index"/> of a sequence started from <paramref name="seed"/>.</summary>
    public static long DeriveSeed(long seed, long index)
    {
        var x = (ulong)seed ^ ((ulong)index * 0x9E3779B97F4A7C15UL);
        return (long)SplitMix64(ref x);
    }

    /// <summary>Returns 64 random bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Returns an integer in <c>[0, count)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Index(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Count must be positive.");
        return (int)Bounded((ulong)count);
    }

    /// <summary>Returns an integer between <paramref name="min"/> and <paramref name="max"/>, both inclusive.</summary>
    public int Int(int min = 0, int max = int.MaxValue)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        return (int)(min + (long)Bounded((ulong)((long)max - min) + 1));
    }

    /// <summary>Returns a long between <paramref name="min"/> and <paramref name="max"/>, both inclusive.</summary>
    public long Long(long min = 0, long max = long.MaxValue)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        var range = (ulong)(max - min) + 1;
        return range == 0 ? (long)NextUInt64() : min + (long)Bounded(range);
    }

    /// <summary>Returns an unsigned long between <paramref name="min"/> and <paramref name="max"/>, both inclusive.</summary>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public ulong ULong(ulong min = 0, ulong max = ulong.MaxValue)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        var range = max - min + 1;
        return range == 0 ? NextUInt64() : min + Bounded(range);
    }

    /// <summary>Returns a double in <c>[0, 1)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Double() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>
    /// Returns a number from a normal (Gaussian) distribution, with the Box–Muller transform. It always draws two numbers, whatever the result, so what is drawn after it does not
    /// depend on its value, and it keeps no state, so reseeding gives the same sequence. The logarithm and cosine are made without the system's maths library
    /// (see <see cref="PureMath"/>), so a seed gives the same numbers on every platform.
    /// </summary>
    /// <param name="mean">The centre of the distribution.</param>
    /// <param name="standardDeviation">How far values spread: about 68 % lie within one deviation of the mean, 95 % within two. Zero gives the mean every time.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mean"/> is not finite, or <paramref name="standardDeviation"/> is negative or not finite.</exception>
    public double Normal(double mean = 0, double standardDeviation = 1)
    {
        if (!double.IsFinite(mean))
            throw new ArgumentOutOfRangeException(nameof(mean), mean, "The mean must be a finite number.");
        if (!double.IsFinite(standardDeviation) || standardDeviation < 0)
            throw new ArgumentOutOfRangeException(nameof(standardDeviation), standardDeviation, "The standard deviation must be a finite number that is not negative.");

        var radius = Math.Sqrt(-2.0 * PureMath.Ln(1.0 - Double())); // 1 - [0, 1) is in (0, 1]
        var turns = Double();
        return mean + (standardDeviation * radius * PureMath.CosTurns(turns));
    }

    /// <summary>Returns a double in <c>[min, max)</c>.</summary>
    public double Double(double min, double max)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        return min + (Double() * (max - min));
    }

    /// <summary>Returns a decimal in <c>[min, max)</c>.</summary>
    public decimal Decimal(decimal min = 0m, decimal max = 1m)
    {
        if (max < min)
            throw new ArgumentException($"Max {max} must be greater than or equal to min {min}.", nameof(max));
        return min + ((decimal)Double() * (max - min));
    }

    /// <summary>Returns <see langword="true"/> with the given <paramref name="probability"/> (0..1).</summary>
    public bool Bool(double probability = 0.5) => probability switch
    {
        <= 0 => false,
        >= 1 => true,
        _ => Double() < probability,
    };

    /// <summary>Returns a random element of <paramref name="items"/>.</summary>
    public T Element<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("Cannot pick an element from an empty collection.", nameof(items));
        return items[Index(items.Count)];
    }

    /// <summary>Returns a random element of <paramref name="items"/>, which can be any sequence (a set, dictionary keys, a query).</summary>
    /// <remarks>Draws like the list overload over the elements in enumeration order, so use a source whose order is stable to get the same element for a seed: lists, arrays, <c>HashSet&lt;T&gt;</c> and <c>Dictionary&lt;K,V&gt;</c> (insertion order) are, hash-ordered sets such as <c>ImmutableHashSet&lt;string&gt;</c> and <c>FrozenSet&lt;string&gt;</c> are not (pass a comparer to the overload that orders them first).</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="items"/> is empty.</exception>
    public T Element<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items is IReadOnlyList<T> list ? Element(list) : Element<T>(items.ToArray());
    }

    /// <summary>
    /// Returns a random element of <paramref name="items"/> after ordering them with <paramref name="comparer"/>, so the result for a seed doesn't depend on the order the source
    /// happens to enumerate in (hash-ordered sets such as <c>ImmutableHashSet&lt;string&gt;</c> and <c>FrozenSet&lt;string&gt;</c> change order from one process to the next).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="items"/> is empty.</exception>
    public T Element<T>(IEnumerable<T> items, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(comparer);
        var ordered = items.ToArray();
        Array.Sort(ordered, comparer);
        return Element<T>(ordered);
    }

    /// <summary>Returns a random element of <paramref name="items"/>.</summary>
    public T Element<T>(ReadOnlySpan<T> items)
    {
        if (items.IsEmpty)
            throw new ArgumentException("Cannot pick an element from an empty collection.", nameof(items));
        return items[Index(items.Length)];
    }

    /// <summary>Returns a random value of the enum <typeparamref name="TEnum"/>.</summary>
    public TEnum Enum<TEnum>() where TEnum : struct, Enum => Element<TEnum>(EnumValues<TEnum>.All);

    /// <summary>Returns <paramref name="count"/> distinct random elements of <paramref name="items"/> in random order.</summary>
    public T[] Elements<T>(IReadOnlyList<T> items, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        count = Math.Clamp(count, 0, items.Count);
        var copy = items.ToArray();
        // Partial Fisher-Yates: only the first `count` slots need shuffling.
        for (var i = 0; i < count; i++)
        {
            var j = i + Index(copy.Length - i);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy.AsSpan(0, count).ToArray();
    }

    /// <summary>Returns the elements of <paramref name="items"/> in random order as a new array; the source is left as it is.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    public T[] Shuffled<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        Shuffle(copy.AsSpan());
        return copy;
    }

    /// <summary>
    /// Returns the elements of <paramref name="items"/> in random order as a new array, after ordering them with <paramref name="comparer"/> so that the result for a seed doesn't depend on
    /// the order the source enumerates in.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
    public T[] Shuffled<T>(IEnumerable<T> items, IComparer<T> comparer)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(comparer);
        var copy = items.ToArray();
        Array.Sort(copy, comparer);
        Shuffle(copy.AsSpan());
        return copy;
    }

    /// <summary>Shuffles <paramref name="items"/> in place.</summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (var i = items.Length - 1; i > 0; i--)
        {
            var j = Index(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Picks a value with probability proportional to its weight, e.g. <c>Weighted(("gold", 1), ("silver", 3))</c>.</summary>
    /// <exception cref="ArgumentException">There are no items or a weight is not positive.</exception>
    public T Weighted<T>(params (T Value, double Weight)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Length == 0)
            throw new ArgumentException("Cannot pick an element from an empty collection.", nameof(items));
        var total = 0.0;
        foreach (var (_, weight) in items)
        {
            if (weight <= 0 || double.IsNaN(weight))
                throw new ArgumentException("Weights must be positive.", nameof(items));
            total += weight;
        }

        var target = Double() * total;
        foreach (var (value, weight) in items)
        {
            target -= weight;
            if (target < 0)
                return value;
        }

        return items[^1].Value; // rounding: the target landed exactly on the total
    }

    /// <summary>
    /// Picks an index according to <paramref name="cumulativeWeights"/>, the running totals of positive weights (<c>[1, 4, 10]</c> for the weights 1, 3 and 6), with one binary search
    /// and exactly one number drawn. For a pick that is made many times from the same weights this is cheaper than <see cref="Weighted{T}"/>, which adds the weights up on every call.
    /// </summary>
    /// <param name="cumulativeWeights">Strictly increasing running totals; the last is the total weight.</param>
    public int WeightedIndex(ReadOnlySpan<double> cumulativeWeights)
    {
        var target = Double() * cumulativeWeights[^1];
        // First running total strictly greater than the target.
        int lo = 0, hi = cumulativeWeights.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cumulativeWeights[mid] > target)
                hi = mid;
            else
                lo = mid + 1;
        }

        return lo; // also when rounding put the target exactly on the total: the last one
    }

    /// <summary>Picks an index according to <paramref name="cumulativeWeights"/> (running totals of the weights).</summary>
    public int WeightedIndex(ReadOnlySpan<int> cumulativeWeights)
    {
        var total = cumulativeWeights[^1];
        var target = Index(total);
        // First cumulative weight strictly greater than the target.
        int lo = 0, hi = cumulativeWeights.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (cumulativeWeights[mid] > target)
                hi = mid;
            else
                lo = mid + 1;
        }

        return lo;
    }

    /// <summary>Returns a random decimal digit character.</summary>
    public char Digit() => (char)('0' + Index(10));

    /// <summary>Returns a random letter <c>a-z</c> (or <c>A-Z</c>).</summary>
    public char Letter(bool upper = false) => (upper ? UpperAlpha : LowerAlpha)[Index(26)];

    /// <summary>
    /// Replaces symbols in <paramref name="pattern"/>: <c>#</c> → digit, <c>?</c> → upper-case letter,
    /// <c>*</c> → digit or upper-case letter. All other characters are kept.
    /// </summary>
    public string ReplaceSymbols(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return string.Create(pattern.Length, (pattern, this), static (span, state) =>
        {
            var (p, r) = state;
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = p[i] switch
                {
                    '#' => r.Digit(),
                    '?' => r.Letter(upper: true),
                    '*' => r.Bool() ? r.Digit() : r.Letter(upper: true),
                    var c => c,
                };
            }
        });
    }

    /// <summary>Returns a random version 4 <see cref="System.Guid"/>.</summary>
    public Guid Guid()
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, NextUInt64());
        BitConverter.TryWriteBytes(bytes[8..], NextUInt64());
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40); // version 4 (byte order of the Guid(ReadOnlySpan<byte>) layout)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }

    /// <summary>Fills <paramref name="buffer"/> with random bytes.</summary>
    public void Bytes(Span<byte> buffer)
    {
        while (buffer.Length >= 8)
        {
            BitConverter.TryWriteBytes(buffer, NextUInt64());
            buffer = buffer[8..];
        }

        if (buffer.Length > 0)
        {
            Span<byte> last = stackalloc byte[8];
            BitConverter.TryWriteBytes(last, NextUInt64());
            last[..buffer.Length].CopyTo(buffer);
        }
    }

    // Lemire's nearly divisionless method: unbiased integer in [0, range).
    /// <summary>Unbiased integer in <c>[0, range)</c> (Lemire's nearly divisionless method).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ulong Bounded(ulong range)
    {
        var high = Math.BigMul(NextUInt64(), range, out var low);
        if (low < range)
        {
            var threshold = (0 - range) % range;
            while (low < threshold)
                high = Math.BigMul(NextUInt64(), range, out low);
        }

        return high;
    }

    /// <summary>SplitMix64 step, used to expand a 64-bit seed into the xoshiro state.</summary>
    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
