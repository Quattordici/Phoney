namespace Phoney.Generation;

/// <summary>
/// A combination of members that must be unique together (<c>Unique(x =&gt; new { x.UserId, x.RoleId })</c>). It is read from the finished object,
/// and an object whose combination was produced before is generated again as a whole, like a duplicate of a unique identity member.
/// </summary>
/// <param name="members">The member indices, in the order they were selected.</param>
/// <param name="names">The member names, for messages.</param>
/// <param name="recorded">Per member: whether its value is taken from <see cref="FakeScope"/> as it was produced (constructor parameters can't be read back).</param>
/// <param name="maxAttempts">How often an object may be generated again for a new combination.</param>
internal sealed class UniqueCombination(int[] members, string[] names, bool[] recorded, int maxAttempts)
{
    private readonly HashSet<object?[]> _seen = new(ValuesComparer.Instance);

    /// <summary>How often an object may be generated again for a new combination.</summary>
    public int MaxAttempts { get; } = maxAttempts;

    /// <summary>"combination of UserId and RoleId", for messages.</summary>
    public string Description { get; } = $"combination of {string.Join(", ", names[..^1])} and {names[^1]}";

    /// <summary>The values of the combination on an object: recorded members from <paramref name="scope"/>, the others through <paramref name="read"/>.</summary>
    public object?[] Read(FakeScope scope, Func<int, object?> read)
    {
        var values = new object?[members.Length];
        for (var i = 0; i < members.Length; i++)
            values[i] = recorded[i] && scope.Ledger is { } ledger && ledger.TryGetKeyPart(members[i], out var value) ? value : read(members[i]);
        return values;
    }

    /// <summary>Whether <paramref name="values"/> were claimed before. Callers hold the claim lock of the run.</summary>
    public bool IsClaimed(object?[] values) => _seen.Contains(values);

    /// <summary>Records <paramref name="values"/>. Callers hold the claim lock of the run.</summary>
    public void Claim(object?[] values) => _seen.Add(values);

    /// <summary>Compares combinations element by element with each value's own equality.</summary>
    private sealed class ValuesComparer : IEqualityComparer<object?[]>
    {
        public static readonly ValuesComparer Instance = new();

        public bool Equals(object?[]? x, object?[]? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is null || y is null || x.Length != y.Length)
                return false;
            for (var i = 0; i < x.Length; i++)
            {
                if (!Equals(x[i], y[i]))
                    return false;
            }

            return true;
        }

        public int GetHashCode(object?[] obj)
        {
            var hash = default(HashCode);
            foreach (var value in obj)
                hash.Add(value);
            return hash.ToHashCode();
        }
    }
}
