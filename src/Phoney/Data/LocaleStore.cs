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
    private sealed class Source(LocaleInfo info, Func<Entry?[]> load)
    {
        private readonly Lazy<Entry?[]> _embedded = new(load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Locale description, including the fallback chain.</summary>
        public LocaleInfo Info { get; set; } = info;

        /// <summary>User-supplied entries by key id; they win over the embedded data of every locale in the chain after this one.</summary>
        public Dictionary<int, Entry> User { get; } = [];

        /// <summary>The embedded (faker.js) entries; empty for user-registered locales.</summary>
        public Entry?[] Embedded => _embedded.Value;
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
            var source = new Source(info, static () => []);
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
        Source[] chain;
        (int Key, Entry Entry)[][] user;
        lock (Gate)
        {
            var source = Sources[code];
            chain = [source, .. source.Info.Fallback.Select(f => Sources[f])];
            user = [.. chain.Select(c => c.User.Select(u => (u.Key, u.Value)).ToArray())];
        }

        var keyCount = KeyRegistry.Count;
        var embedded = chain.Select(s => s.Embedded).ToArray();

        // Embedded data follows faker.js: a group (person.first_name, location.city_pattern…) comes whole from the
        // first locale in the chain that defines any of it.
        var groupPresent = new bool[chain.Length][];
        for (var c = 0; c < chain.Length; c++)
        {
            groupPresent[c] = new bool[KeyRegistry.GroupCount];
            for (var k = 0; k < embedded[c].Length; k++)
            {
                if (embedded[c][k] is not null)
                    groupPresent[c][KeyRegistry.GroupOf(k)] = true;
            }
        }

        var userEntries = user.Select(u => u.ToDictionary(x => x.Key, x => x.Entry)).ToArray();
        var merged = new Entry?[keyCount];
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
                    merged[k] = k < embedded[c].Length ? embedded[c][k] : null;
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
