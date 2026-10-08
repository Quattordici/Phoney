using Phoney.Templates;

namespace Phoney.Data;

/// <summary>Shape of a locale data entry. Numeric values are part of the binary resource format.</summary>
public enum EntryKind : byte
{
    /// <summary>The locale explicitly has no data for this key (faker.js <c>null</c>); fallback locales are not consulted.</summary>
    Unavailable = 0,

    /// <summary>A list of strings, possibly <c>{{...}}</c> templates.</summary>
    Strings = 1,

    /// <summary>Strings with relative weights.</summary>
    Weighted = 2,

    /// <summary>A list of integers.</summary>
    Ints = 3,

    /// <summary>A table of records with named fields (e.g. airports, currencies).</summary>
    Records = 4,
}

/// <summary>An immutable locale data value. Shared between threads and <see cref="Faker"/> instances.</summary>
public abstract class Entry
{
    /// <summary>Entries are created by Phoney only.</summary>
    private protected Entry()
    {
    }

    /// <summary>The shape of this entry.</summary>
    public abstract EntryKind Kind { get; }
}

/// <summary>Marks data explicitly unavailable in a locale.</summary>
public sealed class UnavailableEntry : Entry
{
    internal static readonly UnavailableEntry Instance = new();

    private UnavailableEntry()
    {
    }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Unavailable;
}

/// <summary>A list of strings, optionally weighted. Strings containing <c>{{...}}</c> are templates.</summary>
public class StringsEntry : Entry
{
    private readonly Template?[]? _templates;

    internal StringsEntry(string[] values)
    {
        if (values.Length == 0)
            throw new ArgumentException("A strings entry needs at least one value.", nameof(values));
        Values = values;
        if (Array.Exists(values, Template.ContainsExpression))
            _templates = new Template?[values.Length];
    }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Strings;

    /// <summary>The raw values.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Whether any value is a template.</summary>
    public bool HasTemplates => _templates is not null;

    internal string this[int index] => ((string[])Values)[index];

    /// <summary>Number of values.</summary>
    internal int Count => ((string[])Values).Length;

    /// <summary>Picks a value index; uniform here, weighted in <see cref="WeightedEntry"/>.</summary>
    internal virtual int PickIndex(Randomizer random) => random.Index(Count);

    /// <summary>Returns the value at <paramref name="index"/> with any templates evaluated.</summary>
    internal string Resolve(int index, Faker faker)
    {
        if (_templates is null)
            return this[index];
        var template = _templates[index] ??= Template.Parse(this[index]); // benign race: parsing is idempotent
        return template.Evaluate(faker);
    }
}

/// <summary>Strings picked according to relative weights.</summary>
public sealed class WeightedEntry : StringsEntry
{
    private readonly int[] _cumulative;

    internal WeightedEntry(string[] values, int[] weights) : base(values)
    {
        if (weights.Length != values.Length)
            throw new ArgumentException("Every value needs a weight.", nameof(weights));
        Weights = weights;
        _cumulative = new int[weights.Length];
        var total = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            total = checked(total + Math.Max(0, weights[i]));
            _cumulative[i] = total;
        }

        if (total == 0)
            throw new ArgumentException("At least one weight must be positive.", nameof(weights));
    }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Weighted;

    /// <summary>The weight of each value.</summary>
    public IReadOnlyList<int> Weights { get; }

    /// <summary>Picks an index with probability proportional to its weight.</summary>
    internal override int PickIndex(Randomizer random) => random.WeightedIndex(_cumulative);
}

/// <summary>A list of integers.</summary>
public sealed class IntsEntry : Entry
{
    internal IntsEntry(int[] values)
    {
        if (values.Length == 0)
            throw new ArgumentException("An ints entry needs at least one value.", nameof(values));
        Values = values;
    }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Ints;

    /// <summary>The values.</summary>
    public IReadOnlyList<int> Values { get; }
}

/// <summary>A table of records such as airports or currencies; values are stored as strings.</summary>
public sealed class RecordsEntry : Entry
{
    private readonly string?[] _values;

    internal RecordsEntry(string[] fields, string?[] rowMajorValues)
    {
        if (fields.Length == 0 || rowMajorValues.Length == 0 || rowMajorValues.Length % fields.Length != 0)
            throw new ArgumentException("Record values must contain whole rows.", nameof(rowMajorValues));
        Fields = fields;
        _values = rowMajorValues;
        Count = rowMajorValues.Length / fields.Length;
    }

    /// <inheritdoc />
    public override EntryKind Kind => EntryKind.Records;

    /// <summary>Field names.</summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>Number of records.</summary>
    public int Count { get; }

    /// <summary>Index of <paramref name="field"/>, or -1.</summary>
    public int FieldIndex(string field) => Array.IndexOf((string[])Fields, field);

    /// <summary>Value of <paramref name="field"/> in record <paramref name="row"/>; <see langword="null"/> when the record lacks it.</summary>
    public string? Get(int row, int field) => _values[(row * Fields.Count) + field];

    /// <summary>Value of <paramref name="field"/> in record <paramref name="row"/>.</summary>
    public string? Get(int row, string field)
    {
        var index = FieldIndex(field);
        if (index < 0)
            throw new ArgumentException($"Unknown field '{field}'. Fields: {string.Join(", ", Fields)}.", nameof(field));
        return Get(row, index);
    }
}
