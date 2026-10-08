using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Phoney.Data;

namespace Phoney.Templates;

/// <summary>
/// A pre-parsed <c>{{...}}</c> template. Parsing happens once per distinct string; evaluation only
/// picks values and concatenates.
/// </summary>
/// <remarks>
/// Expressions resolve like faker.js <c>helpers.fake</c>: a registered faker.js method wins, otherwise the
/// expression is a locale data path, optionally ending in a record field (<c>airline.airport.name</c>).
/// Data values that are templates themselves are evaluated recursively.
/// </remarks>
internal sealed class Template
{
    private const int MaxDepth = 32;
    private const int MaxCachedTemplates = 4096;

    private static readonly ConcurrentDictionary<string, Template> Cache = new(StringComparer.Ordinal);

    [ThreadStatic]
    private static int t_depth;

    private readonly Segment[] _segments;
    private readonly int _literalLength;

    private Template(string text, Segment[] segments)
    {
        Text = text;
        _segments = segments;
        foreach (var segment in segments)
            _literalLength += segment.Literal?.Length ?? 0;
    }

    /// <summary>The template source text.</summary>
    public string Text { get; }

    /// <summary>faker.js only treats <c>{{</c> followed by a lower-case letter as an expression.</summary>
    public static bool ContainsExpression(string text)
    {
        var index = text.IndexOf("{{", StringComparison.Ordinal);
        while (index >= 0)
        {
            if (index + 2 < text.Length && char.IsAsciiLetterLower(text[index + 2]))
                return true;
            index = text.IndexOf("{{", index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Parses user templates through a bounded cache.</summary>
    public static Template Cached(string text)
    {
        if (Cache.TryGetValue(text, out var template))
            return template;
        template = Parse(text);
        if (Cache.Count < MaxCachedTemplates)
            Cache.TryAdd(text, template);
        return template;
    }

    /// <summary>Parses <paramref name="text"/> into literal and expression segments.</summary>
    public static Template Parse(string text)
    {
        var segments = new List<Segment>();
        var position = 0;
        while (position < text.Length)
        {
            var start = FindExpressionStart(text, position);
            var end = start < 0 ? -1 : text.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (start < 0 || end < 0)
            {
                segments.Add(Segment.ForLiteral(text[position..]));
                break;
            }

            if (start > position)
                segments.Add(Segment.ForLiteral(text[position..start]));
            segments.Add(Segment.ForExpression(text[(start + 2)..end].Trim()));
            position = end + 2;
        }

        return new Template(text, [.. segments]);
    }

    /// <summary>Evaluates the template with <paramref name="faker"/>.</summary>
    public string Evaluate(Faker faker)
    {
        if (++t_depth > MaxDepth)
        {
            t_depth = 0;
            throw new PhoneyDataException($"Template recursion is too deep while evaluating '{Text}'. Does the data reference itself?");
        }

        try
        {
            if (_segments.Length == 1)
                return _segments[0].Evaluate(faker);

            var sb = new StringBuilder(_literalLength + (_segments.Length * 8));
            foreach (var segment in _segments)
                sb.Append(segment.Evaluate(faker));
            return sb.ToString();
        }
        finally
        {
            t_depth--;
        }
    }

    /// <summary>Index of the next <c>{{</c> followed by a lower-case letter, or -1.</summary>
    private static int FindExpressionStart(string text, int from)
    {
        var index = text.IndexOf("{{", from, StringComparison.Ordinal);
        while (index >= 0 && !(index + 2 < text.Length && char.IsAsciiLetterLower(text[index + 2])))
            index = text.IndexOf("{{", index + 1, StringComparison.Ordinal);
        return index;
    }

    /// <summary>
    /// One part of a template: literal text, a template function call, or a locale data reference.
    /// Everything that can be decided without a locale is resolved at parse time, so evaluation is a
    /// delegate call or an array lookup.
    /// </summary>
    private sealed class Segment
    {
        /// <summary>Bound faker.js-style method, e.g. <c>person.firstName</c>; wins over data paths like in faker.js.</summary>
        private TemplateFunction? _function;

        /// <summary>Arguments parsed once from <c>name(args)</c> expressions.</summary>
        private TemplateArgs _args = TemplateArgs.Empty;

        /// <summary>Key id when the expression is a data path (<c>person.last_name.generic</c>), otherwise -1.</summary>
        private int _key = -1;

        /// <summary>Key id of a records table when the expression selects a field (<c>airline.airport.name</c>), otherwise -1.</summary>
        private int _recordKey = -1;

        /// <summary>Record field selected from <see cref="_recordKey"/>.</summary>
        private string? _field;

        /// <summary>Literal text, or <see langword="null"/> for expression segments.</summary>
        public string? Literal { get; private init; }

        /// <summary>The expression text between <c>{{</c> and <c>}}</c>, used for error messages and late resolution.</summary>
        private string Expression { get; init; } = "";

        /// <summary>A literal text segment.</summary>
        public static Segment ForLiteral(string literal) => new() { Literal = literal };

        /// <summary>Classifies an expression: <c>name(args)</c> must be a function; a bare name is a function if registered, else a data path.</summary>
        public static Segment ForExpression(string expression)
        {
            if (expression.Length == 0)
                throw new FormatException("Template expressions cannot be empty.");

            var segment = new Segment { Expression = expression };
            var parenthesis = expression.IndexOf('(');
            if (parenthesis >= 0)
            {
                if (!expression.EndsWith(')'))
                    throw new FormatException($"Missing closing parenthesis in '{{{{{expression}}}}}'.");
                var name = expression[..parenthesis];
                segment._function = TemplateFunctions.Get(name)
                    ?? throw new FormatException($"Unknown template function '{name}' in '{{{{{expression}}}}}'.");
                segment._args = TemplateArgs.Parse(expression[(parenthesis + 1)..^1]);
                return segment;
            }

            segment._function = TemplateFunctions.Get(expression);
            if (segment._function is null)
                segment.ResolveDataPath();
            return segment;
        }

        /// <summary>Produces the segment's text.</summary>
        public string Evaluate(Faker faker)
        {
            if (Literal is not null)
                return Literal;
            if (_function is not null)
            {
                try
                {
                    return _function(faker, _args);
                }
                catch (PhoneyDataUnavailableException)
                {
                    // A pattern inherited from a fallback locale may use data that is not applicable here
                    // (e.g. en's postal address uses states, which az doesn't have): leave that part empty.
                    return "";
                }
            }
            if (_key < 0 && _recordKey < 0)
                ResolveDataPath(); // data may have been registered after parsing

            if (_key >= 0 && faker.Data.Get(_key) is UnavailableEntry)
                return ""; // see the function case above

            if (_key >= 0 && faker.Data.Get(_key) is { } entry)
            {
                return entry switch
                {
                    StringsEntry strings => faker.Pick(strings),
                    IntsEntry ints => faker.Random.Element(ints.Values).ToString(CultureInfo.InvariantCulture),
                    _ => throw new PhoneyDataException($"'{{{{{Expression}}}}}' refers to {entry.Kind} data; add a field name, e.g. '{{{{{Expression}.name}}}}'."),
                };
            }

            if (_recordKey >= 0 && faker.Data.Get(_recordKey) is RecordsEntry table)
            {
                var value = table.Get(faker.Random.Index(table.Count), _field!) ?? "";
                return ContainsExpression(value) ? faker.Parse(value) : value;
            }

            throw new PhoneyDataException(
                $"Cannot resolve '{{{{{Expression}}}}}' in locale '{faker.Locale}': it is neither a template function nor available locale data.");
        }

        /// <summary>
        /// Maps the expression to a data key, or to a records table plus field. Unknown paths stay unresolved
        /// and are retried at evaluation, because users may register custom data after a template was parsed.
        /// </summary>
        private void ResolveDataPath()
        {
            if (KeyRegistry.TryGetId(Expression, out var key))
            {
                _key = key;
                return;
            }

            var lastDot = Expression.LastIndexOf('.');
            if (lastDot > 0 && KeyRegistry.TryGetId(Expression[..lastDot], out var recordKey))
            {
                _recordKey = recordKey;
                _field = Expression[(lastDot + 1)..];
            }
        }
    }
}
