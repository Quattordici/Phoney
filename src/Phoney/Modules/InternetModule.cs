using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Phoney.Data;
using Phoney.Templates;

namespace Phoney.Modules;

/// <summary>Emails, usernames, URLs, IP addresses and other internet values (faker.js <c>internet</c>).</summary>
public sealed class InternetModule : FakerModule
{
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH"];

    private static readonly int[] EmojiKeys =
    [
        DataKeys.InternetEmojiActivity, DataKeys.InternetEmojiBody, DataKeys.InternetEmojiFlag, DataKeys.InternetEmojiFood,
        DataKeys.InternetEmojiNature, DataKeys.InternetEmojiObject, DataKeys.InternetEmojiPerson, DataKeys.InternetEmojiSmiley,
        DataKeys.InternetEmojiSymbol, DataKeys.InternetEmojiTravel,
    ];

    internal InternetModule(Faker faker) : base(faker)
    {
    }

    /// <summary>
    /// Returns an email address at a free provider, e.g. <c>kassandra.haley@gmail.com</c>.
    /// Pass names to make it match a person generated elsewhere.
    /// </summary>
    /// <param name="firstName">First name to base the local part on.</param>
    /// <param name="lastName">Last name to base the local part on.</param>
    /// <param name="provider">Domain to use instead of a random free email provider.</param>
    /// <param name="allowSpecialCharacters">Whether the local part may contain RFC 5322 special characters such as <c>!#$%</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="provider"/> is empty or white space.</exception>
    public string Email(string? firstName = null, string? lastName = null, string? provider = null, bool allowSpecialCharacters = false)
    {
        if (provider is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        provider ??= Faker.Pick(DataKeys.InternetFreeEmail);
        var text = new Templates.TextBuilder(stackalloc char[96]);
        AppendRawUsername(firstName, lastName, ref text);
        if (!allowSpecialCharacters && Ascii.IsValid(text.Written))
        {
            // The common case, built in one buffer: the steps below on an ASCII username (dropping what isn't allowed covers ToAscii's quotes and spaces).
            var chars = text.Written;
            var email = string.Concat(chars[..AsciiLocalPart(chars)].Trim('.'), "@", provider);
            text.Dispose();
            return email;
        }

        var local = RemoveInvalidEmailChars(ToAscii(text.ToStringAndDispose()));
        if (local.Length > 50)
            local = local[..50];
        if (allowSpecialCharacters)
            local = WithSpecialCharacter(local);

        local = CollapseDots(local).Trim('.');
        return $"{local}@{provider}";
    }

    /// <summary>
    /// Makes an ASCII username a local part in place: drops the characters an address can't hold, keeps the first 50 and collapses runs of dots. Returns its length.
    /// </summary>
    private static int AsciiLocalPart(Span<char> chars)
    {
        var length = 0;
        foreach (var c in chars)
        {
            if (IsEmailLocalChar(c))
                chars[length++] = c;
        }

        length = Math.Min(length, 50);
        var collapsed = 0;
        for (var i = 0; i < length; i++)
        {
            if (chars[i] != '.' || collapsed == 0 || chars[collapsed - 1] != '.')
                chars[collapsed++] = chars[i];
        }

        return collapsed;
    }

    /// <summary>Replaces the first of a randomly chosen separator (<c>.</c>, <c>_</c> or <c>-</c>) with an RFC 5322 special character.</summary>
    private string WithSpecialCharacter(string local)
    {
        var from = "._-"[Random.Index(3)];
        const string special = ".!#$%&'*+-/=?^_`{|}~";
        var index = local.IndexOf(from);
        return index < 0 ? local : string.Concat(local.AsSpan(0, index), special[Random.Index(special.Length)].ToString(), local.AsSpan(index + 1));
    }

    /// <summary>Returns an email at a reserved example domain (<c>example.com</c>…), safe to use in tests and demos.</summary>
    /// <inheritdoc cref="Email" path="/param[@name='firstName']"/>
    /// <inheritdoc cref="Email" path="/param[@name='lastName']"/>
    /// <inheritdoc cref="Email" path="/param[@name='allowSpecialCharacters']"/>
    public string ExampleEmail(string? firstName = null, string? lastName = null, bool allowSpecialCharacters = false) =>
        Email(firstName, lastName, Faker.Pick(DataKeys.InternetExampleEmail), allowSpecialCharacters);

    /// <summary>
    /// Returns an ASCII username such as <c>Nettie_Zboncak40</c>. Non-Latin names are transliterated
    /// (e.g. Cyrillic) and diacritics removed.
    /// </summary>
    public string Username(string? firstName = null, string? lastName = null)
    {
        var text = new Templates.TextBuilder(stackalloc char[96]);
        AppendRawUsername(firstName, lastName, ref text);
        return ToAscii(text.ToStringAndDispose());
    }

    /// <summary>Writes the username before <see cref="ToAscii"/>; draws the names that are not given, the separator, the number and the shape, in that order.</summary>
    private void AppendRawUsername(string? firstName, string? lastName, ref Templates.TextBuilder text)
    {
        var hasLastName = lastName is not null;
        firstName ??= Faker.Person.FirstName();
        lastName ??= Faker.Person.LastName();
        var separator = Random.Bool() ? '.' : '_';
        var disambiguator = Random.Int(0, 99);

        // faker.js offers "first<sep>last99", "first<sep>last" and, without an explicit last name, "first99".
        var shape = Random.Index(hasLastName ? 2 : 3);
        text.Append(firstName);
        if (shape < 2)
        {
            text.Append(separator);
            text.Append(lastName);
        }

        if (shape != 1)
        {
            if (disambiguator >= 10)
                text.Append((char)('0' + (disambiguator / 10)));
            text.Append((char)('0' + (disambiguator % 10)));
        }
    }

    /// <summary>Returns a display name, e.g. <c>Nettie_Zboncak40</c>; unlike <see cref="Username"/> it keeps non-ASCII letters.</summary>
    public string DisplayName(string? firstName = null, string? lastName = null)
    {
        firstName ??= Faker.Person.FirstName();
        lastName ??= Faker.Person.LastName();
        var separator = Random.Bool() ? '.' : '_';
        var disambiguator = Random.Int(0, 99).ToString(CultureInfo.InvariantCulture);
        var result = Random.Index(3) switch
        {
            0 => $"{firstName}{disambiguator}",
            1 => $"{firstName}{separator}{lastName}",
            _ => $"{firstName}{separator}{lastName}{disambiguator}",
        };
        return result.Replace("'", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
    }

    /// <summary>Returns <c>http</c> or <c>https</c>.</summary>
    public string Protocol() => Random.Bool() ? "http" : "https";

    /// <summary>Returns an HTTP method: GET, POST, PUT, DELETE or PATCH.</summary>
    public string HttpMethod() => Random.Element(HttpMethods);

    /// <summary>Returns an HTTP status code, optionally restricted to some classes.</summary>
    public int HttpStatusCode(params HttpStatusCodeType[] types)
    {
        var type = types is { Length: > 0 } ? Random.Element(types) : Random.Enum<HttpStatusCodeType>();
        var key = type switch
        {
            HttpStatusCodeType.Informational => DataKeys.InternetHttpStatusCodeInformational,
            HttpStatusCodeType.Success => DataKeys.InternetHttpStatusCodeSuccess,
            HttpStatusCodeType.Redirection => DataKeys.InternetHttpStatusCodeRedirection,
            HttpStatusCodeType.ClientError => DataKeys.InternetHttpStatusCodeClientError,
            _ => DataKeys.InternetHttpStatusCodeServerError,
        };
        return Random.Element(Faker.Data.Ints(key).Values);
    }

    /// <summary>Returns a URL such as <c>https://dependable-lamp.com/</c>.</summary>
    /// <param name="appendSlash">Whether to end with <c>/</c>; random when <see langword="null"/>.</param>
    /// <param name="protocol">URL scheme, without <c>://</c>: <c>https</c>, <c>ftp</c>, <c>git+ssh</c>…</param>
    /// <exception cref="ArgumentNullException"><paramref name="protocol"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="protocol"/> is not a URL scheme (a letter, then letters, digits, <c>+</c>, <c>-</c> or <c>.</c>).</exception>
    public string Url(bool? appendSlash = null, string protocol = "https")
    {
        ArgumentNullException.ThrowIfNull(protocol);
        if (!Uri.CheckSchemeName(protocol))
            throw new ArgumentException($"'{protocol}' is not a URL scheme: pass one such as \"https\" or \"ftp\", without \"://\".", nameof(protocol));
        var text = new TextBuilder(stackalloc char[128]);
        text.Append(protocol);
        text.Append("://");
        AppendDomainName(ref text);
        if (appendSlash ?? Random.Bool())
            text.Append('/');
        return text.ToStringAndDispose();
    }

    /// <summary>Returns a domain name, e.g. <c>slow-timer.info</c>.</summary>
    public string DomainName()
    {
        var text = new TextBuilder(stackalloc char[128]);
        AppendDomainName(ref text);
        return text.ToStringAndDispose();
    }

    /// <summary>Returns a top-level domain suffix, e.g. <c>com</c>.</summary>
    public string DomainSuffix() => Faker.Pick(DataKeys.InternetDomainSuffix);

    /// <summary>Returns the first part of a domain name, e.g. <c>close-reality</c>, built from an adjective and a noun.</summary>
    public string DomainWord()
    {
        var text = new TextBuilder(stackalloc char[128]);
        AppendDomainWord(ref text);
        return text.ToStringAndDispose();
    }

    /// <summary>Appends <see cref="DomainWord"/>, a dot and <see cref="DomainSuffix"/>.</summary>
    private void AppendDomainName(ref TextBuilder text)
    {
        AppendDomainWord(ref text);
        text.Append('.');
        text.Append(DomainSuffix());
    }

    /// <summary>Appends an adjective and a noun as domain words, lower-cased (both are ASCII, see <see cref="ValidDomainWord"/>).</summary>
    private void AppendDomainWord(ref TextBuilder text)
    {
        var start = text.Length;
        text.Append(ValidDomainWord(Faker.Word.Adjective()));
        text.Append('-');
        text.Append(ValidDomainWord(Faker.Word.Noun()));
        Ascii.ToLowerInPlace(text.Written[start..], out _);
    }

    /// <summary>Returns an IPv4 or IPv6 address.</summary>
    public string Ip() => Random.Bool() ? Ipv4() : Ipv6();

    /// <summary>Returns an IPv4 address inside <paramref name="network"/>, e.g. <c>192.168.1.42</c> for <see cref="IPv4Network.PrivateC"/>.</summary>
    public string Ipv4(IPv4Network network = IPv4Network.Any) => network switch
    {
        IPv4Network.Loopback => Ipv4(0x7F00_0000, 8), // 127.0.0.0/8
        IPv4Network.PrivateA => Ipv4(0x0A00_0000, 8), // 10.0.0.0/8
        IPv4Network.PrivateB => Ipv4(0xAC10_0000, 12), // 172.16.0.0/12
        IPv4Network.PrivateC => Ipv4(0xC0A8_0000, 16), // 192.168.0.0/16
        IPv4Network.TestNet1 => Ipv4(0xC000_0200, 24), // 192.0.2.0/24
        IPv4Network.TestNet2 => Ipv4(0xC633_6400, 24), // 198.51.100.0/24
        IPv4Network.TestNet3 => Ipv4(0xCB00_7100, 24), // 203.0.113.0/24
        IPv4Network.LinkLocal => Ipv4(0xA9FE_0000, 16), // 169.254.0.0/16
        IPv4Network.Multicast => Ipv4(0xE000_0000, 4), // 224.0.0.0/4
        _ => Ipv4(0, 0),
    };

    /// <summary>Returns an IPv4 address inside a CIDR block such as <c>10.1.0.0/16</c>.</summary>
    public string Ipv4(string cidrBlock)
    {
        ArgumentNullException.ThrowIfNull(cidrBlock);
        Span<int> octets = stackalloc int[4];
        if (!TryParseCidr(cidrBlock, octets, out var prefix))
            throw new ArgumentException($"Invalid CIDR block '{cidrBlock}'. Use the format x.x.x.x/y.", nameof(cidrBlock));
        if (prefix > 32)
            throw new ArgumentException($"Invalid CIDR block '{cidrBlock}': the prefix length must be 0-32.", nameof(cidrBlock));

        uint ip = 0;
        foreach (var octet in octets)
        {
            if (octet > 255)
                throw new ArgumentException($"Invalid CIDR block '{cidrBlock}': octets must be 0-255.", nameof(cidrBlock));
            ip = (ip << 8) | (uint)octet;
        }

        return Ipv4(ip, prefix);
    }

    /// <summary>A random address in <paramref name="network"/>/<paramref name="prefix"/>: one draw for the host part, written in one allocation.</summary>
    private string Ipv4(uint network, int prefix)
    {
        var hostMask = prefix switch
        {
            0 => uint.MaxValue,
            32 => 0u,
            _ => uint.MaxValue >> prefix,
        };
        var address = (network & ~hostMask) | (uint)Random.Long(0, hostMask);
        Span<char> text = stackalloc char[15];
        var length = 0;
        for (var shift = 24; shift >= 0; shift -= 8)
        {
            if (shift < 24)
                text[length++] = '.';
            ((address >> shift) & 0xFF).TryFormat(text[length..], out var written, provider: CultureInfo.InvariantCulture);
            length += written;
        }

        return new string(text[..length]);
    }

    /// <summary>
    /// Reads <c>a.b.c.d/prefix</c>: four numbers of one to three ASCII digits and a prefix of one or two. The ranges are checked by the caller,
    /// so the message can say which part is wrong.
    /// </summary>
    private static bool TryParseCidr(ReadOnlySpan<char> text, Span<int> octets, out int prefix)
    {
        prefix = 0;
        for (var part = 0; part < 4; part++)
        {
            if (!TryReadNumber(ref text, 3, out octets[part]) || text.IsEmpty || text[0] != (part < 3 ? '.' : '/'))
                return false;
            text = text[1..];
        }

        return TryReadNumber(ref text, 2, out prefix) && text.IsEmpty;
    }

    /// <summary>Reads one to <paramref name="maxDigits"/> ASCII digits from the start of <paramref name="text"/> and moves past them.</summary>
    private static bool TryReadNumber(ref ReadOnlySpan<char> text, int maxDigits, out int value)
    {
        var digits = 0;
        value = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            value = (value * 10) + (text[digits++] - '0');
        text = text[digits..];
        return digits > 0 && digits <= maxDigits;
    }

    /// <summary>Returns a full (uncompressed) IPv6 address, e.g. <c>269f:1230:73e3:318d:842b:daab:326d:897b</c>.</summary>
    public string Ipv6() =>
        string.Create(39, Random, static (text, random) =>
        {
            // Eight groups of four lower-case hexadecimal digits, drawn like String.Hexadecimal(4, Casing.Lower) each.
            for (var group = 0; group < 8; group++)
            {
                var start = group * 5;
                StringModule.Fill(text.Slice(start, 4), StringModule.HexLower, random);
                if (group < 7)
                    text[start + 4] = ':';
            }
        });

    /// <summary>Returns a port number between 1 and 65535.</summary>
    public int Port() => Random.Int(1, 65535);

    /// <summary>Returns a realistic browser user agent string.</summary>
    public string UserAgent() => Faker.Pick(DataKeys.InternetUserAgentPattern);

    /// <summary>Returns a MAC address, e.g. <c>32:8e:2e:09:c6:05</c>; <paramref name="separator"/> may be <c>:</c>, <c>-</c> or empty.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="separator"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="separator"/> is another text.</exception>
    public string Mac(string separator = ":")
    {
        ArgumentNullException.ThrowIfNull(separator);
        if (separator is not (":" or "-" or ""))
            throw new ArgumentException($"A MAC address separator is \":\", \"-\" or \"\", not \"{separator}\".", nameof(separator));
        var sb = new StringBuilder(17);
        for (var i = 0; i < 12; i++)
        {
            sb.Append("0123456789abcdef"[Random.Index(16)]);
            if (i % 2 == 1 && i != 11)
                sb.Append(separator);
        }

        return sb.ToString();
    }

    /// <summary>The number of characters in a row that may miss the pattern of a password before it is given up.</summary>
    private const int MaxPasswordMisses = 10_000;

    /// <summary>Returns a password.</summary>
    /// <param name="length">Total length including <paramref name="prefix"/>; a prefix already this long or longer is returned unchanged.</param>
    /// <param name="memorable">Alternate consonants and vowels in lower case so the password is pronounceable.</param>
    /// <param name="pattern">Regular expression each generated ASCII character (codes 33–126) must match; defaults to word characters (<c>\w</c>). Ignored when <paramref name="memorable"/> is <see langword="true"/>.</param>
    /// <param name="prefix">Text the password starts with; it is not checked against <paramref name="pattern"/>.</param>
    /// <exception cref="ArgumentException">10,000 consecutive candidate characters are rejected before the requested length is reached.</exception>
    /// <exception cref="RegexMatchTimeoutException">Matching a character against <paramref name="pattern"/> exceeds the expression's timeout.</exception>
    public string Password(int length = 15, bool memorable = false, Regex? pattern = null, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var sb = new StringBuilder(prefix, Math.Max(length, prefix.Length));
        Span<char> one = stackalloc char[1];
        var misses = 0;
        while (sb.Length < length)
        {
            // A pattern that one printable character in 94 matches has less than a 1 in 10^40 chance of 10 000 misses in a row; one that none matches would never end.
            if (misses >= MaxPasswordMisses)
                throw new ArgumentException("The pattern matches no printable ASCII character (33–126), so a password can't be made from it.", nameof(pattern));
            var c = (char)Random.Int(33, 126);
            bool accept;
            if (memorable)
            {
                c = char.ToLowerInvariant(c);
                var wantVowel = sb.Length > 0 && IsConsonant(sb[^1]);
                accept = wantVowel ? IsVowel(c) : IsConsonant(c);
            }
            else if (pattern is null)
            {
                accept = char.IsAsciiLetterOrDigit(c) || c == '_';
            }
            else
            {
                one[0] = c;
                accept = pattern.IsMatch(one);
            }

            if (accept)
            {
                sb.Append(c);
                misses = 0;
            }
            else
            {
                misses++;
            }
        }

        return sb.ToString();

        static bool IsVowel(char c) => "aeiouAEIOU".Contains(c);
        static bool IsConsonant(char c) => char.IsAsciiLetter(c) && !IsVowel(c);
    }

    /// <summary>Returns a hex color such as <c>#a1b2c3</c>; each channel is the average of a random value and the base, so a base of 255 gives pastel colors.</summary>
    /// <param name="redBase">Red base, 0–255.</param>
    /// <param name="greenBase">Green base, 0–255.</param>
    /// <param name="blueBase">Blue base, 0–255.</param>
    /// <exception cref="ArgumentOutOfRangeException">A base is outside 0–255.</exception>
    public string Color(int redBase = 0, int greenBase = 0, int blueBase = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(redBase);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(redBase, 255);
        ArgumentOutOfRangeException.ThrowIfNegative(greenBase);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(greenBase, 255);
        ArgumentOutOfRangeException.ThrowIfNegative(blueBase);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blueBase, 255);
        var red = (Random.Int(0, 255) + redBase) / 2;
        var green = (Random.Int(0, 255) + greenBase) / 2;
        var blue = (Random.Int(0, 255) + blueBase) / 2;
        return string.Create(CultureInfo.InvariantCulture, $"#{red:x2}{green:x2}{blue:x2}");
    }

    /// <summary>Returns an emoji, optionally from specific categories.</summary>
    public string Emoji(params EmojiType[] types)
    {
        var key = types is { Length: > 0 } ? EmojiKeys[(int)Random.Element(types)] : Random.Element(EmojiKeys);
        return Faker.Pick(key);
    }

    /// <summary>Returns a JWT signing algorithm, e.g. <c>HS256</c>.</summary>
    public string JwtAlgorithm() => Faker.Pick(DataKeys.InternetJwtAlgorithm);

    /// <summary>
    /// Returns a structurally valid (but not validly signed) JSON Web Token with realistic header and claims
    /// (<c>iat</c>, <c>exp</c>, <c>nbf</c>, <c>iss</c>, <c>sub</c>, <c>aud</c>, <c>jti</c>).
    /// </summary>
    public string Jwt()
    {
        var issuedAt = Faker.Date.Recent();
        var json = _jwtJson ??= new ArrayBufferWriter<byte>(512);
        var writer = _jwtWriter ??= new Utf8JsonWriter(json);
        var text = new TextBuilder(stackalloc char[512]);

        StartJson(json, writer);
        writer.WriteString("alg", JwtAlgorithm());
        writer.WriteString("typ", "JWT");
        AppendBase64Url(json, writer, ref text);
        text.Append('.');

        StartJson(json, writer);
        writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
        writer.WriteNumber("exp", Faker.Date.Soon(referenceDate: issuedAt).ToUnixTimeSeconds());
        writer.WriteNumber("nbf", Faker.Date.Anytime().ToUnixTimeSeconds());
        writer.WriteString("iss", Faker.Company.Name());
        writer.WriteString("sub", Faker.String.Uuid());
        writer.WriteString("aud", Faker.String.Uuid());
        writer.WriteString("jti", Faker.String.Uuid());
        AppendBase64Url(json, writer, ref text);
        text.Append('.');

        StringModule.Fill(text.AppendSpan(64), StringModule.DigitsAndLettersFor(Casing.Mixed), Random);
        return text.ToStringAndDispose();
    }

    // The JSON of a token's header and claims is written into one buffer and writer per faker (a faker is used by one thread at a time).
    private ArrayBufferWriter<byte>? _jwtJson;
    private Utf8JsonWriter? _jwtWriter;

    /// <summary>Starts a JSON object in the emptied <paramref name="json"/> buffer.</summary>
    private static void StartJson(ArrayBufferWriter<byte> json, Utf8JsonWriter writer)
    {
        json.ResetWrittenCount();
        writer.Reset(json);
        writer.WriteStartObject();
    }

    /// <summary>Ends the JSON object and appends it in unpadded base64url (RFC 7515).</summary>
    private static void AppendBase64Url(ArrayBufferWriter<byte> json, Utf8JsonWriter writer, ref TextBuilder text)
    {
        writer.WriteEndObject();
        writer.Flush();
        var bytes = json.WrittenSpan;
        var start = text.Length;
        var chars = text.AppendSpan(((bytes.Length + 2) / 3) * 4);
        Convert.TryToBase64Chars(bytes, chars, out var written);
        var length = written;
        while (length > 0 && chars[length - 1] == '=')
            length--;
        chars[..length].Replace('+', '-');
        chars[..length].Replace('/', '_');
        text.Length = start + length;
    }

    /// <summary>Removes diacritics, transliterates non-Latin letters and encodes anything else non-ASCII in base 36, like faker.js.</summary>
    private static string ToAscii(string text)
    {
        // Plain ASCII needs no normalisation or transliteration (no letter of the table is ASCII); this is nearly every name of an English locale.
        if (Ascii.IsValid(text))
            return text.Replace("'", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);

        var decomposed = Decomposition.FormKD(text);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark && rune.Value is >= 0x300 and <= 0x36F)
                continue;
            if (rune.IsBmp && CharMappings.TryMap((char)rune.Value, out var mapped))
                sb.Append(mapped);
            else if (rune.Value < 0x80)
                sb.Append((char)rune.Value);
            else
                sb.Append(ToBase36(rune.Value));
        }

        return sb.Replace("'", "").Replace(" ", "").ToString();
    }

    /// <summary>Base-36 representation, used by faker.js for characters it cannot transliterate.</summary>
    private static string ToBase36(int value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[8];
        var i = buffer.Length;
        do
        {
            buffer[--i] = digits[value % 36];
            value /= 36;
        }
        while (value > 0);
        return new string(buffer[i..]);
    }

    /// <summary>Slugifies a word for use in a domain; falls back to a lorem word, then to random letters (faker.js behaviour).</summary>
    private string ValidDomainWord(string word)
    {
        var slug = HelpersModule.Slugify(word);
        if (IsDomainSlug(slug))
            return slug;
        slug = HelpersModule.Slugify(Faker.Lorem.Word());
        return IsDomainSlug(slug) ? slug : Faker.String.Alpha(Random.Int(4, 8), Casing.Lower);
    }

    /// <summary>
    /// Removes the characters that are not allowed in generated email local parts (everything but <c>A-Z a-z 0-9 . _ + -</c>). Written as a loop
    /// because the first use of a <see cref="Regex"/> in a process costs more than the whole first email.
    /// </summary>
    internal static string RemoveInvalidEmailChars(string text)
    {
        var kept = 0;
#pragma warning disable S3267 // runs for every email; Count(...) would allocate an enumerator, and LINQ adds JIT to the first call (see the summary)
        foreach (var c in text)
        {
            if (IsEmailLocalChar(c))
                kept++;
        }
#pragma warning restore S3267

        if (kept == text.Length)
            return text;
        return string.Create(kept, text, static (span, source) =>
        {
            var next = 0;
            foreach (var c in source)
            {
                if (IsEmailLocalChar(c))
                    span[next++] = c;
            }
        });
    }

    private static bool IsEmailLocalChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '+' or '-';

    /// <summary>Replaces every run of two or more dots with one dot.</summary>
    internal static string CollapseDots(string text)
    {
        if (!text.Contains("..", StringComparison.Ordinal))
            return text;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c != '.' || sb.Length == 0 || sb[^1] != '.')
                sb.Append(c);
        }

        return sb.ToString();
    }


    /// <summary>
    /// Whether <paramref name="slug"/> is usable as a domain label: ASCII letters with inner dashes (<c>^[a-z][a-z-]*[a-z]$</c>, ignoring case). A loop: this runs for
    /// every URL, email domain and company website. <see cref="HelpersModule.Slugify"/> has already decomposed compatibility characters (the Kelvin sign is a K by then).
    /// </summary>
    internal static bool IsDomainSlug(ReadOnlySpan<char> slug)
    {
        if (slug.Length < 2 || !char.IsAsciiLetter(slug[0]) || !char.IsAsciiLetter(slug[^1]))
            return false;
        foreach (var c in slug[1..^1])
        {
            if (!char.IsAsciiLetter(c) && c != '-')
                return false;
        }

        return true;
    }
}

/// <summary>Classes of HTTP status codes.</summary>
public enum HttpStatusCodeType
{
    /// <summary>1xx.</summary>
    Informational,

    /// <summary>2xx.</summary>
    Success,

    /// <summary>3xx.</summary>
    Redirection,

    /// <summary>4xx.</summary>
    ClientError,

    /// <summary>5xx.</summary>
    ServerError,
}

/// <summary>Well-known IPv4 networks.</summary>
public enum IPv4Network
{
    /// <summary>Any address (0.0.0.0/0).</summary>
    Any,

    /// <summary>127.0.0.0/8.</summary>
    Loopback,

    /// <summary>10.0.0.0/8.</summary>
    PrivateA,

    /// <summary>172.16.0.0/12.</summary>
    PrivateB,

    /// <summary>192.168.0.0/16.</summary>
    PrivateC,

    /// <summary>192.0.2.0/24 (documentation).</summary>
    TestNet1,

    /// <summary>198.51.100.0/24 (documentation).</summary>
    TestNet2,

    /// <summary>203.0.113.0/24 (documentation).</summary>
    TestNet3,

    /// <summary>169.254.0.0/16.</summary>
    LinkLocal,

    /// <summary>224.0.0.0/4.</summary>
    Multicast,
}

/// <summary>Emoji categories. Values index the emoji data keys; keep in alphabetical order.</summary>
public enum EmojiType
{
    /// <summary>Activities and sports.</summary>
    Activity,

    /// <summary>Body parts and gestures.</summary>
    Body,

    /// <summary>Flags.</summary>
    Flag,

    /// <summary>Food and drink.</summary>
    Food,

    /// <summary>Animals and nature.</summary>
    Nature,

    /// <summary>Objects.</summary>
    Object,

    /// <summary>People.</summary>
    Person,

    /// <summary>Smileys and emotions.</summary>
    Smiley,

    /// <summary>Symbols.</summary>
    Symbol,

    /// <summary>Travel and places.</summary>
    Travel,
}
