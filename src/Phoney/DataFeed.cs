using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Phoney;

/// <summary>How a generator takes rows from a <see cref="DataFeed"/>.</summary>
public enum FeedOrder
{
    /// <summary>Object <c>n</c> takes row <c>n</c>, and the rows start again after the last one.</summary>
    InOrder,

    /// <summary>Every object takes a row chosen from the generator's seed and the object's position, so the choice is reproducible and the same however the objects are generated.</summary>
    Random,
}

/// <summary>
/// Rows of text values with named columns, from a CSV or JSON source, that fill the members of generated objects:
/// <c>Fake.For&lt;Customer&gt;().WithFeed(DataFeed.FromCsvFile("customers.csv")).Generate(100)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A feed is read completely and checked when it is created: ragged CSV rows, duplicate columns and JSON that is not a flat array of objects
/// throw an <see cref="InvalidOperationException"/> that says where. Which column fills which member, and whether a value fits the member's type, is
/// checked when a generator with the feed is first used.
/// </para>
/// <para>Feeds are immutable and can be shared between generators.</para>
/// </remarks>
public sealed class DataFeed
{
    private readonly string?[][] _rows;

    private DataFeed(string[] columns, string?[][] rows)
    {
        Columns = columns;
        _rows = rows;
    }

    /// <summary>The column names, in the order of the source.</summary>
    public IReadOnlyList<string> Columns { get; }

    /// <summary>The number of rows.</summary>
    public int Count => _rows.Length;

    /// <summary>The value of a column in a row: <see langword="null"/> only for a JSON <c>null</c>; an empty CSV cell is an empty string.</summary>
    internal string? Value(int row, int column) => _rows[row][column];

    /// <summary>Reads CSV text (RFC 4180: quoted values may hold delimiters, quotes as <c>""</c> and line breaks) whose first line holds the column names.</summary>
    /// <param name="csv">The text; a byte order mark is ignored and blank lines are skipped.</param>
    /// <param name="delimiter">The separator, <c>,</c> by default (<c>;</c> and tab are common).</param>
    /// <exception cref="InvalidOperationException">No header, no rows, duplicate columns, a row with another number of values than the header, or an unterminated quote.</exception>
    public static DataFeed FromCsv(string csv, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(csv);
        if (delimiter is '"' or '\r' or '\n')
            throw new ArgumentException("The delimiter can't be a quote or a line break.", nameof(delimiter));

        var records = ReadCsv(csv, delimiter);
        if (records.Count == 0)
            throw new InvalidOperationException("The CSV has no header line with the column names.");

        var columns = CheckColumns(records[0].Fields);
        var rows = new string?[records.Count - 1][];
        for (var i = 1; i < records.Count; i++)
        {
            var (line, fields) = records[i];
            if (fields.Count != columns.Length)
                throw new InvalidOperationException($"CSV line {line} has {fields.Count} values, but the header has {columns.Length} columns.");
            rows[i - 1] = [.. fields];
        }

        return Create(columns, rows, "CSV");
    }

    /// <summary>Reads a CSV file, see <see cref="FromCsv"/>.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static DataFeed FromCsvFile(string path, char delimiter = ',')
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromCsv(File.ReadAllText(path), delimiter);
    }

    /// <summary>Reads a JSON array of flat objects with the same properties: <c>[{"name":"Ada","age":36}, {"name":"Grace","age":85}]</c>.</summary>
    /// <remarks>Strings, numbers (as written), <c>true</c>/<c>false</c> and <c>null</c> are values; nested objects and arrays are not.</remarks>
    /// <exception cref="InvalidOperationException">The JSON is not an array of flat objects with the same properties, or it is empty.</exception>
    /// <exception cref="JsonException">The text is not valid JSON.</exception>
    public static DataFeed FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The JSON must be an array of objects, such as [{\"name\":\"Ada\"}].");

        string[]? names = null; // as written in the JSON, for lookups
        string[]? columns = null; // trimmed
        var rows = new List<string?[]>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Item {rows.Count + 1} of the JSON array is not an object.");

            var properties = element.EnumerateObject().Select(p => p.Name).ToArray();
            if (names is null)
            {
                columns = CheckColumns(properties);
                names = properties;
            }

            if (properties.Length != names.Length || !properties.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidOperationException($"Item {rows.Count + 1} of the JSON array does not have the same properties as the first one ({string.Join(", ", columns!)}).");

            var row = new string?[names.Length];
            for (var c = 0; c < names.Length; c++)
                row[c] = JsonValue(element.GetProperty(names[c]), columns![c], rows.Count + 1);
            rows.Add(row);
        }

        return Create(columns ?? [], [.. rows], "JSON");
    }

    /// <summary>Reads a JSON file, see <see cref="FromJson"/>.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static DataFeed FromJsonFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(File.ReadAllText(path));
    }

    private static DataFeed Create(string[] columns, string?[][] rows, string format)
    {
        if (rows.Length == 0)
            throw new InvalidOperationException($"The {format} has no rows: a feed needs at least one row of values.");
        return new DataFeed(columns, rows);
    }

    private static string[] CheckColumns(IReadOnlyCollection<string> names)
    {
        var columns = names.Select(n => n.Trim()).ToArray();
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].Length == 0)
                throw new InvalidOperationException($"Column {i + 1} has no name.");
            for (var j = 0; j < i; j++)
            {
                if (string.Equals(Normalize(columns[i]), Normalize(columns[j]), StringComparison.Ordinal))
                    throw new InvalidOperationException($"The columns '{columns[j]}' and '{columns[i]}' are the same name twice (case, spaces and underscores don't count).");
            }
        }

        return columns;
    }

    /// <summary>The form in which column and member names are compared: letters and digits, lower case.</summary>
    internal static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Where(char.IsLetterOrDigit))
            builder.Append(char.ToLowerInvariant(c));

        return builder.ToString();
    }

    private static string? JsonValue(JsonElement value, string column, int item) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException($"'{column}' of item {item} of the JSON array is an object or an array: a feed holds flat values only."),
    };

    /// <summary>Splits CSV text into records of fields, each with the line it starts on.</summary>
    private static List<(int Line, List<string> Fields)> ReadCsv(string text, char delimiter) => new CsvReader(text, delimiter).Read();

    /// <summary>A single pass over CSV text (RFC 4180: quoted fields, doubled quotes, line breaks inside quotes, CRLF or LF).</summary>
    private sealed class CsvReader(string text, char delimiter)
    {
        private readonly List<(int Line, List<string> Fields)> _records = [];
        private readonly StringBuilder _field = new();
        private List<string> _fields = [];
        private int _position;
        private int _line = 1;
        private int _recordLine = 1;
        private int _quoteLine;
        private bool _inQuotes;
        private bool _atFieldStart = true;
        private bool _quoted;

        public List<(int Line, List<string> Fields)> Read()
        {
            _position = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0; // a byte order mark is not data
            while (_position < text.Length)
            {
                var c = text[_position++];
                if (_inQuotes)
                    Quoted(c);
                else
                    Plain(c);
            }

            if (_inQuotes)
                throw new InvalidOperationException($"The quote that opens a value on CSV line {_quoteLine} is never closed.");
            EndRecord();
            return _records;
        }

        /// <summary>A character inside quotes: a doubled quote is one quote, a single one closes the value.</summary>
        private void Quoted(char c)
        {
            if (c != '"')
            {
                if (c == '\n')
                    _line++;
                _field.Append(c);
            }
            else if (Next('"'))
            {
                _field.Append('"');
            }
            else
            {
                _inQuotes = false;
            }
        }

        /// <summary>A character outside quotes: a quote opening a value, a delimiter, a line break ending the record, or text.</summary>
        private void Plain(char c)
        {
            if (c == '"' && _atFieldStart)
            {
                _inQuotes = _quoted = true;
                _quoteLine = _line;
                _atFieldStart = false;
            }
            else if (c == delimiter)
            {
                _fields.Add(_field.ToString());
                _field.Clear();
                _atFieldStart = true;
                _quoted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r')
                    Next('\n');
                EndRecord();
                _line++;
                _recordLine = _line;
            }
            else
            {
                _field.Append(c);
                _atFieldStart = false;
            }
        }

        /// <summary>Consumes the next character when it is <paramref name="expected"/>.</summary>
        private bool Next(char expected)
        {
            if (_position >= text.Length || text[_position] != expected)
                return false;
            _position++;
            return true;
        }

        private void EndRecord()
        {
            // A line with nothing on it is not a record.
            if (_fields.Count == 0 && _field.Length == 0 && !_quoted)
                return;
            _fields.Add(_field.ToString());
            _records.Add((_recordLine, _fields));
            _fields = [];
            _field.Clear();
            _atFieldStart = true;
            _quoted = false;
        }
    }
}
