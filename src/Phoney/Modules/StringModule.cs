namespace Phoney.Modules;

/// <summary>Random strings and identifiers (faker.js <c>string</c>).</summary>
public sealed class StringModule : FakerModule
{
    private const string Digits = "0123456789";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string HexMixed = "0123456789abcdefABCDEF";
    internal const string HexLower = "0123456789abcdefabcdef";
    private const string HexUpper = "0123456789ABCDEFABCDEF";
    private const string Symbols = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
    private const string CrockfordBase32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string NanoIdAlphabet = Digits + Upper + Lower + "_-";

    internal StringModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns <paramref name="length"/> characters picked from <paramref name="characters"/>.</summary>
    public string FromCharacters(string characters, int length = 1)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0)
            return "";
        if (characters.Length == 0)
            throw new ArgumentException("No characters to select from.", nameof(characters));
        return Pick(characters, length, "");
    }

    /// <summary><paramref name="prefix"/> followed by <paramref name="length"/> characters of <paramref name="characters"/> (not empty), in one allocation.</summary>
    private string Pick(string characters, int length, string prefix) =>
        string.Create(prefix.Length + length, (characters, prefix, Random), static (span, state) =>
        {
            state.prefix.CopyTo(span);
            Fill(span[state.prefix.Length..], state.characters, state.Random);
        });

    /// <summary>Fills <paramref name="destination"/> with characters of <paramref name="characters"/> (not empty), one draw each, in order.</summary>
    internal static void Fill(Span<char> destination, string characters, SeededRandom random)
    {
        for (var i = 0; i < destination.Length; i++)
            destination[i] = characters[random.Index(characters.Length)];
    }

    /// <summary>Returns letters only.</summary>
    /// <param name="length">Number of characters.</param>
    /// <param name="casing">Letter casing.</param>
    /// <param name="exclude">Characters that must not appear.</param>
    public string Alpha(int length = 1, Casing casing = Casing.Mixed, string? exclude = null) =>
        FromCharacters(Exclude(LettersFor(casing), exclude), length);

    /// <summary>Returns letters and digits.</summary>
    /// <param name="length">Number of characters.</param>
    /// <param name="casing">Letter casing.</param>
    /// <param name="exclude">Characters that must not appear.</param>
    public string AlphaNumeric(int length = 1, Casing casing = Casing.Mixed, string? exclude = null) =>
        FromCharacters(Exclude(DigitsAndLettersFor(casing), exclude), length);

    /// <summary>Returns a string of digits.</summary>
    /// <param name="length">Number of digits.</param>
    /// <param name="allowLeadingZeros">Whether the first digit may be <c>0</c>.</param>
    /// <param name="exclude">Digits that must not appear.</param>
    public string Numeric(int length = 1, bool allowLeadingZeros = true, string? exclude = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0)
            return "";
        var allowed = Exclude(Digits, exclude);
        if (allowed.Length == 0 || (allowed == "0" && !allowLeadingZeros))
            throw new ArgumentException("All possible digits are excluded.", nameof(exclude));
        if (allowLeadingZeros || !allowed.Contains('0'))
            return Pick(allowed, length, "");
        var first = Exclude(allowed, "0");
        return string.Create(length, (first, allowed, Random), static (span, state) =>
        {
            span[0] = state.first[state.Random.Index(state.first.Length)];
            Fill(span[1..], state.allowed, state.Random);
        });
    }

    /// <summary>Returns <c>0b</c> followed by <paramref name="length"/> binary digits.</summary>
    public string Binary(int length = 1, string prefix = "0b") => Prefixed("01", length, prefix);

    /// <summary>Returns <c>0o</c> followed by <paramref name="length"/> octal digits.</summary>
    public string Octal(int length = 1, string prefix = "0o") => Prefixed("01234567", length, prefix);

    /// <summary>Returns <c>0x</c> followed by <paramref name="length"/> hexadecimal digits.</summary>
    public string Hexadecimal(int length = 1, Casing casing = Casing.Mixed, string prefix = "0x")
    {
        // The cased alphabets keep the 22 positions of the mixed one, so a casing changes the letters' case, never the draws.
        return Prefixed(casing switch { Casing.Upper => HexUpper, Casing.Lower => HexLower, _ => HexMixed }, length, prefix);
    }

    /// <summary><paramref name="prefix"/> (none when <see langword="null"/>) and <paramref name="length"/> characters of <paramref name="characters"/>.</summary>
    private string Prefixed(string characters, int length, string? prefix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return Pick(characters, length, prefix ?? "");
    }

    /// <summary>Returns printable ASCII characters (codes 33–125).</summary>
    public string Sample(int length = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return string.Create(length, Random, static (span, random) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = (char)random.Int(33, 125);
        });
    }

    /// <summary>Returns ASCII punctuation symbols.</summary>
    public string Symbol(int length = 1) => FromCharacters(Symbols, length);

    /// <summary>Returns a random version 4 UUID string, e.g. <c>4136cd0b-d90b-4af7-b485-5d1ded8db252</c>.</summary>
    public string Uuid() => Random.Guid().ToString();

    /// <summary>Returns a version 7 (time-ordered) UUID string for <paramref name="timestamp"/> (default: the faker's reference date).</summary>
    public string UuidV7(DateTimeOffset? timestamp = null)
    {
        var ms = Math.Max(0, (timestamp ?? Faker.ReferenceDate).ToUnixTimeMilliseconds());
        var hex = (ms & 0xFFFF_FFFF_FFFF).ToString("x12", System.Globalization.CultureInfo.InvariantCulture);
        var random = Uuid();
        // xxxxxxxx-xxxx-7xxx-yxxx-xxxxxxxxxxxx: time in the first 48 bits, version 7, random rest.
        return $"{hex[..8]}-{hex[8..]}-7{random.AsSpan(15, 3)}-{random.AsSpan(19)}";
    }

    /// <summary>Returns a ULID for <paramref name="timestamp"/> (default: the faker's reference date).</summary>
    public string Ulid(DateTimeOffset? timestamp = null)
    {
        var ms = (timestamp ?? Faker.ReferenceDate).ToUnixTimeMilliseconds();
        if (ms is < 0 or > (1L << 48) - 1)
            throw new ArgumentOutOfRangeException(nameof(timestamp), "ULID timestamps must be between 1970 and 10889.");
        Span<char> time = stackalloc char[10];
        for (var i = 9; i >= 0; i--)
        {
            time[i] = CrockfordBase32[(int)(ms % 32)];
            ms /= 32;
        }

        return string.Concat(time, FromCharacters(CrockfordBase32, 16));
    }

    /// <summary>Returns a URL-friendly unique id of <paramref name="length"/> characters (nanoid alphabet).</summary>
    public string NanoId(int length = 21) => FromCharacters(NanoIdAlphabet, length);

    /// <summary>The letters allowed for a casing.</summary>
    private static string LettersFor(Casing casing) => casing switch
    {
        Casing.Upper => Upper,
        Casing.Lower => Lower,
        _ => Lower + Upper,
    };

    /// <summary>The digits followed by <see cref="LettersFor"/> (constants, so nothing is concatenated per call).</summary>
    internal static string DigitsAndLettersFor(Casing casing) => casing switch
    {
        Casing.Upper => Digits + Upper,
        Casing.Lower => Digits + Lower,
        _ => Digits + Lower + Upper,
    };

    // The last alphabets Exclude made: callers pass the same few constants over and over (a VIN excludes "oiqOIQ" from two alphabets).
    private readonly (string Characters, string Exclude, string Result)[] _excluded = new (string, string, string)[4];
    private int _nextExcluded;

    /// <summary><paramref name="characters"/> without those in <paramref name="exclude"/>, in order.</summary>
    internal string Exclude(string characters, string? exclude)
    {
        if (string.IsNullOrEmpty(exclude))
            return characters;
        foreach (var entry in _excluded)
        {
            if (ReferenceEquals(entry.Characters, characters) && string.Equals(entry.Exclude, exclude, StringComparison.Ordinal))
                return entry.Result;
        }

        Span<char> kept = characters.Length <= 256 ? stackalloc char[characters.Length] : new char[characters.Length];
        var count = 0;
        foreach (var c in characters)
        {
            if (!exclude.Contains(c))
                kept[count++] = c;
        }

        var result = new string(kept[..count]);
        _excluded[_nextExcluded] = (characters, exclude, result);
        _nextExcluded = (_nextExcluded + 1) % _excluded.Length;
        return result;
    }
}
