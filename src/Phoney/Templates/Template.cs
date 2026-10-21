using System.Collections.Concurrent;
using System.Globalization;
using Phoney.Data;

namespace Phoney.Templates;

/// <summary>
/// A pre-parsed <c>{{...}}</c> template. Parsing happens once per distinct string; evaluation only
/// picks values and concatenates.
/// </summary>
/// <remarks>
/// Expressions resolve like faker.js <c>helpers.fake</c>: a registered function wins (names are case-insensitive, so
/// <c>Person.FirstName</c> and faker.js' <c>person.firstName</c> are the same), otherwise the expression is a locale
/// data path, optionally ending in a record field (<c>airline.airport.name</c>).
/// Data values that are templates themselves are evaluated recursively.
/// </remarks>
internal sealed class Template
{
    private const int MaxDepth = 32;
    private const int MaxCachedTemplates = 4096;

    private static readonly ConcurrentDictionary<string, Template> Cache = new(StringComparer.Ordinal);

    /// <summary>The number of templates in <see cref="Cache"/>.</summary>
    private static int s_cached;

    [ThreadStatic]
    private static int t_depth;

    /// <summary>How deep templates are nested on this thread right now (0 outside an evaluation).</summary>
    internal static int Depth => t_depth;

    private readonly Segment[] _segments;

    private Template(string text, Segment[] segments)
    {
        Text = text;
        _segments = segments;
    }

    /// <summary>The template source text.</summary>
    public string Text { get; }

    /// <summary>
    /// An expression starts with <c>{{</c> followed by a letter: <c>{{person.firstName}}</c> (faker.js style) or
    /// <c>{{Person.FirstName}}</c> (.NET style). The data compiler rejects locale data that would read differently
    /// under faker.js, which only recognizes lower-case starts.
    /// </summary>
    public static bool ContainsExpression(string text)
    {
        var index = text.IndexOf("{{", StringComparison.Ordinal);
        while (index >= 0)
        {
            if (index + 2 < text.Length && char.IsAsciiLetter(text[index + 2]))
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
        // Its own counter: ConcurrentDictionary.Count takes every lock of the dictionary, on each miss.
        if (Volatile.Read(ref s_cached) < MaxCachedTemplates && Cache.TryAdd(text, template))
            Interlocked.Increment(ref s_cached);
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
            var end = start < 0 ? -1 : FindExpressionEnd(text, start + 2);
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

    /// <summary>Counts one more nested evaluation on this thread, and stops a template that (through its data) evaluates itself without end.</summary>
    private static void Enter(string text)
    {
        if (++t_depth > MaxDepth)
        {
            t_depth--; // the frames above unwind through their finally blocks and bring the depth back to 0
            throw new PhoneyDataException($"Template recursion is too deep while evaluating '{text}'. Does the data reference itself?");
        }
    }

    /// <summary>Ends a nested evaluation started with <see cref="Enter"/>.</summary>
    private static void Leave() => t_depth--;

    /// <summary>Evaluates the template with <paramref name="faker"/>.</summary>
    public string Evaluate(Faker faker)
    {
        Enter(Text);

        try
        {
            if (_segments.Length == 1)
                return _segments[0].Evaluate(faker);

            // Joined on the stack, nested templates included: the result string is the only allocation.
            var text = new TextBuilder(stackalloc char[256]);
            foreach (var segment in _segments)
                segment.AppendTo(ref text, faker);
            return text.ToStringAndDispose();
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Evaluates the template into <paramref name="text"/>, drawing exactly what <see cref="Evaluate"/> draws.</summary>
    public void AppendTo(ref TextBuilder text, Faker faker)
    {
        Enter(Text);

        try
        {
            foreach (var segment in _segments)
                segment.AppendTo(ref text, faker);
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Index of the next <c>{{</c> followed by a letter, or -1.</summary>
    /// <summary>
    /// The <c>}}</c> that closes the expression starting at <paramref name="from"/>: braces inside its arguments (<c>{length: {min: 2, max: 5}}</c>,
    /// a <c>}}</c> in quoted text) don't end it, so options can hold objects. -1 when there is none; the text is then literal, as before.
    /// </summary>
    private static int FindExpressionEnd(string text, int from)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var i = from; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == quote)
                    quote = '\0';
            }
            else if (depth > 0 && c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0 && c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                return i;
            }
        }

        return text.IndexOf("}}", from, StringComparison.Ordinal); // unbalanced: end where the expression ended before, so its error stays the same
    }

    private static int FindExpressionStart(string text, int from)
    {
        var index = text.IndexOf("{{", from, StringComparison.Ordinal);
        while (index >= 0 && !(index + 2 < text.Length && char.IsAsciiLetter(text[index + 2])))
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

        /// <summary>The built-in behind <see cref="_function"/> when it can write its text straight into the caller's buffer, otherwise <see langword="null"/>.</summary>
        private TemplateWriter? _writer;

        /// <summary>Arguments parsed once from <c>name(args)</c> expressions.</summary>
        private TemplateArgs _args = TemplateArgs.Empty;

        /// <summary>The function's name as written, when it is a built-in that must use every option it is given (see <see cref="TemplateArgs.CheckOptionsRead"/>); otherwise <see langword="null"/>.</summary>
        private string? _checkedFunction;

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
                    ?? throw new FormatException($"Unknown template function '{name}' in '{{{{{expression}}}}}'.{NameSuggestions.For(name, TemplateFunctions.Names)}");
                segment._args = TemplateArgs.Parse(expression[(parenthesis + 1)..^1]);
                segment._writer = WritingFunction.WriterOf(segment._function);
                if (!TemplateFunctions.IsRegisteredByUser(name))
                    segment._checkedFunction = name; // a function of the user's own reads its arguments as it likes
                return segment;
            }

            segment._function = TemplateFunctions.Get(expression);
            if (segment._function is null)
                segment.ResolveDataPath();
            else
                segment._writer = WritingFunction.WriterOf(segment._function);
            return segment;
        }

        /// <summary>Writes the segment's text into <paramref name="text"/>, with the same draws as <see cref="Evaluate"/>; nested templates and writing built-ins add no string of their own.</summary>
        public void AppendTo(ref TextBuilder text, Faker faker)
        {
            if (Literal is not null)
            {
                text.Append(Literal);
                return;
            }

            if (_writer is not null)
            {
                var mark = text.Length;
                try
                {
                    _writer(faker, _args, ref text);
                    if (_checkedFunction is not null)
                        _args.CheckOptionsRead(_checkedFunction);
                }
                catch (PhoneyDataUnavailableException)
                {
                    text.Length = mark; // as in Evaluate: the part is left empty, also when it failed halfway
                }

                return;
            }

            if (_function is null && _key >= 0 && faker.Data.Get(_key) is StringsEntry strings)
            {
                strings.AppendTo(strings.PickIndex(faker.Random), faker, ref text);
                return;
            }

            text.Append(Evaluate(faker));
        }

        /// <summary>Produces the segment's text.</summary>
        public string Evaluate(Faker faker)
        {
            if (Literal is not null)
                return Literal;
            if (_function is not null)
                return Call(_function, faker);
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
                $"Cannot resolve '{{{{{Expression}}}}}' in locale '{faker.Locale}': it is neither a template function nor available locale data."
                + NameSuggestions.For(Expression, [.. TemplateFunctions.Names, .. KeyRegistry.AllPaths()]));
        }

        /// <summary>Calls the segment's template function.</summary>
        private string Call(TemplateFunction function, Faker faker)
        {
            try
            {
                var text = function(faker, _args);
                if (_checkedFunction is not null)
                    _args.CheckOptionsRead(_checkedFunction);
                return text;
            }
            catch (PhoneyDataUnavailableException)
            {
                // A pattern inherited from a fallback locale may use data that is not applicable here
                // (e.g. en's postal address uses states, which az doesn't have): leave that part empty.
                return "";
            }
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
