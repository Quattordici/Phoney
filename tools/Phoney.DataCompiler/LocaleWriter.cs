using System.IO.Compression;
using System.Text;

namespace Phoney.DataCompiler;

/// <summary>
/// Writes a locale's own (unmerged) entries in Phoney's binary format, Brotli-compressed.
/// The reader lives in <c>src/Phoney/Data/LocaleResource.cs</c>; keep both in sync and bump
/// <see cref="FormatVersion"/> on any layout change.
/// </summary>
/// <remarks>
/// Layout (all integers 7-bit encoded unless noted):
/// <code>
/// "PHNY"  formatVersion:u8  fakerVersion:string  catalogKeyCount
/// stringCount  string*                      (deduplicated string table)
/// entryCount   entry*
/// entry   = keyId  kind:u8  payload
/// Strings = n  stringIndex*n
/// Weighted= n  (stringIndex weight)*n
/// Ints    = n  int32*n
/// Records = fieldCount  stringIndex*fieldCount  rowCount  (stringIndex+1 | 0 for missing)*(rowCount*fieldCount)
/// </code>
/// </remarks>
internal static class LocaleWriter
{
    public const byte FormatVersion = 1;

    /// <summary>Serializes the locale's own leaves and Brotli-compresses them.</summary>
    public static byte[] Write(RawLocale locale, Catalog catalog, string fakerVersion)
    {
        var strings = new StringTable();
        var leaves = locale.Leaves.Values.OrderBy(l => catalog.KeyIds[l.Path]).ToList();

        // Entries are written to a separate buffer first so the string table can precede them.
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            w.Write7BitEncodedInt(leaves.Count);
            foreach (var leaf in leaves)
                WriteEntry(w, leaf, catalog.KeyIds[leaf.Path], strings);
        }

        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            w.Write("PHNY"u8);
            w.Write(FormatVersion);
            w.Write(fakerVersion);
            w.Write7BitEncodedInt(catalog.Keys.Count);
            w.Write7BitEncodedInt(strings.Values.Count);
            foreach (var s in strings.Values)
                w.Write(s);
            w.Write(body.ToArray());
        }

        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(raw.ToArray());
        return compressed.ToArray();
    }

    /// <summary>Writes one entry: its key, its kind and the payload of that kind.</summary>
    private static void WriteEntry(BinaryWriter w, LeafEntry leaf, int keyId, StringTable strings)
    {
        w.Write7BitEncodedInt(keyId);
        w.Write((byte)leaf.Kind);
        switch (leaf.Kind)
        {
            case EntryKind.Unavailable:
                break;
            case EntryKind.Strings:
                WriteStrings(w, leaf, strings);
                break;
            case EntryKind.Weighted:
                WriteWeighted(w, leaf, strings);
                break;
            case EntryKind.Ints:
                WriteInts(w, leaf);
                break;
            case EntryKind.Records:
                WriteRecords(w, leaf, strings);
                break;
            default:
                throw new InvalidOperationException($"Unknown kind {leaf.Kind}.");
        }
    }

    private static void WriteStrings(BinaryWriter w, LeafEntry leaf, StringTable strings)
    {
        w.Write7BitEncodedInt(leaf.Strings.Count);
        foreach (var s in leaf.Strings)
            w.Write7BitEncodedInt(strings.Intern(s));
    }

    private static void WriteWeighted(BinaryWriter w, LeafEntry leaf, StringTable strings)
    {
        w.Write7BitEncodedInt(leaf.Strings.Count);
        for (var i = 0; i < leaf.Strings.Count; i++)
        {
            w.Write7BitEncodedInt(strings.Intern(leaf.Strings[i]));
            w.Write7BitEncodedInt(leaf.Weights[i]);
        }
    }

    private static void WriteInts(BinaryWriter w, LeafEntry leaf)
    {
        w.Write7BitEncodedInt(leaf.Ints.Count);
        foreach (var n in leaf.Ints)
            w.Write(n);
    }

    private static void WriteRecords(BinaryWriter w, LeafEntry leaf, StringTable strings)
    {
        w.Write7BitEncodedInt(leaf.Fields.Count);
        foreach (var f in leaf.Fields)
            w.Write7BitEncodedInt(strings.Intern(f));
        w.Write7BitEncodedInt(leaf.Rows.Count);
        foreach (var row in leaf.Rows)
            foreach (var value in row)
                w.Write7BitEncodedInt(value is null ? 0 : strings.Intern(value) + 1);
    }

    /// <summary>The deduplicated string table, in the order the entries first use each string.</summary>
    private sealed class StringTable
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);

        public List<string> Values { get; } = [];

        public int Intern(string value)
        {
            if (!_ids.TryGetValue(value, out var id))
            {
                id = Values.Count;
                Values.Add(value);
                _ids.Add(value, id);
            }

            return id;
        }
    }
}
