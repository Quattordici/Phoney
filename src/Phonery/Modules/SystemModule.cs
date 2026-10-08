using System.Globalization;
using System.Text;
using Phonery.Data;

namespace Phonery.Modules;

/// <summary>File names, MIME types, paths, versions and cron expressions (faker.js <c>system</c>).</summary>
public sealed class SystemModule : FakerModule
{
    private static readonly string[] CommonFileTypes = ["video", "audio", "image", "text", "application"];

    private static readonly string[] CommonMimeTypes =
        ["application/pdf", "audio/mpeg", "audio/wav", "image/png", "image/jpeg", "image/gif", "video/mp4", "video/mpeg", "text/html"];

    private static readonly string[] InterfaceTypes = ["en", "wl", "ww"];
    private static readonly string[] CronDaysOfWeek = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];
    private static readonly string[] NonStandardCron = ["@annually", "@daily", "@hourly", "@monthly", "@reboot", "@weekly", "@yearly"];

    internal SystemModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a file name with <paramref name="extensionCount"/> extensions, e.g. <c>self_assured_cake.pdf</c>.</summary>
    public string FileName(int extensionCount = 1)
    {
        var sb = new StringBuilder(BaseName());
        for (var i = 0; i < extensionCount; i++)
            sb.Append('.').Append(FileExt());
        return sb.ToString();
    }

    /// <summary>Returns a file name with a common extension (pdf, mp3, png…) or the given one.</summary>
    public string CommonFileName(string? extension = null) => $"{BaseName()}.{(string.IsNullOrEmpty(extension) ? CommonFileExt() : extension)}";

    /// <summary>Returns a MIME type, e.g. <c>image/png</c>.</summary>
    public string MimeType()
    {
        var (table, row) = Faker.PickRecord(DataKeys.SystemMimeType);
        return table.Get(row, 0)!;
    }

    /// <summary>Returns a common file type: video, audio, image, text or application.</summary>
    public string CommonFileType() => Random.Element(CommonFileTypes);

    /// <summary>Returns the extension of a common file type, e.g. <c>gif</c>.</summary>
    public string CommonFileExt() => FileExt(Random.Element(CommonMimeTypes));

    /// <summary>Returns a top-level file type from the MIME type list, e.g. <c>audio</c>.</summary>
    public string FileType()
    {
        var types = MimeTable().Types;
        return types[Random.Index(types.Length)];
    }

    /// <summary>Returns a file extension, optionally one valid for <paramref name="mimeType"/>.</summary>
    /// <exception cref="ArgumentException">The MIME type is unknown.</exception>
    public string FileExt(string? mimeType = null)
    {
        var mime = MimeTable();
        if (mimeType is null)
            return mime.AllExtensions[Random.Index(mime.AllExtensions.Length)];
        if (!mime.ExtensionsByType.TryGetValue(mimeType, out var extensions))
            throw new ArgumentException($"MIME type '{mimeType}' is not supported.", nameof(mimeType));
        return extensions[Random.Index(extensions.Length)];
    }

    /// <summary>Returns a directory path, e.g. <c>/etc/mail</c>.</summary>
    public string DirectoryPath() => Faker.Pick(DataKeys.SystemDirectoryPath);

    /// <summary>Returns a file path, e.g. <c>/usr/local/src/money.dotx</c>.</summary>
    public string FilePath() => $"{DirectoryPath()}/{FileName()}";

    /// <summary>Returns a semantic version, e.g. <c>1.15.2</c>.</summary>
    public string Semver() => string.Create(CultureInfo.InvariantCulture, $"{Random.Int(0, 9)}.{Random.Int(0, 20)}.{Random.Int(0, 20)}");

    /// <summary>Returns a predictable network interface name, e.g. <c>enp0s3</c> or <c>wlx2c6fc6b0a1a4</c>.</summary>
    public string NetworkInterface()
    {
        var type = Random.Element(InterfaceTypes);
        string Maybe(string letter) => Random.Bool() ? letter + Faker.String.Numeric() : "";
        return Random.Index(4) switch
        {
            0 => $"{type}o{Faker.String.Numeric()}",
            1 => $"{type}s{Faker.String.Numeric()}{Maybe("f")}{Maybe("d")}",
            2 => $"{type}x{Faker.Internet.Mac("")}",
            _ => $"{Maybe("P")}{type}p{Faker.String.Numeric()}s{Faker.String.Numeric()}{Maybe("f")}{Maybe("d")}",
        };
    }

    /// <summary>Returns a cron expression, e.g. <c>45 23 * * 6</c>.</summary>
    /// <param name="includeYear">Add a sixth (year) field.</param>
    /// <param name="includeNonStandard">Sometimes return macros like <c>@daily</c>.</param>
    public string Cron(bool includeYear = false, bool includeNonStandard = false)
    {
        string Field(string value, params string[] alternatives) => Random.Index(alternatives.Length + 1) == 0 ? value : alternatives[Random.Index(alternatives.Length)];
        var inv = CultureInfo.InvariantCulture;
        var minute = Random.Bool() ? Random.Int(0, 59).ToString(inv) : "*";
        var hour = Random.Bool() ? Random.Int(0, 23).ToString(inv) : "*";
        var day = Field(Random.Int(1, 31).ToString(inv), "*", "?");
        var month = Random.Bool() ? Random.Int(1, 12).ToString(inv) : "*";
        var dayOfWeek = Random.Index(4) switch
        {
            0 => Random.Int(0, 6).ToString(inv),
            1 => Random.Element(CronDaysOfWeek),
            2 => "*",
            _ => "?",
        };
        var expression = $"{minute} {hour} {day} {month} {dayOfWeek}";
        if (includeYear)
            expression += " " + (Random.Bool() ? Random.Int(1970, 2099).ToString(inv) : "*");
        return !includeNonStandard || Random.Bool() ? expression : Random.Element(NonStandardCron);
    }

    /// <summary>A file base name from random words, like faker.js.</summary>
    private string BaseName() => FileNameChars(Faker.Word.Words().ToLowerInvariant());

    /// <summary>Replaces non-word characters with underscores, like faker.js <c>/\W/g</c>.</summary>
    private static string FileNameChars(string text) => string.Create(text.Length, text, static (span, t) =>
    {
        for (var i = 0; i < span.Length; i++)
            span[i] = char.IsAsciiLetterOrDigit(t[i]) || t[i] == '_' ? t[i] : '_';
    });

    /// <summary>Indexes of the locale's MIME type table, built once per table and cached on it.</summary>
    private MimeIndex MimeTable()
    {
        var table = Faker.Data.Records(DataKeys.SystemMimeType);
        return MimeIndex.For(table);
    }

    /// <summary>Lookup tables over a locale's MIME type records, built once per table.</summary>
    private sealed class MimeIndex
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RecordsEntry, MimeIndex> Cache = new();

        private MimeIndex(RecordsEntry table)
        {
            var extensionsField = table.FieldIndex("extensions");
            var byType = new Dictionary<string, string[]>(StringComparer.Ordinal);
            for (var row = 0; row < table.Count; row++)
            {
                var extensions = table.Get(row, extensionsField)?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
                byType[table.Get(row, 0)!] = extensions;
            }

            ExtensionsByType = byType;
            AllExtensions = [.. byType.Values.SelectMany(e => e).Distinct()];
            Types = [.. byType.Keys.Select(k => k.Split('/')[0]).Distinct()];
        }

        /// <summary>Extensions per MIME type.</summary>
        public Dictionary<string, string[]> ExtensionsByType { get; }

        /// <summary>Every distinct extension.</summary>
        public string[] AllExtensions { get; }

        /// <summary>Every distinct top-level type (<c>image</c>, <c>audio</c>…).</summary>
        public string[] Types { get; }

        /// <summary>The cached index for <paramref name="table"/>.</summary>
        public static MimeIndex For(RecordsEntry table) => Cache.GetValue(table, static t => new MimeIndex(t));
    }
}
