using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Phoney.Modules;

/// <summary>
/// Generates strings matching a (subset of) regular expression syntax, e.g. <c>[A-Z]{3}-\d{2,4}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Supported: literals, escapes (<c>\d \w \s \D \W \S \t \n \r \xHH \uHHHH</c>), <c>.</c>, character classes
/// with ranges and negation, groups (<c>(...)</c>, <c>(?:...)</c>), alternation <c>|</c>, quantifiers
/// <c>? * + {n} {n,} {n,m}</c> (also lazy forms), anchors (ignored), JavaScript-style <c>/pattern/i</c>
/// and the inline <c>(?i)</c> flag for case-insensitive output.
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

    private static readonly ConcurrentDictionary<string, PatternGenerator> Cache = new(StringComparer.Ordinal);

    private readonly Node _root;

    private PatternGenerator(Node root) => _root = root;

    /// <summary>Returns the cached generator for <paramref name="pattern"/>, parsing it on first use.</summary>
    public static PatternGenerator Get(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var generator))
            return generator;
        generator = new PatternGenerator(new Parser(pattern).ParsePattern());
        if (Cache.Count < MaxCached)
            Cache.TryAdd(pattern, generator);
        return generator;
    }

    /// <summary>Generates one matching string.</summary>
    public string Generate(Randomizer random)
    {
        var sb = new StringBuilder();
        _root.Append(sb, random);
        return sb.ToString();
    }

    /// <summary>A node of the parsed pattern tree.</summary>
    private abstract class Node
    {
        /// <summary>Appends generated text.</summary>
        public abstract void Append(StringBuilder sb, Randomizer random);
    }

    /// <summary>Fixed text.</summary>
    private sealed class LiteralNode(string text) : Node
    {
        /// <summary>The text.</summary>
        public string Text { get; } = text;

        /// <inheritdoc />
        public override void Append(StringBuilder sb, Randomizer random) => sb.Append(Text);
    }

    /// <summary>One character out of a set (character class, <c>\d</c>, <c>.</c>, or a case-insensitive letter).</summary>
    private sealed class SetNode(string characters) : Node
    {
        /// <summary>Characters to choose from.</summary>
        public string Characters { get; } = characters;

        /// <inheritdoc />
        public override void Append(StringBuilder sb, Randomizer random) => sb.Append(Characters[random.Index(Characters.Length)]);
    }

    /// <summary>Nodes generated one after another.</summary>
    private sealed class SequenceNode(Node[] items) : Node
    {
        /// <inheritdoc />
        public override void Append(StringBuilder sb, Randomizer random)
        {
            foreach (var item in items)
                item.Append(sb, random);
        }
    }

    /// <summary>One of several alternatives (<c>a|b</c>).</summary>
    private sealed class AlternationNode(Node[] options) : Node
    {
        /// <inheritdoc />
        public override void Append(StringBuilder sb, Randomizer random) => options[random.Index(options.Length)].Append(sb, random);
    }

    /// <summary>Repeats a node; <c>Max == -1</c> means unbounded (faker.js doubling distribution).</summary>
    private sealed class RepeatNode(Node node, int min, int max) : Node
    {
        /// <inheritdoc />
        public override void Append(StringBuilder sb, Randomizer random)
        {
            int count;
            if (max >= 0)
            {
                count = random.Int(min, max);
            }
            else
            {
                var limit = 1;
                while (limit < 1 << 10 && random.Bool())
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
        private bool _ignoreCase;

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

        /// <summary>Parses the whole pattern.</summary>
        public Node ParsePattern()
        {
            var node = ParseAlternation();
            if (_pos < _pattern.Length)
                throw Error($"unexpected '{_pattern[_pos]}'");
            return node;
        }

        /// <summary>Parses <c>a|b|c</c>.</summary>
        private Node ParseAlternation()
        {
            var options = new List<Node> { ParseSequence() };
            while (Peek() == '|')
            {
                _pos++;
                options.Add(ParseSequence());
            }

            return options.Count == 1 ? options[0] : new AlternationNode([.. options]);
        }

        /// <summary>Parses atoms up to <c>|</c> or <c>)</c>, merging adjacent literals.</summary>
        private Node ParseSequence()
        {
            var items = new List<Node>();
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
        private Node? ParseAtom()
        {
            var c = _pattern[_pos++];
            switch (c)
            {
                case '^' or '$':
                    return null; // anchors produce no output
                case '.':
                    return new SetNode(Alphanumeric);
                case '(':
                {
                    if (Peek() == '?')
                    {
                        if (_pos + 1 < _pattern.Length && _pattern[_pos + 1] is ':' or '=' or '!')
                            _pos += 2;
                        else
                            throw Error("unsupported group syntax");
                    }

                    var inner = ParseAlternation();
                    Expect(')');
                    return inner;
                }

                case '[':
                    return new SetNode(ParseClass());
                case '\\':
                    return ParseEscape(inClass: false);
                default:
                    return Letter(c);
            }
        }

        /// <summary>Wraps <paramref name="atom"/> in a repetition when a quantifier follows.</summary>
        private Node ParseQuantifier(Node atom)
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
            return new RepeatNode(atom, min, max);
        }

        /// <summary>Parses <c>{n}</c>, <c>{n,}</c> or <c>{n,m}</c>; anything else is a literal brace.</summary>
        private bool TryParseBraces(out int min, out int max)
        {
            min = max = 0;
            var close = _pattern.IndexOf('}', _pos);
            if (close < 0)
                return false;
            var body = _pattern.AsSpan(_pos + 1, close - _pos - 1);
            var comma = body.IndexOf(',');
            if (comma < 0)
            {
                if (!int.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out min))
                    return false;
                max = min;
            }
            else
            {
                if (!int.TryParse(body[..comma], NumberStyles.None, CultureInfo.InvariantCulture, out min))
                    return false;
                var upper = body[(comma + 1)..];
                if (upper.IsEmpty)
                    max = -1;
                else if (!int.TryParse(upper, NumberStyles.None, CultureInfo.InvariantCulture, out max))
                    return false;
            }

            _pos = close + 1;
            return true;
        }

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
                char start;
                if (_pattern[_pos] == '\\')
                {
                    _pos++;
                    var escaped = ParseEscape(inClass: true);
                    if (escaped is SetNode set)
                    {
                        chars.UnionWith(set.Characters);
                        continue;
                    }

                    start = ((LiteralNode)escaped).Text[0];
                }
                else
                {
                    start = _pattern[_pos++];
                }

                if (Peek() == '-' && _pos + 1 < _pattern.Length && _pattern[_pos + 1] != ']')
                {
                    _pos++;
                    var end = _pattern[_pos] == '\\' ? Unescape(++_pos) : _pattern[_pos++];
                    if (end < start)
                        throw Error($"character range {start}-{end} is out of order");
                    for (var ch = start; ch <= end; ch++)
                        AddCased(chars, ch);
                }
                else
                {
                    AddCased(chars, start);
                }
            }

            Expect(']');
            if (negate)
            {
                // Like faker.js, negation is taken against the alphanumeric characters.
                var remaining = Alphanumeric.Where(ch => !chars.Contains(ch)).ToArray();
                if (remaining.Length == 0)
                    throw Error("negated character class excludes every alphanumeric character");
                return new string(remaining);
            }

            if (chars.Count == 0)
                throw Error("empty character class");
            return new string([.. chars]);
        }

        /// <summary>Reads an escaped character used as a range bound.</summary>
        private char Unescape(int position)
        {
            _pos = position + 1;
            return _pattern[position] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                var other => other,
            };
        }

        /// <summary>Parses an escape after <c>\</c>.</summary>
        private Node ParseEscape(bool inClass)
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
                'x' => new LiteralNode(((char)ReadHex(2)).ToString()),
                'u' => new LiteralNode(((char)ReadHex(4)).ToString()),
                'b' or 'B' when !inClass => new LiteralNode(""),
                _ => new LiteralNode(c.ToString()),
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
        private readonly Node Letter(char c) =>
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

        /// <summary>A parse error that names the pattern and position.</summary>
        private readonly FormatException Error(string message) =>
            new($"Invalid pattern '{_pattern}' at position {_pos}: {message}.");
    }
}
