using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Phoney.Generation;

namespace Phoney;

/// <summary>
/// Writes generated objects as CSV or JSON, the formats <see cref="DataFeed"/> reads back: fixtures, seed files and sample data from the same models and rules as the tests.
/// </summary>
/// <remarks>
/// <para>
/// One row (or JSON object) per generated object, with one column per scalar member (strings, numbers, booleans, enums, dates and times, <see cref="Guid"/>, <see cref="Uri"/>…) that can be
/// read from an existing object. Nested objects and collections are not written; list the scalar members you want in <c>columns</c> to choose and order them. Values use the invariant culture
/// and round-trip formats (<c>2025-06-01T00:00:00.0000000+00:00</c>), so reading the file with <see cref="DataFeed"/> and <see cref="ObjectGenerator{T}.WithFeed"/> gives the objects back.
/// </para>
/// <para>
/// The objects are those of <see cref="ObjectGenerator{T}.Generate(int)"/>, so a seeded generator writes the same file on every run, and the next call continues the sequence. Nothing here uses
/// reflection beyond what the generator's model does, so it works with source-generated models in trimmed and Native AOT apps.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Fake.For&lt;Customer&gt;().Seed(1).WriteCsvFile("customers.csv", 500);
/// string json = Fake.For&lt;Customer&gt;().ToJson(10);
/// </code>
/// </example>
public static class DataExport
{
    /// <summary>Generates <paramref name="count"/> objects and returns them as CSV text: a header, then one line per object, with <c>\n</c> as the line ending.</summary>
    /// <remarks>A zero count returns only the header. Null values and empty strings both produce empty fields. Generation and member-access exceptions propagate to the caller.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="delimiter">The field separator; not a quote or a line break.</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">The delimiter is a quote or a line break, or a column is null, unknown, not a scalar member, not readable or listed twice, or the list is empty.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    public static string ToCsv<T>(this ObjectGenerator<T> generator, int count, char delimiter = ',', IEnumerable<string>? columns = null)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        generator.WriteCsv(writer, count, delimiter, columns);
        return writer.ToString();
    }

    /// <summary>Generates <paramref name="count"/> objects and writes them to <paramref name="writer"/> as CSV (see <see cref="ToCsv{T}"/>); the writer is not closed.</summary>
    /// <remarks>Objects are generated before output begins. Generation, member-access and writer exceptions propagate; a failure during output can leave partial CSV.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="writer">Where the text goes.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="delimiter">The field separator; not a quote or a line break.</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">See <see cref="ToCsv{T}"/>.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    public static void WriteCsv<T>(this ObjectGenerator<T> generator, TextWriter writer, int count, char delimiter = ',', IEnumerable<string>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (delimiter is '"' or '\r' or '\n')
            throw new ArgumentException("The delimiter can't be a quote or a line break.", nameof(delimiter));

        var model = generator.Model;
        var selected = Select(model, columns);
        var items = generator.Generate(count);

        for (var c = 0; c < selected.Length; c++)
        {
            if (c > 0)
                writer.Write(delimiter);
            WriteCsvField(writer, selected[c].Member.Name, delimiter, alone: selected.Length == 1);
        }

        writer.Write('\n');
        foreach (var item in items)
        {
            for (var c = 0; c < selected.Length; c++)
            {
                if (c > 0)
                    writer.Write(delimiter);
                WriteCsvField(writer, Format(model.Get(item, selected[c].Index)), delimiter, alone: selected.Length == 1);
            }

            writer.Write('\n');
        }
    }

    /// <summary>Generates <paramref name="count"/> objects and writes them to the file at <paramref name="path"/> as CSV (UTF-8 without a byte order mark); an existing file is replaced.</summary>
    /// <remarks>The file is created or truncated before count, delimiter and column validation. File-access and <see cref="WriteCsv{T}"/> exceptions propagate; a failure can leave an empty or partial file.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="path">The file to create.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="delimiter">The field separator; not a quote or a line break.</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace, or see <see cref="ToCsv{T}"/>.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    /// <exception cref="IOException">The file cannot be created or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to <paramref name="path"/> is denied, or it names a directory.</exception>
    public static void WriteCsvFile<T>(this ObjectGenerator<T> generator, string path, int count, char delimiter = ',', IEnumerable<string>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        generator.WriteCsv(writer, count, delimiter, columns);
    }

    /// <summary>
    /// Generates <paramref name="count"/> objects and returns them as a JSON array of flat objects. Numbers and booleans are JSON numbers and booleans, everything else a string,
    /// and a <see langword="null"/> is <c>null</c>. Nonfinite floating-point values are strings (<c>"NaN"</c>, <c>"Infinity"</c> or <c>"-Infinity"</c>).
    /// </summary>
    /// <remarks>A zero count returns an empty array. Generation and member-access exceptions propagate to the caller.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="indented">Whether to indent the output (on by default).</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">A column is null, unknown, not a scalar member, not readable or listed twice, or the list is empty.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    public static string ToJson<T>(this ObjectGenerator<T> generator, int count, bool indented = true, IEnumerable<string>? columns = null)
    {
        using var stream = new MemoryStream();
        generator.WriteJson(stream, count, indented, columns);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Generates <paramref name="count"/> objects and writes them to <paramref name="utf8Stream"/> as a JSON array (see <see cref="ToJson{T}"/>); the stream is not closed.</summary>
    /// <remarks>Objects are generated before output begins. Generation, member-access and stream exceptions propagate; a failure during output can leave partial JSON.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="utf8Stream">Where the UTF-8 text goes.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="indented">Whether to indent the output (on by default).</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> or <paramref name="utf8Stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">See <see cref="ToJson{T}"/>.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    public static void WriteJson<T>(this ObjectGenerator<T> generator, Stream utf8Stream, int count, bool indented = true, IEnumerable<string>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var model = generator.Model;
        var selected = Select(model, columns);
        var items = generator.Generate(count);

        var options = new JsonWriterOptions
        {
            Indented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // readable non-ASCII text; this is a data file, not HTML
#if NET9_0_OR_GREATER
            NewLine = "\n",
#endif
        };
        using var json = new Utf8JsonWriter(utf8Stream, options);
        json.WriteStartArray();
        foreach (var item in items)
        {
            json.WriteStartObject();
            foreach (var (index, member) in selected)
            {
                json.WritePropertyName(member.Name);
                WriteJsonValue(json, member, model.Get(item, index));
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.Flush();
    }

    /// <summary>Generates <paramref name="count"/> objects and writes them to the file at <paramref name="path"/> as a JSON array (UTF-8 without a byte order mark); an existing file is replaced.</summary>
    /// <remarks>The file is created or truncated before count and column validation. File-access and <see cref="WriteJson{T}"/> exceptions propagate; a failure can leave an empty or partial file.</remarks>
    /// <param name="generator">The generator.</param>
    /// <param name="path">The file to create.</param>
    /// <param name="count">How many objects to write.</param>
    /// <param name="indented">Whether to indent the output (on by default).</param>
    /// <param name="columns">The members to write, in order (case-insensitive); all readable scalar members when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="generator"/> or <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace, or see <see cref="ToJson{T}"/>.</exception>
    /// <exception cref="InvalidOperationException">The type has no scalar member that can be written.</exception>
    /// <exception cref="IOException">The file cannot be created or written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to <paramref name="path"/> is denied, or it names a directory.</exception>
    public static void WriteJsonFile<T>(this ObjectGenerator<T> generator, string path, int count, bool indented = true, IEnumerable<string>? columns = null)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        generator.WriteJson(stream, count, indented, columns);
    }

    /// <summary>The members to write with their indices in the model: the ones named, or every scalar member that can be read.</summary>
    private static (int Index, FakeMember Member)[] Select<T>(FakeModel<T> model, IEnumerable<string>? columns)
    {
        var members = model.Members;
        if (columns is null)
            return AllWritable<T>(members);

        var chosen = new List<(int, FakeMember)>();
        foreach (var name in columns)
        {
            var index = WritableIndex<T>(members, name ?? throw new ArgumentException("A column name is null.", nameof(columns)), out var problem);
            if (problem is not null)
                throw new ArgumentException(problem, nameof(columns));
            if (chosen.Exists(c => c.Item1 == index))
                throw new ArgumentException($"The column '{members[index].Name}' is listed twice.", nameof(columns));
            chosen.Add((index, members[index]));
        }

        return chosen.Count > 0 ? [.. chosen] : throw new ArgumentException("Name at least one column, or pass null for all of them.", nameof(columns));
    }

    /// <summary>Every scalar member that can be read, in model order.</summary>
    private static (int Index, FakeMember Member)[] AllWritable<T>(IReadOnlyList<FakeMember> members)
    {
        var readable = new List<(int, FakeMember)>();
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is { IsScalar: true, IsSettable: true, FillsExisting: false } member)
                readable.Add((i, member));
        }

        return readable.Count > 0
            ? [.. readable]
            : throw new InvalidOperationException($"{typeof(T).Name} has no scalar member that can be written: CSV and JSON files hold strings, numbers, booleans, enums, dates, times and the like, not nested objects or collections.");
    }

    /// <summary>The index of the member a column names, checked to be a scalar that can be read from a generated object.</summary>
    /// <param name="members">The members of the model.</param>
    /// <param name="name">The column name.</param>
    /// <param name="problem">Why the column can't be written, or <see langword="null"/> when it can.</param>
    private static int WritableIndex<T>(IReadOnlyList<FakeMember> members, string name, out string? problem)
    {
        var index = IndexOf(members, name);
        problem = index < 0 ? $"{typeof(T).Name} has no member '{name}'." : members[index] switch
        {
            { IsScalar: false } member => $"'{member.Name}' of {typeof(T).Name} is not a scalar member (it is a {member.Type.Name}); a CSV or JSON file holds flat values.",
            { IsSettable: false } or { FillsExisting: true } => $"'{members[index].Name}' of {typeof(T).Name} can't be read from a generated object (it is only set by the constructor).",
            _ => null,
        };
        if (problem is not null)
            problem += $" Members that can be written: {string.Join(", ", WritableNames(members))}.";
        return index;
    }

    /// <summary>Returns the index of the first member whose name matches ignoring case, or -1 when none matches.</summary>
    private static int IndexOf(IReadOnlyList<FakeMember> members, string name)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (string.Equals(members[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>Yields readable scalar member names in model order for column-validation errors.</summary>
    private static IEnumerable<string> WritableNames(IReadOnlyList<FakeMember> members) =>
        members.Where(m => m is { IsScalar: true, IsSettable: true, FillsExisting: false }).Select(m => m.Name);

    /// <summary>A value as text in the formats <see cref="DataFeed"/> reads: invariant culture, round-trip dates, <c>true</c>/<c>false</c>, enum names.</summary>
    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        Uri uri => uri.OriginalString,
        Enum named => named.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>Writes a scalar using its JSON type; nonfinite numbers and other scalar values use the text returned by <see cref="Format"/>.</summary>
    private static void WriteJsonValue(Utf8JsonWriter json, FakeMember member, object? value)
    {
        if (value is null)
        {
            json.WriteNullValue();
            return;
        }

        switch (FakeMember.ScalarCategory(member.Type))
        {
            case TypeCategory.Bool:
                json.WriteBooleanValue((bool)value);
                return;
            case TypeCategory.Single or TypeCategory.Double or TypeCategory.Half when !IsFinite(value):
                json.WriteStringValue(Format(value));
                return;
            case TypeCategory.Byte or TypeCategory.SByte or TypeCategory.Int16 or TypeCategory.UInt16 or TypeCategory.Int32 or TypeCategory.UInt32
                or TypeCategory.Int64 or TypeCategory.UInt64 or TypeCategory.Single or TypeCategory.Double or TypeCategory.Decimal or TypeCategory.Half
                or TypeCategory.Int128 or TypeCategory.UInt128 or TypeCategory.BigInteger or TypeCategory.IntPtr or TypeCategory.UIntPtr:
                json.WriteRawValue(Format(value)!);
                return;
            default:
                json.WriteStringValue(Format(value));
                return;
        }
    }

    /// <summary>Tests floating-point values for finiteness; other values return <see langword="true"/>.</summary>
    private static bool IsFinite(object value) => value switch
    {
        double d => double.IsFinite(d),
        float f => float.IsFinite(f),
        Half h => Half.IsFinite(h),
        _ => true,
    };

    /// <summary>Writes a CSV field, quoted when it holds the delimiter, a quote or a line break (and when it is empty and the only field, so the line is not taken for a blank one).</summary>
    private static void WriteCsvField(TextWriter writer, string? text, char delimiter, bool alone)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (alone)
                writer.Write("\"\"");
            return;
        }

        if (text.AsSpan().IndexOfAny(delimiter, '"', '\r') < 0 && !text.Contains('\n', StringComparison.Ordinal))
        {
            writer.Write(text);
            return;
        }

        writer.Write('"');
        writer.Write(text.Replace("\"", "\"\"", StringComparison.Ordinal));
        writer.Write('"');
    }
}
