using Phony.Data;

namespace Phony.Modules;

/// <summary>Real words of the locale's language by part of speech (faker.js <c>word</c>).</summary>
/// <remarks>
/// Every method accepts an optional length range. When no word fits, the <c>strategy</c> argument decides the
/// fallbacks apply as in faker.js; see <see cref="WordLengthStrategy"/>.
/// </remarks>
public sealed class WordModule : FakerModule
{
    private static readonly int[] PartsOfSpeech =
    [
        DataKeys.WordAdjective, DataKeys.WordAdverb, DataKeys.WordConjunction, DataKeys.WordInterjection,
        DataKeys.WordNoun, DataKeys.WordPreposition, DataKeys.WordVerb,
    ];

    internal WordModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an adjective.</summary>
    /// <param name="minLength">Minimum word length.</param>
    /// <param name="maxLength">Maximum word length.</param>
    /// <param name="strategy">What to do when no word has a matching length.</param>
    public string Adjective(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordAdjective, minLength, maxLength, strategy);

    /// <summary>Returns an adverb.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Adverb(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordAdverb, minLength, maxLength, strategy);

    /// <summary>Returns a conjunction.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Conjunction(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordConjunction, minLength, maxLength, strategy);

    /// <summary>Returns an interjection.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Interjection(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordInterjection, minLength, maxLength, strategy);

    /// <summary>Returns a noun.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Noun(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordNoun, minLength, maxLength, strategy);

    /// <summary>Returns a preposition.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Preposition(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordPreposition, minLength, maxLength, strategy);

    /// <summary>Returns a verb.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Verb(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail) =>
        Pick(DataKeys.WordVerb, minLength, maxLength, strategy);

    /// <summary>Returns a word of any part of speech the locale has data for.</summary>
    /// <inheritdoc cref="Adjective" path="/param"/>
    public string Sample(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail)
    {
        Span<int> order = stackalloc int[PartsOfSpeech.Length];
        PartsOfSpeech.CopyTo(order);
        Random.Shuffle(order);
        foreach (var key in order)
        {
            if (Faker.Data.TryStrings(key) is { } entry && WordPicker.TryPick(entry, Random, minLength, maxLength, strategy, out var word))
                return word;
        }

        throw new PhonyDataException($"No matching word data available for locale '{Faker.Locale}'.");
    }

    /// <summary>Returns <paramref name="count"/> space-separated words (1–3 when not given).</summary>
    public string Words(int? count = null)
    {
        var n = count ?? Random.Int(1, 3);
        var words = new string[Math.Max(0, n)];
        for (var i = 0; i < words.Length; i++)
            words[i] = Sample();
        return string.Join(' ', words);
    }

    /// <summary>Picks a word with the length constraint or throws a descriptive exception.</summary>
    private string Pick(int key, int? minLength, int? maxLength, WordLengthStrategy strategy)
    {
        var entry = Faker.Data.Strings(key);
        if (WordPicker.TryPick(entry, Random, minLength, maxLength, strategy, out var word))
            return word;
        throw new PhonyDataException($"No words of length {minLength}-{maxLength} found in '{KeyRegistry.PathOf(key)}' for locale '{Faker.Locale}'.");
    }
}

/// <summary>What to do when no word matches the requested length.</summary>
public enum WordLengthStrategy
{
    /// <summary>Throw a <see cref="PhonyDataException"/>.</summary>
    Fail,

    /// <summary>Use the words whose length is closest to the requested range.</summary>
    Closest,

    /// <summary>Use the shortest words.</summary>
    Shortest,

    /// <summary>Use the longest words.</summary>
    Longest,

    /// <summary>Ignore the length.</summary>
    AnyLength,
}

/// <summary>
/// Picks a word with a length constraint without building filtered lists: one pass counts the candidates,
/// a second pass returns the randomly chosen one.
/// </summary>
internal static class WordPicker
{
    /// <summary>Picks a word from <paramref name="entry"/> honouring the length range and fallback strategy.</summary>
    public static bool TryPick(StringsEntry entry, Randomizer random, int? minLength, int? maxLength, WordLengthStrategy strategy, out string word)
    {
        word = "";
        var values = (string[])entry.Values;
        if (minLength is null && maxLength is null)
        {
            if (strategy is WordLengthStrategy.Shortest or WordLengthStrategy.Longest)
                return PickByLength(values, random, strategy == WordLengthStrategy.Shortest ? MinLength(values) : MaxLength(values), out word);
            word = values[random.Index(values.Length)];
            return true;
        }

        var min = minLength ?? 0;
        var max = maxLength ?? int.MaxValue;
        if (PickInRange(values, random, min, max, out word))
            return true;

        switch (strategy)
        {
            case WordLengthStrategy.AnyLength:
                word = values[random.Index(values.Length)];
                return true;
            case WordLengthStrategy.Shortest:
                return PickByLength(values, random, MinLength(values), out word);
            case WordLengthStrategy.Longest:
                return PickByLength(values, random, MaxLength(values), out word);
            case WordLengthStrategy.Closest:
            {
                // Distance of the nearest length outside [min, max]; words at that distance on either side qualify.
                var best = int.MaxValue;
                foreach (var v in values)
                    best = Math.Min(best, v.Length < min ? min - v.Length : v.Length - max);
                var count = 0;
                foreach (var v in values)
                    if (Distance(v.Length, min, max) == best)
                        count++;
                var target = random.Index(count);
                foreach (var v in values)
                {
                    if (Distance(v.Length, min, max) == best && target-- == 0)
                    {
                        word = v;
                        return true;
                    }
                }

                return false;
            }

            default:
                return false;
        }

        static int Distance(int length, int min, int max) => length < min ? min - length : length - max;
    }

    /// <summary>Picks uniformly among words with a length in range, without allocating.</summary>
    private static bool PickInRange(string[] values, Randomizer random, int min, int max, out string word)
    {
        var count = 0;
        foreach (var v in values)
            if (v.Length >= min && v.Length <= max)
                count++;
        word = "";
        if (count == 0)
            return false;
        var target = random.Index(count);
        foreach (var v in values)
        {
            if (v.Length >= min && v.Length <= max && target-- == 0)
            {
                word = v;
                return true;
            }
        }

        return false;
    }

    /// <summary>Picks among words of exactly <paramref name="length"/> characters.</summary>
    private static bool PickByLength(string[] values, Randomizer random, int length, out string word) =>
        PickInRange(values, random, length, length, out word);

    /// <summary>Length of the shortest word.</summary>
    private static int MinLength(string[] values) => values.Min(v => v.Length);

    /// <summary>Length of the longest word.</summary>
    private static int MaxLength(string[] values) => values.Max(v => v.Length);
}
