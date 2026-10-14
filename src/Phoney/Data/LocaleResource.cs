using System.IO.Compression;
using System.Text;

namespace Phoney.Data;

/// <summary>
/// An embedded locale file, opened without decoding its entries. Opening decompresses the file and makes one pass over it that only
/// records where every string and every entry starts; an entry (and the strings it uses) is decoded the first time it is asked for,
/// so a process that uses a few dozen of the ~270 keys of a locale pays for those. See <c>LocaleWriter</c> in
/// <c>tools/Phoney.DataCompiler</c> for the layout; keep both in sync and bump the format version on any layout change.
/// </summary>
internal sealed class LocaleResource
{
    private const byte FormatVersion = 1;

    /// <summary>The resource of a locale without embedded data (user registered ones).</summary>
    public static readonly LocaleResource Empty = new("", [], [], [], []);

    private readonly string _code;
    private readonly byte[] _data;
    private readonly int[] _stringStart; // where the length prefix of each string of the table starts
    private readonly int[] _entryStart; // by key: where the entry starts (its kind byte), 0 when the locale has no data for the key
    private readonly int[] _entryEnd; // by key: the first byte after the entry
    private readonly string?[] _strings; // decoded strings, shared by the entries that use them (a benign race may decode one twice)
    private readonly Entry?[] _entries; // decoded entries, published with Interlocked.CompareExchange so every caller gets one instance
    private int _decoded;

    private LocaleResource(string code, byte[] data, int[] stringStart, int[] entryStart, int[] entryEnd)
    {
        _code = code;
        _data = data;
        _stringStart = stringStart;
        _entryStart = entryStart;
        _entryEnd = entryEnd;
        _strings = new string?[stringStart.Length];
        _entries = new Entry?[entryStart.Length];
    }

    /// <summary>How many entries have been decoded so far.</summary>
    public int DecodedEntries => Volatile.Read(ref _decoded);

    /// <summary>Opens the embedded data of <paramref name="code"/>, or returns <see langword="null"/> when there is no such resource.</summary>
    public static LocaleResource? Open(string code)
    {
        using var stream = typeof(LocaleResource).Assembly.GetManifestResourceStream($"Phoney.Locales.{code}.bin.br");
        if (stream is null)
            return null;
        using var brotli = new BrotliStream(stream, CompressionMode.Decompress);
        using var buffer = new MemoryStream((int)Math.Min(stream.Length * 4, int.MaxValue));
        brotli.CopyTo(buffer);
        return Scan(code, buffer.GetBuffer(), (int)buffer.Length);
    }

    /// <summary>Whether the locale has an entry for the key; nothing is decoded to find out.</summary>
    public bool Has(int key) => (uint)key < (uint)_entryStart.Length && _entryStart[key] != 0;

    /// <summary>The entry for the key, decoded on first use; <see langword="null"/> when the locale has none.</summary>
    public Entry? Get(int key)
    {
        if (!Has(key))
            return null;
        return _entries[key] ?? Decode(key);
    }

    /// <summary>Decodes every entry; a file whose entries don't end where the first pass found them to end is rejected.</summary>
    public Entry?[] DecodeAll()
    {
        var all = new Entry?[_entryStart.Length];
        for (var key = 0; key < all.Length; key++)
            all[key] = Get(key);
        return all;
    }

    private Entry Decode(int key)
    {
        var position = _entryStart[key];
        var kind = (EntryKind)_data[position++];
        Entry entry = kind switch
        {
            EntryKind.Unavailable => UnavailableEntry.Instance,
            EntryKind.Strings => ReadStrings(ref position),
            EntryKind.Weighted => ReadWeighted(ref position),
            EntryKind.Ints => ReadInts(ref position),
            EntryKind.Records => ReadRecords(ref position),
            _ => throw new InvalidDataException($"Locale resource '{_code}' has unknown entry kind {kind}."),
        };
        if (position != _entryEnd[key])
            throw new InvalidDataException($"Locale resource '{_code}' is corrupt: the entry for key {key} ends at {position}, expected {_entryEnd[key]}.");

        var existing = Interlocked.CompareExchange(ref _entries[key], entry, null);
        if (existing is not null)
            return existing;
        Interlocked.Increment(ref _decoded);
        return entry;
    }

    private string ReadString(int index) => _strings[index] ??= DecodeString(index);

    private string DecodeString(int index)
    {
        var position = _stringStart[index];
        var length = ReadVarint(_data, ref position);
        return Encoding.UTF8.GetString(_data, position, length);
    }

    private StringsEntry ReadStrings(ref int position)
    {
        var values = new string[ReadVarint(_data, ref position)];
        for (var i = 0; i < values.Length; i++)
            values[i] = ReadString(ReadVarint(_data, ref position));
        return new StringsEntry(values);
    }

    private WeightedEntry ReadWeighted(ref int position)
    {
        var count = ReadVarint(_data, ref position);
        var values = new string[count];
        var weights = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadString(ReadVarint(_data, ref position));
            weights[i] = ReadVarint(_data, ref position);
        }

        return new WeightedEntry(values, weights);
    }

    private IntsEntry ReadInts(ref int position)
    {
        var values = new int[ReadVarint(_data, ref position)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToInt32(_data, position);
            position += 4;
        }

        return new IntsEntry(values);
    }

    private RecordsEntry ReadRecords(ref int position)
    {
        var fields = new string[ReadVarint(_data, ref position)];
        for (var i = 0; i < fields.Length; i++)
            fields[i] = ReadString(ReadVarint(_data, ref position));
        var rows = ReadVarint(_data, ref position);
        var values = new string?[rows * fields.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var index = ReadVarint(_data, ref position);
            values[i] = index == 0 ? null : ReadString(index - 1);
        }

        return new RecordsEntry(fields, values);
    }

    /// <summary>The single pass over the file: header, the start of every string, the start and end of every entry. Nothing is allocated per string or entry.</summary>
    private static LocaleResource Scan(string code, byte[] data, int length)
    {
        if (length < 5 || !data.AsSpan(0, 4).SequenceEqual("PHNY"u8))
            throw new InvalidDataException($"Locale resource '{code}' is not Phoney locale data.");
        var position = 4;
        var version = data[position++];
        if (version != FormatVersion)
            throw new InvalidDataException($"Locale resource '{code}' has format {version}; expected {FormatVersion}.");
        var versionLength = ReadVarint(data, ref position); // the faker.js version: a length prefix, then the text
        position += versionLength;
        var keyCount = ReadVarint(data, ref position);
        if (keyCount != DataKeys.Count)
            throw new InvalidDataException($"Locale resource '{code}' was compiled for {keyCount} keys but the catalog has {DataKeys.Count}. Re-run the data compiler.");

        var stringStart = new int[ReadVarint(data, ref position)];
        for (var i = 0; i < stringStart.Length; i++)
        {
            stringStart[i] = position;
            var stringLength = ReadVarint(data, ref position);
            position += stringLength;
        }

        var entryStart = new int[DataKeys.Count];
        var entryEnd = new int[DataKeys.Count];
        var entryCount = ReadVarint(data, ref position);
        for (var e = 0; e < entryCount; e++)
        {
            var key = ReadVarint(data, ref position);
            var start = position;
            var kind = (EntryKind)data[position++];
            // Empty lists and tables count as missing data (like the entries the eager reader returned as null).
            var present = kind switch
            {
                EntryKind.Unavailable => true,
                EntryKind.Strings => SkipVarints(data, ref position, 1) > 0,
                EntryKind.Weighted => SkipVarints(data, ref position, 2) > 0,
                EntryKind.Ints => SkipInts(data, ref position) > 0,
                EntryKind.Records => SkipRecords(data, ref position) > 0,
                _ => throw new InvalidDataException($"Locale resource '{code}' has unknown entry kind {kind}."),
            };
            entryStart[key] = present ? start : 0;
            entryEnd[key] = position;
        }

        if (position != length)
            throw new InvalidDataException($"Locale resource '{code}' is corrupt: {length - position} unexpected bytes after the last entry.");
        return new LocaleResource(code, data, stringStart, entryStart, entryEnd);
    }

    /// <summary>Skips a count and <c>count × perItem</c> varints; returns the count.</summary>
    private static int SkipVarints(byte[] data, ref int position, int perItem)
    {
        var count = ReadVarint(data, ref position);
        for (var i = 0; i < count * perItem; i++)
            SkipVarint(data, ref position);
        return count;
    }

    private static int SkipInts(byte[] data, ref int position)
    {
        var count = ReadVarint(data, ref position);
        position += count * 4;
        return count;
    }

    private static int SkipRecords(byte[] data, ref int position)
    {
        var fields = ReadVarint(data, ref position);
        for (var i = 0; i < fields; i++)
            SkipVarint(data, ref position);
        var rows = ReadVarint(data, ref position);
        for (var i = 0; i < rows * fields; i++)
            SkipVarint(data, ref position);
        return rows;
    }

    private static void SkipVarint(byte[] data, ref int position)
    {
        while ((data[position++] & 0x80) != 0)
        {
        }
    }

    /// <summary>A 7-bit encoded integer (what <c>BinaryWriter.Write7BitEncodedInt</c> writes).</summary>
    private static int ReadVarint(byte[] data, ref int position)
    {
        var result = 0;
        var shift = 0;
        byte b;
        do
        {
            b = data[position++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);
        return result;
    }
}
