using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Phoney.Templates;

namespace Phoney.Modules;

/// <summary>General-purpose helpers: values from regular expressions, slugs, Luhn checksums and repetition. For picking and shuffling use <see cref="Phoney.Faker.Random"/>.</summary>
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

    /// <summary>Replaces <paramref name="symbol"/> with a digit 0-9 and <c>!</c> with a digit 2-9 (faker.js phone formats).</summary>
    internal string ReplaceSymbolWithNumber(string pattern, char symbol = '#')
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return string.Create(pattern.Length, (pattern, symbol, Random), static (span, state) => FillSymbols(span, state.pattern, state.symbol, state.Random));
    }

    /// <summary>Copies <paramref name="pattern"/> into <paramref name="span"/> with <paramref name="symbol"/> a digit and <c>!</c> a digit from 2 to 9.</summary>
    private static void FillSymbols(Span<char> span, string pattern, char symbol, SeededRandom random)
    {
        for (var i = 0; i < span.Length; i++)
        {
            if (pattern[i] == symbol)
                span[i] = random.Digit();
            else if (pattern[i] == '!')
                span[i] = (char)('0' + random.Int(2, 9));
            else
                span[i] = pattern[i];
        }
    }

    /// <summary>
    /// Fills a faker.js credit-card pattern: <c>[a-b]</c> becomes a number in that range, <c>x{n}</c>/<c>x{n,m}</c>
    /// repeat a character, <c>#</c> and <c>!</c> become digits and <c>L</c> becomes the Luhn check digit.
    /// Public callers use <see cref="FinanceModule.CreditCardNumber(string)"/>.
    /// </summary>
    internal string ReplaceCreditCardSymbols(string pattern = "6453-####-####-####-###L", char symbol = '#')
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var expanded = ExpandLegacyRanges(pattern);
        // Filled like ReplaceSymbolWithNumber, then the first L becomes the Luhn digit of everything before a trailing L, in the same buffer.
        return string.Create(expanded.Length, (expanded, symbol, Random), static (span, state) =>
        {
            FillSymbols(span, state.expanded, state.symbol, state.Random);
            var l = span.IndexOf('L');
            if (l >= 0)
                span[l] = (char)('0' + LuhnCheckDigit(span[^1] == 'L' ? span[..^1] : span));
        });
    }

    /// <summary>Computes the Luhn check digit for <paramref name="digits"/> (an optional trailing <c>L</c> is the placeholder; spaces and dashes are ignored).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="digits"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="digits"/> has no digit, or a character other than a digit, space or dash.</exception>
    public static int LuhnCheckDigit(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        var body = digits.EndsWith('L') ? digits.AsSpan(0, digits.Length - 1) : digits;
        if (!IsLuhnText(body))
            throw new ArgumentException($"A Luhn check digit is computed over digits (spaces and dashes are ignored), but got \"{digits}\".", nameof(digits));
        return LuhnCheckDigit(body);
    }

    /// <summary>The digit that makes <paramref name="body"/> followed by it pass the Luhn check.</summary>
    private static int LuhnCheckDigit(ReadOnlySpan<char> body)
    {
        var sum = LuhnSum(body, alternate: true); // the body followed by a 0: the 0 adds nothing and makes the body's last digit the first doubled one
        return sum == 0 ? 0 : 10 - sum;
    }

    /// <summary>Whether <paramref name="digits"/> (spaces and dashes ignored) passes the Luhn check; text without digits or with other characters doesn't.</summary>
    public static bool LuhnCheck(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        return IsLuhnText(digits) && LuhnSum(digits, alternate: false) == 0;
    }

    /// <summary>Whether <paramref name="text"/> has at least one digit and nothing but digits, spaces and dashes.</summary>
    private static bool IsLuhnText(ReadOnlySpan<char> text) =>
        text.IndexOfAnyInRange('0', '9') >= 0 && text.IndexOfAnyExcept(LuhnChars) < 0;

    private static readonly System.Buffers.SearchValues<char> LuhnChars = System.Buffers.SearchValues.Create("0123456789 -");

    /// <summary>Turns text into a URL-friendly slug: removes diacritics, replaces spaces with dashes and drops other symbols.</summary>
    public static string Slugify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Ascii.IsValid(text))
            return SlugifyAscii(text);
        var decomposed = Decomposition.FormKD(text);
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

    /// <summary><see cref="Slugify"/> for ASCII text, which has nothing to decompose and no marks: the text itself when every character stays.</summary>
    private static string SlugifyAscii(string text)
    {
        var keepsAll = true;
#pragma warning disable S3267 // a plain loop: Any with a lambda allocates a delegate and is slower on this hot path
        foreach (var c in text)
        {
            if (!IsSlugChar(c))
            {
                keepsAll = false;
                break;
            }
        }
#pragma warning restore S3267

        if (keepsAll)
            return text;
        Span<char> slug = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
        var length = 0;
        foreach (var c in text)
        {
            if (c == ' ')
                slug[length++] = '-';
            else if (IsSlugChar(c))
                slug[length++] = c;
        }

        return new string(slug[..length]);

        static bool IsSlugChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-';
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
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
            list.Add(factory());
        return list;
    }

    /// <summary>Calls <paramref name="factory"/> a random number of times between <paramref name="min"/> and <paramref name="max"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative.</exception>
    public List<T> Multiple<T>(Func<T> factory, int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        return Multiple(factory, Random.Int(min, max));
    }

    /// <summary>Returns up to <paramref name="count"/> distinct values produced by <paramref name="factory"/> (gives up after 1000 attempts per value).</summary>
    public List<T> UniqueValues<T>(Func<T> factory, int count)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var set = new HashSet<T>();
        var result = new List<T>(count);
        var limit = 1000L * count; // a long: 1000 × count passes int.MaxValue from about two million values on
        for (var attempts = 0L; result.Count < count && attempts < limit; attempts++)
        {
            var value = factory();
            if (set.Add(value))
                result.Add(value);
        }

        return result;
    }

    /// <summary>Replaces runs of <c>#</c> with digits where the first digit is not zero (used for building and apartment numbers).</summary>
    internal string ReplaceHashRunsWithoutLeadingZero(string text)
    {
        // A loop, not a Regex: the first use of one costs a process milliseconds, and every address has a building number.
        if (!text.Contains('#', StringComparison.Ordinal))
            return text;
        var builder = new TextBuilder(stackalloc char[64]);
        AppendHashRunsWithoutLeadingZero(text, ref builder);
        return builder.ToStringAndDispose();
    }

    /// <summary>Writes <paramref name="text"/> into <paramref name="builder"/> with each run of <c>#</c> replaced like <see cref="ReplaceHashRunsWithoutLeadingZero"/> (the same draws).</summary>
    internal void AppendHashRunsWithoutLeadingZero(string text, ref TextBuilder builder)
    {
        var random = Random;
        var rest = text.AsSpan();
        while (rest.IndexOf('#') is var first and >= 0)
        {
            builder.Append(rest[..first]);
            rest = rest[first..];
            var run = rest.IndexOfAnyExcept('#') is var end and >= 0 ? end : rest.Length;
            // What String.Numeric(run, allowLeadingZeros: false) draws: a digit from 1–9, then digits from 0–9.
            var digits = builder.AppendSpan(run);
            digits[0] = (char)('1' + random.Index(9));
            for (var i = 1; i < digits.Length; i++)
                digits[i] = (char)('0' + random.Index(10));
            rest = rest[run..];
        }

        builder.Append(rest);
    }

    /// <summary>
    /// faker.js' legacy pre-pass for credit card patterns: <c>x{n,m}</c>, <c>x{n}</c> repeat characters and
    /// <c>[a-b]</c> is replaced by a random integer in that range.
    /// </summary>
    private string ExpandLegacyRanges(string pattern)
    {
        // Each form needs a brace or a bracket, and most card formats have neither: no regex runs for them.
        if (pattern.AsSpan().IndexOfAny('{', '[') < 0)
            return pattern;
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
    private static int LuhnSum(ReadOnlySpan<char> digits, bool alternate)
    {
        var sum = 0;
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
