using System.Collections.Concurrent;

namespace Phoney.Data;

/// <summary>
/// Owns every locale's own data (embedded or user registered) and builds merged <see cref="LocaleData"/>
/// views on first use, so lookups never walk the fallback chain at generation time. Embedded data merges like
/// faker.js (each <c>module.entry</c> group comes whole from the first locale defining it); user data merges per key.
/// </summary>
internal static class LocaleStore
{
    /// <summary>
    /// A locale's data in two layers: embedded faker.js data (merged per entry group, like faker.js) and user data
    /// from <see cref="Locales.Register(string, System.Action{LocaleBuilder})"/> or <see cref="Locales.Extend"/>
    /// (merged per key, so setting female first names keeps the inherited male ones).
    /// </summary>
    private sealed class Source(LocaleInfo info, Func<LocaleResource> load)
    {
        private readonly Lazy<LocaleResource> _embedded = new(load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Locale description, including the fallback chain.</summary>
        public LocaleInfo Locale { get; set; } = info;

        /// <summary>User-supplied entries by key id; they win over the embedded data of every locale in the chain after this one.</summary>
        public Dictionary<int, Entry> User { get; } = [];

        /// <summary>The embedded (faker.js) data, opened on first use and decoded entry by entry; empty for user-registered locales.</summary>
        public LocaleResource Embedded => _embedded.Value;
    }

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, Source> Sources = CreateEmbeddedSources();
    private static readonly ConcurrentDictionary<string, Lazy<LocaleData>> Merged = new(StringComparer.Ordinal);

    /// <summary>All known locales, sorted by code.</summary>
    public static IReadOnlyList<LocaleInfo> All =>
        [.. Sources.Values.Select(s => s.Locale).OrderBy(i => i.Code, StringComparer.Ordinal)];

    /// <summary>Merged data of a locale, built on first use and cached.</summary>
    public static LocaleData Get(string code)
    {
        var canonical = Resolve(code);
        return Merged.GetOrAdd(canonical, static c => new Lazy<LocaleData>(() => Build(c))).Value;
    }

    /// <summary>Description of a locale.</summary>
    public static LocaleInfo Info(string code) => Sources[Resolve(code)].Locale;

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
        IEnumerable<LocaleInfo> locales() => Sources.Values.Select(s => s.Locale).OrderBy(i => i.Code, StringComparer.Ordinal); // only the fuzzy matches need it; reading Values locks the whole dictionary
        var normalized = code.Trim().Replace('-', '_');

        // 1. An existing code in any casing and separator style.
        if (FindCode(normalized) is { } exact)
            return Found(exact, out canonical);

        // 2. Script-tagged culture names: stripping segments would drop the region (en-Latn-US → en), so match
        //    on language/script/country instead (sr-Latn-RS → sr_RS_latin, zh-Hant-TW → zh_TW).
        if (CultureMatcher.TryParse(code, out _, out var script, out _) && script is not null &&
            CultureMatcher.Match(code, locales()) is { } scripted)
            return Found(scripted, out canonical);

        // 3. Drop trailing segments: sv_SE → sv, de_AT_u_ca → de_AT, sv_dalarna_x → a custom sv_dalarna.
        for (var candidate = normalized; candidate.LastIndexOf('_') is var cut and > 0;)
        {
            candidate = candidate[..cut];
            if (FindCode(candidate) is { } prefix)
                return Found(prefix, out canonical);
        }

        // 4. The closest locale of the same language: mn-MN → mn_MN_cyrl, ckb-IQ → ku_ckb, pt-AO → pt_BR.
        if (CultureMatcher.Match(code, locales()) is { } closest)
            return Found(closest, out canonical);

        return false;

        static bool Found(string code, out string canonical)
        {
            canonical = code;
            return true;
        }

        // The exact code wins; codes that differ only in case (a custom "SV" next to "sv") resolve to the first in ordinal order.
        static string? FindCode(string candidate) =>
            Sources.ContainsKey(candidate)
                ? candidate
                : Sources.Keys.Where(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>Adds or replaces a user-defined locale and invalidates merged data.</summary>
    public static void Register(LocaleBuilder builder)
    {
        lock (Gate)
        {
            foreach (var fallback in builder.FallbackCodes.Where(code => !Sources.ContainsKey(code)))
            {
                throw new ArgumentException($"Fallback locale '{fallback}' of '{builder.Code}' is not registered.");
            }

            var info = new LocaleInfo(builder.Code, builder.TitleText, null, null, null, null, "ltr", builder.FallbackCodes);
            var source = new Source(info, static () => LocaleResource.Empty);
            foreach (var (key, entry) in builder.Changes)
            {
                if (entry is null)
                    source.User.Remove(key); // an empty Set(...) removes an earlier value
                else
                    source.User[key] = entry;
            }
            Sources[builder.Code] = source;
            Merged.Clear();
        }
    }

    /// <summary>Adds user data to a locale (per key) and invalidates merged data.</summary>
    public static void Extend(string code, LocaleBuilder builder)
    {
        lock (Gate)
        {
            var source = Sources[Resolve(code)];
            foreach (var (key, entry) in builder.Changes)
            {
                if (entry is null)
                    source.User.Remove(key); // an empty Set(...) removes an earlier value
                else
                    source.User[key] = entry;
            }
            Merged.Clear();
        }
    }

    /// <summary>Merges a locale with its fallback chain: each group comes from the first locale that has it.</summary>
    private static LocaleData Build(string code)
    {
        var (chain, userEntries) = Snapshot(code);
        var keyCount = KeyRegistry.Count;
        var embedded = new LocaleResource[chain.Length];
        for (var c = 0; c < chain.Length; c++)
            embedded[c] = chain[c].Embedded; // opens the files; no entry is decoded here

        var groupPresent = PresentGroups(embedded);
        var merged = new Entry?[keyCount];
        var origin = new LocaleResource?[keyCount]; // where an embedded entry is decoded from when it is first read
        for (var k = 0; k < keyCount; k++)
        {
            var group = KeyRegistry.GroupOf(k);
            for (var c = 0; c < chain.Length; c++)
            {
                // User data is merged per key: it only replaces what was set.
                if (userEntries[c].TryGetValue(k, out var entry))
                {
                    merged[k] = entry;
                    break;
                }

                if (group < groupPresent[c].Length && groupPresent[c][group])
                {
                    origin[k] = embedded[c].Has(k) ? embedded[c] : null;
                    break;
                }
            }
        }

        return new LocaleData(chain[0].Locale, merged, origin);
    }

    /// <summary>The locale and its fallbacks in order, with a snapshot of each one's user entries (only looked up by key, so their order never matters).</summary>
    private static (Source[] Chain, Dictionary<int, Entry>[] UserEntries) Snapshot(string code)
    {
        lock (Gate)
        {
            var source = Sources[code];
            var fallback = source.Locale.Fallback;
            var chain = new Source[fallback.Count + 1];
            var userEntries = new Dictionary<int, Entry>[chain.Length];
            chain[0] = source;
            for (var c = 1; c < chain.Length; c++)
                chain[c] = Sources[fallback[c - 1]];
            for (var c = 0; c < chain.Length; c++)
                userEntries[c] = new Dictionary<int, Entry>(chain[c].User);
            return (chain, userEntries);
        }
    }

    /// <summary>
    /// The groups each embedded locale defines. Embedded data follows faker.js: a group (person.first_name, location.city_pattern…) comes whole from the
    /// first locale in the chain that defines any of it.
    /// </summary>
    private static bool[][] PresentGroups(LocaleResource[] embedded)
    {
        var groupPresent = new bool[embedded.Length][];
        for (var c = 0; c < embedded.Length; c++)
        {
            groupPresent[c] = new bool[KeyRegistry.GroupCount];
            for (var k = 0; k < DataKeys.Count; k++)
            {
                if (embedded[c].Has(k))
                    groupPresent[c][KeyRegistry.GroupOf(k)] = true;
            }
        }

        return groupPresent;
    }

    /// <summary>One lazily loaded source per embedded locale.</summary>
    private static ConcurrentDictionary<string, Source> CreateEmbeddedSources()
    {
        var sources = new ConcurrentDictionary<string, Source>(StringComparer.Ordinal);
        foreach (var info in LocaleCatalog.Embedded)
        {
            var code = info.Code;
            sources[code] = new Source(info, () => LocaleResource.Open(code)
                ?? throw new PhoneyDataException($"The embedded data for locale '{code}' is missing from the Phoney assembly."));
        }

        return sources;
    }
}
