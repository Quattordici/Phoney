using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Phony.Data;

namespace Phony.Modules;

/// <summary>Emails, usernames, URLs, IP addresses and other internet values (faker.js <c>internet</c>).</summary>
public sealed partial class InternetModule : FakerModule
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
    public string Email(string? firstName = null, string? lastName = null, string? provider = null, bool allowSpecialCharacters = false)
    {
        provider ??= Faker.Pick(DataKeys.InternetFreeEmail);
        var local = InvalidEmailChars().Replace(Username(firstName, lastName), "");
        if (local.Length > 50)
            local = local[..50];
        if (allowSpecialCharacters)
        {
            var from = "._-"[Random.Index(3)];
            const string special = ".!#$%&'*+-/=?^_`{|}~";
            var index = local.IndexOf(from);
            if (index >= 0)
                local = string.Concat(local.AsSpan(0, index), special[Random.Index(special.Length)].ToString(), local.AsSpan(index + 1));
        }

        local = RepeatedDots().Replace(local, ".").Trim('.');
        return $"{local}@{provider}";
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
        var hasLastName = lastName is not null;
        firstName ??= Faker.Person.FirstName();
        lastName ??= Faker.Person.LastName();
        var separator = Random.Bool() ? '.' : '_';
        var disambiguator = Random.Int(0, 99).ToString(CultureInfo.InvariantCulture);

        // faker.js offers "first<sep>last99", "first<sep>last" and, without an explicit last name, "first99".
        var raw = Random.Index(hasLastName ? 2 : 3) switch
        {
            0 => $"{firstName}{separator}{lastName}{disambiguator}",
            1 => $"{firstName}{separator}{lastName}",
            _ => $"{firstName}{disambiguator}",
        };
        return ToAscii(raw);
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
    /// <param name="protocol">URL scheme.</param>
    public string Url(bool? appendSlash = null, string protocol = "https") =>
        $"{protocol}://{DomainName()}{((appendSlash ?? Random.Bool()) ? "/" : "")}";

    /// <summary>Returns a domain name, e.g. <c>slow-timer.info</c>.</summary>
    public string DomainName() => $"{DomainWord()}.{DomainSuffix()}";

    /// <summary>Returns a top-level domain suffix, e.g. <c>com</c>.</summary>
    public string DomainSuffix() => Faker.Pick(DataKeys.InternetDomainSuffix);

    /// <summary>Returns the first part of a domain name, e.g. <c>close-reality</c>, built from an adjective and a noun.</summary>
    public string DomainWord() =>
        $"{ValidDomainWord(Faker.Word.Adjective())}-{ValidDomainWord(Faker.Word.Noun())}".ToLowerInvariant();

    /// <summary>Returns an IPv4 or IPv6 address.</summary>
    public string Ip() => Random.Bool() ? Ipv4() : Ipv6();

    /// <summary>Returns an IPv4 address inside <paramref name="network"/>, e.g. <c>192.168.1.42</c> for <see cref="IPv4Network.PrivateC"/>.</summary>
    public string Ipv4(IPv4Network network = IPv4Network.Any) => Ipv4(network switch
    {
        IPv4Network.Loopback => "127.0.0.0/8",
        IPv4Network.PrivateA => "10.0.0.0/8",
        IPv4Network.PrivateB => "172.16.0.0/12",
        IPv4Network.PrivateC => "192.168.0.0/16",
        IPv4Network.TestNet1 => "192.0.2.0/24",
        IPv4Network.TestNet2 => "198.51.100.0/24",
        IPv4Network.TestNet3 => "203.0.113.0/24",
        IPv4Network.LinkLocal => "169.254.0.0/16",
        IPv4Network.Multicast => "224.0.0.0/4",
        _ => "0.0.0.0/0",
    });

    /// <summary>Returns an IPv4 address inside a CIDR block such as <c>10.1.0.0/16</c>.</summary>
    public string Ipv4(string cidrBlock)
    {
        ArgumentNullException.ThrowIfNull(cidrBlock);
        var match = CidrBlock().Match(cidrBlock);
        if (!match.Success)
            throw new ArgumentException($"Invalid CIDR block '{cidrBlock}'. Use the format x.x.x.x/y.", nameof(cidrBlock));
        var prefix = int.Parse(match.Groups[5].ValueSpan, CultureInfo.InvariantCulture);
        if (prefix > 32)
            throw new ArgumentException($"Invalid CIDR block '{cidrBlock}': the prefix length must be 0-32.", nameof(cidrBlock));

        uint ip = 0;
        for (var i = 1; i <= 4; i++)
        {
            var octet = int.Parse(match.Groups[i].ValueSpan, CultureInfo.InvariantCulture);
            if (octet > 255)
                throw new ArgumentException($"Invalid CIDR block '{cidrBlock}': octets must be 0-255.", nameof(cidrBlock));
            ip = (ip << 8) | (uint)octet;
        }

        var hostMask = prefix == 0 ? uint.MaxValue : prefix == 32 ? 0u : uint.MaxValue >> prefix;
        var address = (ip & ~hostMask) | (uint)Random.Long(0, hostMask);
        return $"{address >> 24}.{(address >> 16) & 0xFF}.{(address >> 8) & 0xFF}.{address & 0xFF}";
    }

    /// <summary>Returns a full (uncompressed) IPv6 address, e.g. <c>269f:1230:73e3:318d:842b:daab:326d:897b</c>.</summary>
    public string Ipv6()
    {
        var parts = new string[8];
        for (var i = 0; i < 8; i++)
            parts[i] = Faker.String.Hexadecimal(4, Casing.Lower, prefix: "");
        return string.Join(':', parts);
    }

    /// <summary>Returns a port number between 1 and 65535.</summary>
    public int Port() => Random.Int(1, 65535);

    /// <summary>Returns a realistic browser user agent string.</summary>
    public string UserAgent() => Faker.Pick(DataKeys.InternetUserAgentPattern);

    /// <summary>Returns a MAC address, e.g. <c>32:8e:2e:09:c6:05</c>; <paramref name="separator"/> may be <c>:</c>, <c>-</c> or empty.</summary>
    public string Mac(string separator = ":")
    {
        if (separator is not (":" or "-" or ""))
            separator = ":";
        var sb = new StringBuilder(17);
        for (var i = 0; i < 12; i++)
        {
            sb.Append("0123456789abcdef"[Random.Index(16)]);
            if (i % 2 == 1 && i != 11)
                sb.Append(separator);
        }

        return sb.ToString();
    }

    /// <summary>Returns a password.</summary>
    /// <param name="length">Total length including <paramref name="prefix"/>.</param>
    /// <param name="memorable">Alternate consonants and vowels in lower case so the password is pronounceable.</param>
    /// <param name="pattern">Regular expression each generated character must match; defaults to word characters (<c>\w</c>).</param>
    /// <param name="prefix">Text the password starts with.</param>
    public string Password(int length = 15, bool memorable = false, Regex? pattern = null, string prefix = "")
    {
        var sb = new StringBuilder(prefix, Math.Max(length, prefix.Length));
        Span<char> one = stackalloc char[1];
        while (sb.Length < length)
        {
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
                sb.Append(c);
        }

        return sb.ToString();

        static bool IsVowel(char c) => "aeiouAEIOU".Contains(c);
        static bool IsConsonant(char c) => char.IsAsciiLetter(c) && !IsVowel(c);
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
        var header = Base64Url(w =>
        {
            w.WriteString("alg", JwtAlgorithm());
            w.WriteString("typ", "JWT");
        });
        var payload = Base64Url(w =>
        {
            w.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            w.WriteNumber("exp", Faker.Date.Soon(referenceDate: issuedAt).ToUnixTimeSeconds());
            w.WriteNumber("nbf", Faker.Date.Anytime().ToUnixTimeSeconds());
            w.WriteString("iss", Faker.Company.Name());
            w.WriteString("sub", Faker.String.Uuid());
            w.WriteString("aud", Faker.String.Uuid());
            w.WriteString("jti", Faker.String.Uuid());
        });
        return $"{header}.{payload}.{Faker.String.AlphaNumeric(64)}";
    }

    /// <summary>Removes diacritics, transliterates non-Latin letters and encodes anything else non-ASCII in base 36, like faker.js.</summary>
    private static string ToAscii(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark && rune.Value is >= 0x300 and <= 0x36F)
                continue;
            if (rune.IsBmp && CharMappings.Map.TryGetValue((char)rune.Value, out var mapped))
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
        if (DomainSlug().IsMatch(slug))
            return slug;
        slug = HelpersModule.Slugify(Faker.Lorem.Word());
        return DomainSlug().IsMatch(slug) ? slug : Faker.String.Alpha(Random.Int(4, 8), Casing.Lower);
    }

    /// <summary>Writes a JSON object and encodes it as base64url without padding (JWT segment).</summary>
    private static string Base64Url(Action<Utf8JsonWriter> write)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return Convert.ToBase64String(buffer.WrittenSpan).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Characters not allowed in generated email local parts.</summary>
    [GeneratedRegex("[^A-Za-z0-9._+-]+")]
    private static partial Regex InvalidEmailChars();

    /// <summary>Two or more consecutive dots.</summary>
    [GeneratedRegex(@"\.{2,}")]
    private static partial Regex RepeatedDots();

    /// <summary><c>a.b.c.d/prefix</c>.</summary>
    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})/(\d{1,2})$")]
    private static partial Regex CidrBlock();

    /// <summary>A slug usable as a domain label: letters and inner dashes.</summary>
    [GeneratedRegex("^[a-z][a-z-]*[a-z]$", RegexOptions.IgnoreCase)]
    private static partial Regex DomainSlug();
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
