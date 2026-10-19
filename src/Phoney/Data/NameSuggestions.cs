namespace Phoney.Data;

/// <summary>
/// "Did you mean …?" for error messages: the known name closest to a misspelt one. Only error paths call it, so nothing is built or
/// paid for until a name fails to resolve.
/// </summary>
internal static class NameSuggestions
{
    /// <summary>
    /// <c> Did you mean 'x'?</c> for the candidate closest to <paramref name="name"/> (case-insensitive edit distance, adjacent swaps count
    /// as one edit), or <c>""</c> when none is close enough to be a likely typo. Ties go to the ordinally first candidate, so the message is stable.
    /// </summary>
    public static string For(string name, IEnumerable<string> candidates)
    {
        var limit = Math.Clamp(name.Length / 5, 1, 3);
        string? best = null;
        var bestDistance = limit + 1;
        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Length - name.Length) >= bestDistance)
                continue;
            var distance = Distance(name, candidate);
            if (distance < bestDistance || (distance == bestDistance && best is not null && string.CompareOrdinal(candidate, best) < 0))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best is null || bestDistance == 0 && string.Equals(best, name, StringComparison.Ordinal) ? "" : $" Did you mean '{best}'?";
    }

    /// <summary>Optimal string alignment distance, ignoring case.</summary>
    private static int Distance(string a, string b)
    {
        // Three rows: the one before the previous is needed for transpositions.
        var older = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = Same(a[i - 1], b[j - 1]) ? 0 : 1;
                var value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && Same(a[i - 1], b[j - 2]) && Same(a[i - 2], b[j - 1]))
                    value = Math.Min(value, older[j - 2] + 1);
                current[j] = value;
            }

            (older, previous, current) = (previous, current, older);
        }

        return previous[b.Length];
    }

    private static bool Same(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
}
