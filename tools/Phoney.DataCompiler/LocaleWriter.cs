using System.IO.Compression;
using System.Text;

namespace Phoney.DataCompiler;

/// <summary>
/// Writes a locale's own (unmerged) entries in Phoney's binary format, Brotli-compressed.
/// The reader lives in <c>src/Phoney/Data/LocaleResourceReader.cs</c>; keep both in sync and bump
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
        var strings = new List<string>();
        var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string s)
        {
            if (!stringIds.TryGetValue(s, out var id))
            {
                id = strings.Count;
                strings.Add(s);
                stringIds.Add(s, id);
            }

            return id;
        }

        var leaves = locale.Leaves.Values.OrderBy(l => catalog.KeyIds[l.Path]).ToList();

        // Entries are written to a separate buffer first so the string table can precede them.
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
        {
            w.Write7BitEncodedInt(leaves.Count);
            foreach (var leaf in leaves)
            {
                w.Write7BitEncodedInt(catalog.KeyIds[leaf.Path]);
                w.Write((byte)leaf.Kind);
                switch (leaf.Kind)
                {
                    case EntryKind.Unavailable:
                        break;
                    case EntryKind.Strings:
                        w.Write7BitEncodedInt(leaf.Strings.Count);
                        foreach (var s in leaf.Strings)
                            w.Write7BitEncodedInt(Intern(s));
                        break;
                    case EntryKind.Weighted:
                        w.Write7BitEncodedInt(leaf.Strings.Count);
                        for (var i = 0; i < leaf.Strings.Count; i++)
                        {
                            w.Write7BitEncodedInt(Intern(leaf.Strings[i]));
                            w.Write7BitEncodedInt(leaf.Weights[i]);
                        }

                        break;
                    case EntryKind.Ints:
                        w.Write7BitEncodedInt(leaf.Ints.Count);
                        foreach (var n in leaf.Ints)
                            w.Write(n);
                        break;
                    case EntryKind.Records:
                        w.Write7BitEncodedInt(leaf.Fields.Count);
                        foreach (var f in leaf.Fields)
                            w.Write7BitEncodedInt(Intern(f));
                        w.Write7BitEncodedInt(leaf.Rows.Count);
                        foreach (var row in leaf.Rows)
                            foreach (var value in row)
                                w.Write7BitEncodedInt(value is null ? 0 : Intern(value) + 1);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown kind {leaf.Kind}.");
                }
            }
        }

        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true))
        {
            w.Write("PHNY"u8);
            w.Write(FormatVersion);
            w.Write(fakerVersion);
            w.Write7BitEncodedInt(catalog.Keys.Count);
            w.Write7BitEncodedInt(strings.Count);
            foreach (var s in strings)
                w.Write(s);
            w.Write(body.ToArray());
        }

        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(raw.ToArray());
        return compressed.ToArray();
    }
}
