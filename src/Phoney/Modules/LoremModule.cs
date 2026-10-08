using System.Text;
using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Placeholder text: words, sentences and paragraphs (faker.js <c>lorem</c>).</summary>
public sealed class LoremModule : FakerModule
{
    internal LoremModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns one placeholder word, optionally with a length between <paramref name="minLength"/> and <paramref name="maxLength"/>.</summary>
    public string Word(int? minLength = null, int? maxLength = null, WordLengthStrategy strategy = WordLengthStrategy.Fail)
    {
        if (WordPicker.TryPick(Faker.Data.Strings(DataKeys.LoremWord), Random, minLength, maxLength, strategy, out var word))
            return word;
        throw new PhoneyDataException($"No lorem words of length {minLength}-{maxLength} in locale '{Faker.Locale}'.");
    }

    /// <summary>Returns <paramref name="count"/> space-separated words.</summary>
    public string Words(int count = 3)
    {
        var sb = new StringBuilder();
        AppendWords(sb, count);
        return sb.ToString();
    }

    /// <summary>Returns a sentence of <paramref name="wordCount"/> words (3–10 when not given), capitalized and ending with a period.</summary>
    public string Sentence(int? wordCount = null)
    {
        var sb = new StringBuilder();
        AppendSentence(sb, wordCount ?? Random.Int(3, 10));
        return sb.ToString();
    }

    /// <summary>Returns <paramref name="sentenceCount"/> sentences (2–6 when not given) joined by <paramref name="separator"/>.</summary>
    public string Sentences(int? sentenceCount = null, string separator = " ")
    {
        var count = sentenceCount ?? Random.Int(2, 6);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(separator);
            AppendSentence(sb, Random.Int(3, 10));
        }

        return sb.ToString();
    }

    /// <summary>Returns a paragraph of <paramref name="sentenceCount"/> sentences.</summary>
    public string Paragraph(int sentenceCount = 3) => Sentences(sentenceCount);

    /// <summary>Returns <paramref name="paragraphCount"/> paragraphs joined by <paramref name="separator"/>.</summary>
    public string Paragraphs(int paragraphCount = 3, string separator = "\n")
    {
        var paragraphs = new string[Math.Max(0, paragraphCount)];
        for (var i = 0; i < paragraphs.Length; i++)
            paragraphs[i] = Paragraph();
        return string.Join(separator, paragraphs);
    }

    /// <summary>Returns <paramref name="lineCount"/> sentences (1–5 when not given), one per line.</summary>
    public string Lines(int? lineCount = null) => Sentences(lineCount ?? Random.Int(1, 5), "\n");

    /// <summary>Returns a URL slug of <paramref name="wordCount"/> words, e.g. <c>dolores-illo-est</c>.</summary>
    public string Slug(int wordCount = 3) => HelpersModule.Slugify(Words(wordCount));

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
    private void AppendWords(StringBuilder sb, int count)
    {
        var entry = Faker.Data.Strings(DataKeys.LoremWord);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(entry[entry.PickIndex(Random)]);
        }
    }

    /// <summary>Appends a capitalized sentence ending with a period.</summary>
    private void AppendSentence(StringBuilder sb, int wordCount)
    {
        var start = sb.Length;
        AppendWords(sb, wordCount);
        if (sb.Length > start)
            sb[start] = char.ToUpperInvariant(sb[start]);
        sb.Append('.');
    }
}
