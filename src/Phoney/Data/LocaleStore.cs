using System.Collections.Concurrent;

namespace Phoney.Data;

/// <summary>
/// Owns every locale's own data (embedded or user registered) and builds merged <see cref="LocaleData"/>
/// views on first use. Merging follows faker.js: each <c>module.entry</c> group comes whole from the first
/// locale in the fallback chain that defines it, so lookups never walk the chain at generation time.
/// </summary>
internal static class LocaleStore
{
    /// <summary>A locale's own data (embedded or registered) plus user overrides from <see cref="Locales.Extend"/>.</summary>
    private sealed class Source(LocaleInfo info, Func<Entry?[]> load)
    {
        private readonly Lazy<Entry?[]> _own = new(load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Locale description, including the fallback chain.</summary>
        public LocaleInfo Info { get; set; } = info;

        /// <summary>Entries replaced or added with <see cref="Locales.Extend"/>.</summary>
        public Dictionary<int, Entry?> Overlay { get; } = [];

        /// <summary>The locale's own entries with the overlay applied.</summary>
        public Entry?[] OwnEntries()
        {
            var own = _own.Value;
            if (Overlay.Count == 0)
                return own;
            var copy = new Entry?[Math.Max(own.Length, Overlay.Keys.Max() + 1)];
            own.CopyTo(copy, 0);
            foreach (var (key, entry) in Overlay)
                copy[key] = entry;
            return copy;
        }
    }

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, Source> Sources = CreateEmbeddedSources();
    private static readonly ConcurrentDictionary<string, Lazy<LocaleData>> Merged = new(StringComparer.Ordinal);

    /// <summary>All known locales, sorted by code.</summary>
    public static IReadOnlyList<LocaleInfo> All =>
        [.. Sources.Values.Select(s => s.Info).OrderBy(i => i.Code, StringComparer.Ordinal)];

    /// <summary>Merged data of a locale, built on first use and cached.</summary>
    public static LocaleData Get(string code)
    {
        var canonical = Resolve(code);
        return Merged.GetOrAdd(canonical, static c => new Lazy<LocaleData>(() => Build(c))).Value;
    }

    /// <summary>Description of a locale.</summary>
    public static LocaleInfo Info(string code) => Sources[Resolve(code)].Info;

    /// <summary>
    /// Maps user input to an existing locale code: an exact code in any casing and with <c>-</c> or <c>_</c>
    /// (<c>de-AT</c>, <c>DE_at</c>) or one after dropping trailing segments (<c>sv-SE</c> → <c>sv</c>), otherwise the
    /// closest locale for a culture name, including script-tagged ones (<c>sr-Latn-RS</c> → <c>sr_RS_latin</c>,
    /// <c>zh-Hant-TW</c> → <c>zh_TW</c>, <c>ckb-IQ</c> → <c>ku_ckb</c>; see <see cref="CultureMatcher"/>).
    /// </summary>
    public static string Resolve(string code)
    {
        if (TryResolve(code, out var canonical))
            return canonical;
        throw new ArgumentException(
            $"Unknown locale '{code}'. Available locales: {string.Join(", ", Sources.Keys.Order(StringComparer.Ordinal))}.",
            nameof(code));
    }

    /// <summary>Non-throwing <see cref="Resolve"/>.</summary>
    public static bool TryResolve(string? code, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(code))
            return false;
        var locales = Sources.Values.Select(s => s.Info);
        var normalized = code.Trim().Replace('-', '_');

        // 1. An existing code in any casing and separator style.
        if (FindCode(normalized) is { } exact)
            return Found(exact, out canonical);

        // 2. Script-tagged culture names: stripping segments would drop the region (en-Latn-US → en), so match
        //    on language/script/country instead (sr-Latn-RS → sr_RS_latin, zh-Hant-TW → zh_TW).
        if (CultureMatcher.TryParse(code, out _, out var script, out _) && script is not null &&
            CultureMatcher.Match(code, locales) is { } scripted)
            return Found(scripted, out canonical);

        // 3. Drop trailing segments: sv_SE → sv, de_AT_u_ca → de_AT, sv_dalarna_x → a custom sv_dalarna.
        for (var candidate = normalized; candidate.LastIndexOf('_') is var cut and > 0;)
        {
            candidate = candidate[..cut];
            if (FindCode(candidate) is { } prefix)
                return Found(prefix, out canonical);
        }

        // 4. The closest locale of the same language: mn-MN → mn_MN_cyrl, ckb-IQ → ku_ckb, pt-AO → pt_BR.
        if (CultureMatcher.Match(code, locales) is { } closest)
            return Found(closest, out canonical);

        return false;

        static bool Found(string code, out string canonical)
        {
            canonical = code;
            return true;
        }

        static string? FindCode(string candidate) =>
            Sources.Keys.FirstOrDefault(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or replaces a user-defined locale and invalidates merged data.</summary>
    public static void Register(LocaleBuilder builder)
    {
        lock (Gate)
        {
            foreach (var fallback in builder.FallbackCodes)
            {
                if (!Sources.ContainsKey(fallback))
                    throw new ArgumentException($"Fallback locale '{fallback}' of '{builder.Code}' is not registered.");
            }

            var info = new LocaleInfo(builder.Code, builder.TitleText, null, null, null, null, "ltr", builder.FallbackCodes);
            var entries = builder.BuildEntries();
            Sources[builder.Code] = new Source(info, () => entries);
            Merged.Clear();
        }
    }

    /// <summary>Overlays user data on a locale and invalidates merged data.</summary>
    public static void Extend(string code, LocaleBuilder builder)
    {
        lock (Gate)
        {
            var source = Sources[Resolve(code)];
            foreach (var (key, entry) in builder.Changes)
                source.Overlay[key] = entry;
            Merged.Clear();
        }
    }

    /// <summary>Merges a locale with its fallback chain: each group comes from the first locale that has it.</summary>
    private static LocaleData Build(string code)
    {
        Source[] chain;
        lock (Gate)
        {
            var source = Sources[code];
            chain = [source, .. source.Info.Fallback.Select(f => Sources[f])];
        }

        var keyCount = KeyRegistry.Count;
        var own = chain.Select(s => s.OwnEntries()).ToArray();
        var groupPresent = new bool[own.Length][];
        for (var c = 0; c < own.Length; c++)
        {
            groupPresent[c] = new bool[KeyRegistry.GroupCount];
            for (var k = 0; k < own[c].Length; k++)
            {
                if (own[c][k] is not null)
                    groupPresent[c][KeyRegistry.GroupOf(k)] = true;
            }
        }

        var merged = new Entry?[keyCount];
        for (var k = 0; k < keyCount; k++)
        {
            var group = KeyRegistry.GroupOf(k);
            for (var c = 0; c < own.Length; c++)
            {
                if (group < groupPresent[c].Length && groupPresent[c][group])
                {
                    merged[k] = k < own[c].Length ? own[c][k] : null;
                    break;
                }
            }
        }

        return new LocaleData(chain[0].Info, merged);
    }

    /// <summary>One lazily loaded source per embedded locale.</summary>
    private static ConcurrentDictionary<string, Source> CreateEmbeddedSources()
    {
        var sources = new ConcurrentDictionary<string, Source>(StringComparer.Ordinal);
        foreach (var info in LocaleCatalog.Embedded)
        {
            var code = info.Code;
            sources[code] = new Source(info, () => LocaleResourceReader.ReadEmbedded(code)
                ?? throw new PhoneyDataException($"The embedded data for locale '{code}' is missing from the Phoney assembly."));
        }

        return sources;
    }
}
