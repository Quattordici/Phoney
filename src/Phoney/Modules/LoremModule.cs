using Phoney.Data;
using Phoney.Templates;

namespace Phoney.Modules;

/// <summary>Placeholder text: words, sentences and paragraphs (faker.js <c>lorem</c>).</summary>
public sealed class LoremModule : FakerModule
{
    internal LoremModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns one placeholder word, optionally with a length between <paramref name="minLength"/> and <paramref name="maxLength"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxLength"/> is less than <paramref name="minLength"/>.</exception>
    /// <exception cref="PhoneyDataException">No word has a matching length and <paramref name="strategy"/> is <see cref="WordLengthStrategy.Fail"/>.</exception>
    public string Word(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail)
    {
        WordPicker.ValidateLengths(minLength, maxLength);
        if (WordPicker.TryPick(Faker.Data.Strings(DataKeys.LoremWord), Random, minLength, maxLength, strategy, out var word))
            return word;
        throw new PhoneyDataException($"No lorem words {WordPicker.DescribeLengths(minLength, maxLength)} in locale '{Faker.Locale}'.");
    }

    /// <summary>Returns <paramref name="count"/> space-separated words.</summary>
    public string Words(int count = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var text = new TextBuilder(stackalloc char[256]);
        AppendWords(ref text, count);
        return text.ToStringAndDispose();
    }

    /// <summary>Returns between <paramref name="min"/> and <paramref name="max"/> space-separated words (faker.js <c>words({ min, max })</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public string Words(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        return Words(Count(min, max, nameof(max)));
    }

    /// <summary>Returns a sentence of <paramref name="wordCount"/> words (3–10 when not given), capitalized and ending with a period.</summary>
    public string Sentence(int? wordCount = null)
    {
        OptionalArgument.ThrowIfNegative(wordCount);
        var text = new TextBuilder(stackalloc char[256]);
        AppendSentence(ref text, wordCount ?? Random.Int(3, 10));
        return text.ToStringAndDispose();
    }

    /// <summary>Returns a sentence of between <paramref name="minWords"/> and <paramref name="maxWords"/> words; <c>Sentence(3, 10)</c> is <c>Sentence()</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minWords"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="maxWords"/> is less than <paramref name="minWords"/>.</exception>
    public string Sentence(int minWords, int maxWords)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minWords);
        return Sentence(Count(minWords, maxWords, nameof(maxWords)));
    }

    /// <summary>Draws a count between <paramref name="min"/> (checked by the caller) and <paramref name="max"/> (one draw, as for the default counts).</summary>
    private int Count(int min, int max, string maxName)
    {
        if (max < min)
            throw new ArgumentException($"The maximum ({max}) is less than the minimum ({min}).", maxName);
        return Random.Int(min, max);
    }

    /// <summary>A sentence of <paramref name="wordCount"/> words without its closing period, like <c>Sentence(wordCount).TrimEnd('.')</c> in one allocation.</summary>
    internal string Title(int wordCount)
    {
        var text = new TextBuilder(stackalloc char[256]);
        AppendSentence(ref text, wordCount);
        while (text.Length > 0 && text.Written[^1] == '.')
            text.Length--;
        return text.ToStringAndDispose();
    }

    /// <summary>Returns <paramref name="sentenceCount"/> sentences (2–6 when not given) joined by <paramref name="separator"/>.</summary>
    public string Sentences(int? sentenceCount = null, string separator = " ")
    {
        OptionalArgument.ThrowIfNegative(sentenceCount);
        var text = new TextBuilder(stackalloc char[512]);
        AppendSentences(ref text, sentenceCount ?? Random.Int(2, 6), separator);
        return text.ToStringAndDispose();
    }

    /// <summary>Returns a paragraph of <paramref name="sentenceCount"/> sentences.</summary>
    public string Paragraph(int sentenceCount = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sentenceCount);
        return Sentences(sentenceCount);
    }

    /// <summary>Returns <paramref name="paragraphCount"/> paragraphs joined by <paramref name="separator"/>.</summary>
    public string Paragraphs(int paragraphCount = 3, string separator = "\n")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(paragraphCount);
        var text = new TextBuilder(stackalloc char[1024]);
        for (var i = 0; i < paragraphCount; i++)
        {
            if (i > 0)
                text.Append(separator);
            AppendSentences(ref text, 3, " "); // a Paragraph()
        }

        return text.ToStringAndDispose();
    }

    /// <summary>Returns <paramref name="lineCount"/> sentences (1–5 when not given), one per line.</summary>
    public string Lines(int? lineCount = null)
    {
        OptionalArgument.ThrowIfNegative(lineCount);
        return Sentences(lineCount ?? Random.Int(1, 5), "\n");
    }

    /// <summary>Returns a URL slug of <paramref name="wordCount"/> words, e.g. <c>dolores-illo-est</c>.</summary>
    public string Slug(int wordCount = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wordCount);
        return HelpersModule.Slugify(Words(wordCount));
    }

    /// <summary>Returns text of random shape: a sentence, sentences, paragraph(s) or lines.</summary>
    public string Text() => Random.Index(5) switch
    {
        0 => Sentence(),
        1 => Sentences(),
        2 => Paragraph(),
        3 => Paragraphs(),
        _ => Lines(),
    };

    /// <summary>Appends <paramref name="count"/> space-separated lorem words.</summary>
    private void AppendWords(ref TextBuilder text, int count)
    {
        var entry = Faker.Data.Strings(DataKeys.LoremWord);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                text.Append(' ');
            text.Append(entry[entry.PickIndex(Random)]);
        }
    }

    /// <summary>Appends a capitalized sentence ending with a period.</summary>
    private void AppendSentence(ref TextBuilder text, int wordCount)
    {
        var start = text.Length;
        AppendWords(ref text, wordCount);
        if (text.Length > start)
            text.Written[start] = char.ToUpperInvariant(text.Written[start]);
        text.Append('.');
    }

    /// <summary>Appends <paramref name="count"/> sentences of 3–10 words joined by <paramref name="separator"/>.</summary>
    private void AppendSentences(ref TextBuilder text, int count, string separator)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                text.Append(separator);
            AppendSentence(ref text, Random.Int(3, 10));
        }
    }
}
