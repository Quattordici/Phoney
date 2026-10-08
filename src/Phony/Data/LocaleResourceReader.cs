using System.IO.Compression;
using System.Text;

namespace Phony.Data;

/// <summary>
/// Reads the embedded binary locale resources written by <c>tools/Phony.DataCompiler</c> (see <c>LocaleWriter</c> there for the layout).
/// </summary>
internal static class LocaleResourceReader
{
    private const byte FormatVersion = 1;

    /// <summary>Reads the embedded data of <paramref name="code"/>, or <see langword="null"/> when there is no such resource.</summary>
    public static Entry?[]? ReadEmbedded(string code)
    {
        using var stream = typeof(LocaleResourceReader).Assembly.GetManifestResourceStream($"Phony.Locales.{code}.bin.br");
        if (stream is null)
            return null;
        using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
        using var buffered = new BufferedStream(brotli, 64 * 1024);
        return Read(buffered, code);
    }

    /// <summary>Parses a decompressed resource into entries indexed by key id.</summary>
    private static Entry?[] Read(Stream stream, string code)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8);
        Span<byte> magic = stackalloc byte[4];
        r.BaseStream.ReadExactly(magic);
        if (!magic.SequenceEqual("PHNY"u8))
            throw new InvalidDataException($"Locale resource '{code}' is not Phony locale data.");
        var version = r.ReadByte();
        if (version != FormatVersion)
            throw new InvalidDataException($"Locale resource '{code}' has format {version}; expected {FormatVersion}.");
        _ = r.ReadString(); // faker.js version
        var keyCount = r.Read7BitEncodedInt();
        if (keyCount != DataKeys.Count)
            throw new InvalidDataException($"Locale resource '{code}' was compiled for {keyCount} keys but the catalog has {DataKeys.Count}. Re-run the data compiler.");

        var strings = new string[r.Read7BitEncodedInt()];
        for (var i = 0; i < strings.Length; i++)
            strings[i] = r.ReadString();

        var entries = new Entry?[DataKeys.Count];
        var entryCount = r.Read7BitEncodedInt();
        for (var e = 0; e < entryCount; e++)
        {
            var key = r.Read7BitEncodedInt();
            var kind = (EntryKind)r.ReadByte();
            entries[key] = kind switch
            {
                EntryKind.Unavailable => UnavailableEntry.Instance,
                EntryKind.Strings => ReadStrings(r, strings),
                EntryKind.Weighted => ReadWeighted(r, strings),
                EntryKind.Ints => ReadInts(r),
                EntryKind.Records => ReadRecords(r, strings),
                _ => throw new InvalidDataException($"Locale resource '{code}' has unknown entry kind {kind}."),
            };
        }

        return entries;
    }

    // Empty lists and tables are read as missing data (null) rather than as entries.
    /// <summary>Reads a list of string indices.</summary>
    private static Entry? ReadStrings(BinaryReader r, string[] strings)
    {
        var values = new string[r.Read7BitEncodedInt()];
        for (var i = 0; i < values.Length; i++)
            values[i] = strings[r.Read7BitEncodedInt()];
        return values.Length == 0 ? null : new StringsEntry(values);
    }

    /// <summary>Reads (string index, weight) pairs.</summary>
    private static Entry? ReadWeighted(BinaryReader r, string[] strings)
    {
        var count = r.Read7BitEncodedInt();
        var values = new string[count];
        var weights = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = strings[r.Read7BitEncodedInt()];
            weights[i] = r.Read7BitEncodedInt();
        }

        return count == 0 ? null : new WeightedEntry(values, weights);
    }

    /// <summary>Reads a list of 32-bit integers.</summary>
    private static Entry? ReadInts(BinaryReader r)
    {
        var values = new int[r.Read7BitEncodedInt()];
        for (var i = 0; i < values.Length; i++)
            values[i] = r.ReadInt32();
        return values.Length == 0 ? null : new IntsEntry(values);
    }

    /// <summary>Reads field names and row-major values (index + 1, 0 for missing).</summary>
    private static Entry? ReadRecords(BinaryReader r, string[] strings)
    {
        var fields = new string[r.Read7BitEncodedInt()];
        for (var i = 0; i < fields.Length; i++)
            fields[i] = strings[r.Read7BitEncodedInt()];
        var rows = r.Read7BitEncodedInt();
        var values = new string?[rows * fields.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var index = r.Read7BitEncodedInt();
            values[i] = index == 0 ? null : strings[index - 1];
        }

        return rows == 0 ? null : new RecordsEntry(fields, values);
    }
}
