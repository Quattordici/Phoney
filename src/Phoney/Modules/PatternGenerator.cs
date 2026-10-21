using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Phoney.Modules;

/// <summary>
/// Generates strings matching a (subset of) regular expression syntax, e.g. <c>[A-Z]{3}-\d{2,4}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Supported: literals, escapes (<c>\d \w \s \D \W \S \t \n \r \f \v \0 \xHH \uHHHH</c>, and <c>\p{L} \p{Lu} \p{Ll} \p{N} \p{Nd}</c> as their ASCII
/// characters), <c>.</c>, character classes with ranges and negation, groups (<c>(...)</c>, <c>(?:...)</c>, named <c>(?&lt;name&gt;...)</c>), alternation <c>|</c>,
/// quantifiers <c>? * + {n} {n,} {n,m}</c> (also lazy forms), anchors (<c>^ $ \A \z \Z \b</c>, ignored), JavaScript-style <c>/pattern/i</c>
/// and the inline <c>(?i)</c> flag for case-insensitive output.
/// </para>
/// <para>
/// Lookarounds (<c>(?=…) (?!…) (?&lt;=…) (?&lt;!…)</c>) produce nothing, so the text may not satisfy them; <see cref="IsExact"/> says so, and callers that must
/// match check the text. Escapes that can't be generated (backreferences, other letters) are a <see cref="FormatException"/> rather than literal letters.
/// </para>
/// <para>
/// Unlike faker.js' string-rewriting <c>fromRegExp</c>, the pattern is parsed once into a tree and cached,
/// so repeated generation does no parsing. Unbounded quantifiers use faker.js' distribution: the upper
/// limit doubles while a coin flip succeeds.
/// </para>
/// </remarks>
internal sealed class PatternGenerator
{
    private const string Alphanumeric = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Word = Alphanumeric + "_";
    private const string NonWord = " !\"#$%&'()*+,-./:;<=>?@[\\]^`{|}~";
    private const int MaxCached = 1024;

    /// <summary>How deep groups may nest: parsing and generating recurse once per level, so a deeper pattern would overflow the stack instead of failing with a message.</summary>
    private const int MaxNesting = 256;

    /// <summary>The longest text a pattern may be able to produce (16 Mi characters): longer ones are refused when parsed rather than running out of memory while generating.</summary>
    private const long MaxOutputLength = 1L << 24;

    /// <summary>The most repetitions an unbounded quantifier (<c>* + {n,}</c>) adds to its minimum (faker.js' doubling stops at 1024).</summary>
    private const int UnboundedExtra = 1 << 10;

    private static readonly ConcurrentDictionary<string, PatternGenerator> Cache = new(StringComparer.Ordinal);

    private readonly INode _root;

    private PatternGenerator(INode root, bool exact)
    {
        _root = root;
        IsExact = exact;
    }

    /// <summary>Whether every generated text matches the pattern; <see langword="false"/> when it has lookarounds, which generate nothing.</summary>
    public bool IsExact { get; }

    /// <summary>Returns the cached generator for <paramref name="pattern"/>, parsing it on first use.</summary>
    public static PatternGenerator Get(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var generator))
            return generator;
        var parser = new Parser(pattern);
        var root = parser.ParsePattern();
        generator = new PatternGenerator(root, parser.IsExact);
        if (Cache.Count < MaxCached)
            Cache.TryAdd(pattern, generator);
        return generator;
    }

    /// <summary>Generates one matching string.</summary>
    public string Generate(SeededRandom random)
    {
        var sb = new StringBuilder();
        _root.Append(sb, random);
        return sb.ToString();
    }

    /// <summary>A node of the parsed pattern tree.</summary>
    private interface INode
    {
        /// <summary>
        /// The most characters the node can append, saturated at <see cref="long.MaxValue"/>; a repetition of something empty counts as one, because it still costs a
        /// turn of the loop (<c>(){2000000000}</c> appends nothing but would take a minute).
        /// </summary>
        long MaxLength { get; }

        /// <summary>Appends generated text.</summary>
        void Append(StringBuilder sb, SeededRandom random);
    }

    /// <summary>Fixed text.</summary>
    private sealed class LiteralNode(string text) : INode
    {
        /// <summary>The text.</summary>
        public string Text { get; } = text;

        /// <inheritdoc />
        public long MaxLength => Text.Length;

        /// <inheritdoc />
        public void Append(StringBuilder sb, SeededRandom random) => sb.Append(Text);
    }

    /// <summary>One character out of a set (character class, <c>\d</c>, <c>.</c>, or a case-insensitive letter).</summary>
    private sealed class SetNode(string characters) : INode
    {
        /// <summary>Characters to choose from.</summary>
        public string Characters { get; } = characters;

        /// <inheritdoc />
        public long MaxLength => 1;

        /// <inheritdoc />
        public void Append(StringBuilder sb, SeededRandom random) => sb.Append(Characters[random.Index(Characters.Length)]);
    }

    /// <summary>Nodes generated one after another.</summary>
    private sealed class SequenceNode(INode[] items) : INode
    {
        /// <inheritdoc />
        public long MaxLength { get; } = items.Aggregate(0L, (total, item) => AddLengths(total, item.MaxLength));

        /// <summary><paramref name="a"/> + <paramref name="b"/> for lengths, saturated at <see cref="long.MaxValue"/>.</summary>
        private static long AddLengths(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

        /// <inheritdoc />
        public void Append(StringBuilder sb, SeededRandom random)
        {
            foreach (var item in items)
                item.Append(sb, random);
        }
    }

    /// <summary>One of several alternatives (<c>a|b</c>).</summary>
    private sealed class AlternationNode(INode[] options) : INode
    {
        /// <inheritdoc />
        public long MaxLength { get; } = options.Max(option => option.MaxLength);

        /// <inheritdoc />
        public void Append(StringBuilder sb, SeededRandom random) => options[random.Index(options.Length)].Append(sb, random);
    }

    /// <summary>Repeats a node; <c>Max == -1</c> means unbounded (faker.js doubling distribution).</summary>
    private sealed class RepeatNode(INode node, int min, int max) : INode
    {
        /// <inheritdoc />
        public long MaxLength { get; } = MultiplyLengths(node.MaxLength, max >= 0 ? max : (long)min + UnboundedExtra);

        /// <summary><paramref name="length"/> (at least one per repetition) × <paramref name="count"/>, saturated at <see cref="long.MaxValue"/>.</summary>
        private static long MultiplyLengths(long length, long count)
        {
            length = Math.Max(length, 1);
            if (count == 0)
                return 0;
            return length > long.MaxValue / count ? long.MaxValue : length * count;
        }

        /// <inheritdoc />
        public void Append(StringBuilder sb, SeededRandom random)
        {
            int count;
            if (max >= 0)
            {
                count = random.Int(min, max);
            }
            else
            {
                var limit = 1;
                while (limit < UnboundedExtra && random.Bool())
                    limit *= 2;
                count = random.Int(min, min + limit);
            }

            for (var i = 0; i < count; i++)
                node.Append(sb, random);
        }
    }

    /// <summary>Recursive-descent parser: pattern → alternation → sequence → quantified atom.</summary>
    private ref struct Parser
    {
        private readonly string _pattern;
        private int _pos;
        private readonly bool _ignoreCase;
        private bool _hasZeroWidth; // a lookaround or an anchor inside the pattern, which generation cannot guarantee
        private int _depth; // groups open at the current position

        public Parser(string pattern)
        {
            // JavaScript literal syntax: /source/flags
            if (pattern.Length >= 2 && pattern[0] == '/' && pattern.LastIndexOf('/') > 0)
            {
                var end = pattern.LastIndexOf('/');
                var flags = pattern[(end + 1)..];
                if (flags.All(char.IsAsciiLetter))
                {
                    _ignoreCase = flags.Contains('i');
                    pattern = pattern[1..end];
                }
            }

            if (pattern.StartsWith("(?i)", StringComparison.Ordinal))
            {
                _ignoreCase = true;
                pattern = pattern[4..];
            }

            _pattern = pattern;
            _pos = 0;
        }

        /// <summary>Whether the parsed pattern has no lookarounds, so its text always matches.</summary>
        public readonly bool IsExact => !_hasZeroWidth;

        /// <summary>Parses the whole pattern.</summary>
        public INode ParsePattern()
        {
            var node = ParseAlternation();
            if (_pos < _pattern.Length)
                throw Error($"unexpected '{_pattern[_pos]}'");
            if (node.MaxLength > MaxOutputLength)
            {
                throw new FormatException(
                    $"Invalid pattern '{Shortened(_pattern)}': it can produce up to {(node.MaxLength == long.MaxValue ? "more than 9 quintillion" : node.MaxLength.ToString("N0", CultureInfo.InvariantCulture))} " +
                    $"characters or repetitions, and the limit is {MaxOutputLength.ToString("N0", CultureInfo.InvariantCulture)}. Use smaller repetition counts.");
            }

            return node;
        }

        /// <summary>Parses <c>a|b|c</c>.</summary>
        private INode ParseAlternation()
        {
            var options = new List<INode> { ParseSequence() };
            while (Peek() == '|')
            {
                _pos++;
                options.Add(ParseSequence());
            }

            return options.Count == 1 ? options[0] : new AlternationNode([.. options]);
        }

        /// <summary>Parses atoms up to <c>|</c> or <c>)</c>, merging adjacent literals.</summary>
        private INode ParseSequence()
        {
            var items = new List<INode>();
            var literal = new StringBuilder();
            while (_pos < _pattern.Length && Peek() is not ('|' or ')'))
            {
                var atom = ParseAtom();
                if (atom is null)
                    continue;
                atom = ParseQuantifier(atom);

                // Merge consecutive literals so generation appends fewer, longer strings.
                if (atom is LiteralNode text)
                {
                    literal.Append(text.Text);
                    continue;
                }

                if (literal.Length > 0)
                {
                    items.Add(new LiteralNode(literal.ToString()));
                    literal.Clear();
                }

                items.Add(atom);
            }

            if (literal.Length > 0)
                items.Add(new LiteralNode(literal.ToString()));
            return items.Count == 1 ? items[0] : new SequenceNode([.. items]);
        }

        /// <summary>Parses one literal, class, escape, group or <c>.</c>; anchors return <see langword="null"/>.</summary>
        private INode? ParseAtom()
        {
            var c = _pattern[_pos++];
            switch (c)
            {
                case '^' or '$':
                    // Anchors produce no output; only those at the very start or end are sure to hold.
                    if (!(c == '^' && _pos == 1 || c == '$' && _pos == _pattern.Length))
                        _hasZeroWidth = true;
                    return null;
                case '.':
                    return new SetNode(Alphanumeric);
                case '(':
                {
                    if (++_depth > MaxNesting)
                        throw Error($"groups are nested more than {MaxNesting} deep");
                    var zeroWidth = Peek() == '?' && ParseGroupPrefix();
                    var inner = ParseAlternation();
                    Expect(')');
                    _depth--;
                    if (!zeroWidth)
                        return inner;
                    _hasZeroWidth = true;
                    return null; // a lookaround matches no text of its own
                }

                case '[':
                    return new SetNode(ParseClass());
                case '\\':
                    return ParseEscape(inClass: false);
                case '*' or '+' or '?':
                    _pos--;
                    throw Error($"the quantifier '{c}' follows nothing it could repeat");
                default:
                    return Letter(c);
            }
        }

        /// <summary>Reads what follows <c>(?</c>: non-capturing and named groups, or a lookaround (returns <see langword="true"/>).</summary>
        private bool ParseGroupPrefix()
        {
            var rest = _pattern.AsSpan(_pos + 1);
            switch (rest)
            {
                case [':', ..]:
                    _pos += 2;
                    return false;
                case ['=' or '!', ..]:
                    _pos += 2;
                    return true;
                case ['<', '=' or '!', ..]:
                    _pos += 3;
                    return true;
                case ['<' or '\'', ..]:
                    var close = rest[1..].IndexOf(rest[0] == '<' ? '>' : '\'') + 1;
                    if (close > 1)
                    {
                        _pos += close + 2; // a named group generates like any other
                        return false;
                    }

                    break;
            }

            throw Error("unsupported group syntax");
        }

        /// <summary>Wraps <paramref name="atom"/> in a repetition when a quantifier follows.</summary>
        private INode ParseQuantifier(INode atom)
        {
            if (_pos >= _pattern.Length)
                return atom;
            int min, max;
            switch (_pattern[_pos])
            {
                case '?':
                    _pos++;
                    (min, max) = (0, 1);
                    break;
                case '*':
                    _pos++;
                    (min, max) = (0, -1);
                    break;
                case '+':
                    _pos++;
                    (min, max) = (1, -1);
                    break;
                case '{' when TryParseBraces(out min, out max):
                    break;
                default:
                    return atom;
            }

            if (Peek() == '?')
                _pos++; // lazy quantifiers generate the same way
            if (max >= 0 && max < min)
                throw Error($"quantifier {{{min},{max}}} is out of order");
            if (Peek() is '*' or '+' or '?' || (Peek() == '{' && IsBraceQuantifier()))
                throw Error("a quantifier follows another quantifier (.NET has no possessive or nested quantifiers)");
            return new RepeatNode(atom, min, max);
        }

        /// <summary>Whether a <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c> quantifier starts at the current <c>{</c> (otherwise the brace is a literal, as in .NET).</summary>
        private readonly bool IsBraceQuantifier()
        {
            var close = _pattern.IndexOf('}', _pos);
            if (close < 0)
                return false;
            var body = _pattern.AsSpan(_pos + 1, close - _pos - 1);
            var comma = body.IndexOf(',');
            return comma < 0
                ? IsDigits(body)
                : IsDigits(body[..comma]) && (comma == body.Length - 1 || IsDigits(body[(comma + 1)..]));

            static bool IsDigits(ReadOnlySpan<char> text) => !text.IsEmpty && !text.ContainsAnyExceptInRange('0', '9');
        }

        /// <summary>Parses <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>; anything else is a literal brace.</summary>
        private bool TryParseBraces(out int min, out int max)
        {
            min = max = 0;
            if (!IsBraceQuantifier())
                return false;
            var close = _pattern.IndexOf('}', _pos);
            var body = _pattern.AsSpan(_pos + 1, close - _pos - 1);
            var comma = body.IndexOf(',');
            min = Count(comma < 0 ? body : body[..comma]);
            if (comma < 0)
                max = min;
            else if (comma == body.Length - 1)
                max = -1;
            else
                max = Count(body[(comma + 1)..]);
            _pos = close + 1;
            return true;
        }

        /// <summary>A repetition count of a brace quantifier (digits only); like .NET, one beyond <see cref="int.MaxValue"/> is an error.</summary>
        private readonly int Count(ReadOnlySpan<char> digits) =>
            int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? count
                : throw Error($"the repetition count {digits} is too large");

        /// <summary>Parses a character class after <c>[</c>, returning the allowed characters.</summary>
        private string ParseClass()
        {
            var negate = Peek() == '^';
            if (negate)
                _pos++;
            var chars = new SortedSet<char>();
            var first = true;
            while (_pos < _pattern.Length && (_pattern[_pos] != ']' || first))
            {
                first = false;
                ParseClassItem(chars);
            }

            Expect(']');
            if (negate)
                return Negated(chars);
            if (chars.Count == 0)
                throw Error("empty character class");
            return new string([.. chars]);
        }

        /// <summary>Parses one item of a character class (a character, an escape or a range) and adds what it allows.</summary>
        private void ParseClassItem(SortedSet<char> chars)
        {
            char start;
            if (_pattern[_pos] == '\\')
            {
                _pos++;
                var escaped = ParseEscape(inClass: true);
                if (escaped is SetNode set)
                {
                    chars.UnionWith(set.Characters);
                    return;
                }

                start = ((LiteralNode)escaped).Text[0];
            }
            else
            {
                start = _pattern[_pos++];
            }

            if (Peek() != '-' || _pos + 1 >= _pattern.Length || _pattern[_pos + 1] == ']')
            {
                AddCased(chars, start);
                return;
            }

            _pos++;
            var end = _pattern[_pos] == '\\' ? EscapedRangeEnd() : _pattern[_pos++];
            if (end < start)
                throw Error($"character range {Printable(start)}-{Printable(end)} is out of order");
            for (int ch = start; ch <= end; ch++) // an int, so a range ending at \uFFFF stops
                AddCased(chars, (char)ch);
        }

        /// <summary>The alphanumeric characters a class leaves out; like faker.js, negation is taken against those.</summary>
        private string Negated(SortedSet<char> chars)
        {
            var remaining = Alphanumeric.Where(ch => !chars.Contains(ch)).ToArray();
            if (remaining.Length == 0)
                throw Error("negated character class excludes every alphanumeric character");
            return new string(remaining);
        }

        /// <summary>Reads the escaped character that ends a range (<c>\u005A</c>, <c>\x5A</c>, <c>\t</c>…), read like any escape in a class.</summary>
        private char EscapedRangeEnd()
        {
            _pos++;
            return ParseEscape(inClass: true) is LiteralNode { Text.Length: 1 } literal
                ? literal.Text[0]
                : throw Error("a character class escape (\\d, \\w, \\p{…}) can't end a range");
        }

        /// <summary>Parses an escape after <c>\</c>.</summary>
        private INode ParseEscape(bool inClass)
        {
            if (_pos >= _pattern.Length)
                throw Error("pattern ends with a backslash");
            var c = _pattern[_pos++];
            return c switch
            {
                'd' => new SetNode("0123456789"),
                'w' => new SetNode(Word),
                's' => new SetNode(" "),
                'D' => new SetNode("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"),
                'W' => new SetNode(NonWord),
                'S' => new SetNode(Alphanumeric),
                't' => new LiteralNode("\t"),
                'n' => new LiteralNode("\n"),
                'r' => new LiteralNode("\r"),
                'f' => new LiteralNode("\f"),
                'v' => new LiteralNode("\v"),
                '0' => new LiteralNode("\0"),
                'x' => new LiteralNode(((char)ReadHex(2)).ToString()),
                'u' => new LiteralNode(((char)ReadHex(4)).ToString()),
                'p' => new SetNode(ReadCategory()),
                'P' => new SetNode(Outside(ReadCategory())),
                'b' when inClass => new LiteralNode("\b"),
                'b' or 'B' or 'A' or 'z' or 'Z' or 'G' when !inClass => Anchor(), // anchors produce no output and may not hold
                >= '1' and <= '9' or 'k' => throw Error("backreferences are not supported"),
                _ when char.IsAsciiLetter(c) => throw Error($"the escape \\{c} is not supported"),
                _ => new LiteralNode(c.ToString()),
            };
        }

        /// <summary>An anchor inside the pattern: no output, and generated values must be checked because it may not hold.</summary>
        private LiteralNode Anchor()
        {
            _hasZeroWidth = true;
            return new LiteralNode("");
        }

        /// <summary>The alphanumeric characters that are not in <paramref name="characters"/>, as negated classes are taken.</summary>
        private static string Outside(string characters) => new([.. Alphanumeric.Where(ch => !characters.Contains(ch))]);

        /// <summary>Reads <c>{Name}</c> after <c>\p</c>: the ASCII characters of a Unicode category.</summary>
        private string ReadCategory()
        {
            var close = _pattern.IndexOf('}', _pos);
            if (Peek() != '{' || close < 0)
                throw Error("expected a category name in braces after \\p");
            var name = _pattern[(_pos + 1)..close];
            _pos = close + 1;
            return name switch
            {
                "L" => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz",
                "Lu" => "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
                "Ll" => "abcdefghijklmnopqrstuvwxyz",
                "N" or "Nd" => "0123456789",
                _ => throw Error($"the Unicode category {name} is not supported (L, Lu, Ll, N and Nd are)"),
            };
        }

        /// <summary>Reads <paramref name="digits"/> hexadecimal digits.</summary>
        private int ReadHex(int digits)
        {
            if (_pos + digits > _pattern.Length ||
                !int.TryParse(_pattern.AsSpan(_pos, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw Error("invalid hexadecimal escape");
            _pos += digits;
            return value;
        }

        /// <summary>A literal character, or both cases when ignoring case.</summary>
        private readonly INode Letter(char c) =>
            _ignoreCase && char.IsAsciiLetter(c)
                ? new SetNode(new string([char.ToLowerInvariant(c), char.ToUpperInvariant(c)]))
                : new LiteralNode(c.ToString());

        /// <summary>Adds a class character (both cases when ignoring case).</summary>
        private readonly void AddCased(SortedSet<char> chars, char c)
        {
            chars.Add(c);
            if (_ignoreCase && char.IsAsciiLetter(c))
            {
                chars.Add(char.ToLowerInvariant(c));
                chars.Add(char.ToUpperInvariant(c));
            }
        }

        /// <summary>The current character, if any.</summary>
        private readonly char? Peek() => _pos < _pattern.Length ? _pattern[_pos] : null;

        /// <summary>Consumes <paramref name="c"/> or fails.</summary>
        private void Expect(char c)
        {
            if (Peek() != c)
                throw Error($"expected '{c}'");
            _pos++;
        }

        /// <summary>The pattern for an error message: long ones (a hundred thousand nested groups) are cut, so the message stays readable.</summary>
        private static string Shortened(string pattern) => pattern.Length <= 200 ? pattern : string.Concat(pattern.AsSpan(0, 200), "…");

        /// <summary>A parse error that names the pattern and position.</summary>
        private readonly FormatException Error(string message) =>
            new($"Invalid pattern '{Shortened(_pattern)}' at position {_pos}: {message}.");

        /// <summary>A character as it can be read in a message: itself, or <c>\uXXXX</c> for control characters and halves of surrogate pairs.</summary>
        private static string Printable(char c) =>
            char.IsControl(c) || char.IsSurrogate(c) ? $"\\u{(int)c:X4}" : c.ToString();
    }
}
