using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Phonery.Modules;

/// <summary>General-purpose helpers: patterns, symbols, checksums, collections (faker.js <c>helpers</c>).</summary>
public sealed partial class HelpersModule : FakerModule
{
    internal HelpersModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Generates a string matching a regular expression, e.g. <c>[A-Z]{3}-\d{4}</c>. See <see cref="PatternGenerator"/> for the supported syntax.</summary>
    public string FromRegExp(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return PatternGenerator.Get(pattern).Generate(Random);
    }

    /// <summary>Replaces <c>#</c> with a digit, <c>?</c> with an upper-case letter and <c>*</c> with either.</summary>
    public string ReplaceSymbols(string pattern) => Random.Replace(pattern);

    /// <summary>Replaces <paramref name="symbol"/> with a digit 0-9 and <c>!</c> with a digit 2-9.</summary>
    public string ReplaceSymbolWithNumber(string pattern, char symbol = '#')
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return string.Create(pattern.Length, (pattern, symbol, Random), static (span, state) =>
        {
            var (p, sym, random) = state;
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = p[i] == sym ? random.Digit()
                    : p[i] == '!' ? (char)('0' + random.Int(2, 9))
                    : p[i];
            }
        });
    }

    /// <summary>
    /// Fills a credit-card style pattern: <c>[a-b]</c> becomes a number in that range, <c>x{n}</c>/<c>x{n,m}</c>
    /// repeat a character, <c>#</c> and <c>!</c> become digits and <c>L</c> becomes the Luhn check digit.
    /// </summary>
    public string ReplaceCreditCardSymbols(string pattern = "6453-####-####-####-###L", char symbol = '#')
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var expanded = ExpandLegacyRanges(pattern);
        var filled = ReplaceSymbolWithNumber(expanded, symbol);
        var check = LuhnCheckDigit(filled);
        var l = filled.IndexOf('L');
        return l < 0 ? filled : string.Concat(filled.AsSpan(0, l), check.ToString(CultureInfo.InvariantCulture), filled.AsSpan(l + 1));
    }

    /// <summary>Computes the Luhn check digit for <paramref name="digits"/> (an optional trailing <c>L</c> is the placeholder).</summary>
    public static int LuhnCheckDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        var body = digits.EndsWith('L') ? digits[..^1] : digits;
        var sum = LuhnSum(body + "0");
        return sum == 0 ? 0 : 10 - sum;
    }

    /// <summary>Whether <paramref name="digits"/> (spaces and dashes ignored) passes the Luhn check.</summary>
    public static bool LuhnCheck(string digits) => LuhnSum(digits) == 0;

    /// <summary>Turns text into a URL-friendly slug: removes diacritics, replaces spaces with dashes and drops other symbols.</summary>
    public static string Slugify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (c == ' ')
                sb.Append('-');
            else if (char.IsLetterOrDigit(c) || c is '_' or '.' or '-')
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Replaces <c>{{key}}</c> placeholders with values from <paramref name="values"/> (no locale lookups).</summary>
    public static string Mustache(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (key, value) in values)
            text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        return text;
    }

    /// <summary>Returns <paramref name="factory"/>'s result with the given <paramref name="probability"/>, otherwise <see langword="default"/>.</summary>
    public T? Maybe<T>(Func<T> factory, double probability = 0.5)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Random.Bool(probability) ? factory() : default;
    }

    /// <summary>Calls <paramref name="factory"/> <paramref name="count"/> times.</summary>
    public List<T> Multiple<T>(Func<T> factory, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var list = new List<T>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
            list.Add(factory());
        return list;
    }

    /// <summary>Calls <paramref name="factory"/> a random number of times between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public List<T> Multiple<T>(Func<T> factory, int min, int max) => Multiple(factory, Random.Int(min, max));

    /// <summary>Returns up to <paramref name="count"/> distinct values produced by <paramref name="factory"/> (gives up after 1000 attempts per value, like faker.js).</summary>
    public List<T> UniqueArray<T>(Func<T> factory, int count)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var set = new HashSet<T>();
        var result = new List<T>(count);
        for (var attempts = 0; result.Count < count && attempts < 1000 * count; attempts++)
        {
            var value = factory();
            if (set.Add(value))
                result.Add(value);
        }

        return result;
    }

    /// <summary>Returns a random element of <paramref name="items"/>.</summary>
    public T ArrayElement<T>(IReadOnlyList<T> items) => Random.Element(items);

    /// <summary>Returns a random subset (in random order); between 1 and all elements when <paramref name="count"/> is null.</summary>
    public T[] ArrayElements<T>(IReadOnlyList<T> items, int? count = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Count == 0 ? [] : Random.Elements(items, count ?? Random.Int(1, items.Count));
    }

    /// <summary>Picks a value according to its relative weight.</summary>
    public T WeightedArrayElement<T>(IReadOnlyList<(T Value, double Weight)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("Cannot pick an element from an empty collection.", nameof(items));
        var total = 0.0;
        foreach (var (_, weight) in items)
        {
            if (weight <= 0)
                throw new ArgumentException("Weights must be positive.", nameof(items));
            total += weight;
        }

        var target = Random.Double() * total;
        foreach (var (value, weight) in items)
        {
            target -= weight;
            if (target < 0)
                return value;
        }

        return items[^1].Value;
    }

    /// <summary>Returns a shuffled copy of <paramref name="items"/>.</summary>
    public T[] Shuffle<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        Random.Shuffle(copy.AsSpan());
        return copy;
    }

    /// <summary>Returns a random value of <typeparamref name="TEnum"/>.</summary>
    public TEnum EnumValue<TEnum>() where TEnum : struct, Enum => Random.Enum<TEnum>();

    /// <summary>Evaluates a faker.js template; same as <see cref="Phonery.Faker.Parse"/>.</summary>
    public string Fake(string template) => Faker.Parse(template);

    /// <summary>Picks one of <paramref name="templates"/> and evaluates it.</summary>
    public string Fake(IReadOnlyList<string> templates) => Faker.Parse(Random.Element(templates));

    /// <summary>Replaces runs of <c>#</c> with digits where the first digit is not zero (used for building and apartment numbers).</summary>
    internal string ReplaceHashRunsWithoutLeadingZero(string text) =>
        HashRun().Replace(text, m => Faker.String.Numeric(m.Length, allowLeadingZeros: false));

    /// <summary>
    /// faker.js' legacy pre-pass for credit card patterns: <c>x{n,m}</c>, <c>x{n}</c> repeat characters and
    /// <c>[a-b]</c> is replaced by a random integer in that range.
    /// </summary>
    private string ExpandLegacyRanges(string pattern)
    {
        pattern = RangeRepeat().Replace(pattern, m =>
        {
            var (min, max) = Ordered(int.Parse(m.Groups[2].ValueSpan, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].ValueSpan, CultureInfo.InvariantCulture));
            return new string(m.Groups[1].Value[0], Random.Int(min, max));
        });
        pattern = Repeat().Replace(pattern, m => new string(m.Groups[1].Value[0], int.Parse(m.Groups[2].ValueSpan, CultureInfo.InvariantCulture)));
        return NumberRange().Replace(pattern, m =>
        {
            var (min, max) = Ordered(int.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].ValueSpan, CultureInfo.InvariantCulture));
            return Random.Int(min, max).ToString(CultureInfo.InvariantCulture);
        });

        static (int, int) Ordered(int a, int b) => a <= b ? (a, b) : (b, a);
    }

    /// <summary>Luhn checksum modulo 10, ignoring spaces and dashes.</summary>
    private static int LuhnSum(string digits)
    {
        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var c = digits[i];
            if (c is ' ' or '-')
                continue;
            var n = c - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                    n = (n % 10) + 1;
            }

            sum += n;
            alternate = !alternate;
        }

        return sum % 10;
    }

    /// <summary>Runs of <c>#</c>.</summary>
    [GeneratedRegex("#+")]
    private static partial Regex HashRun();

    /// <summary><c>x{n,m}</c>: a character repeated n to m times.</summary>
    [GeneratedRegex(@"(.)\{(\d+),(\d+)\}")]
    private static partial Regex RangeRepeat();

    /// <summary><c>x{n}</c>: a character repeated n times.</summary>
    [GeneratedRegex(@"(.)\{(\d+)\}")]
    private static partial Regex Repeat();

    /// <summary><c>[a-b]</c>: a number between a and b.</summary>
    [GeneratedRegex(@"\[(\d+)-(\d+)\]")]
    private static partial Regex NumberRange();
}
