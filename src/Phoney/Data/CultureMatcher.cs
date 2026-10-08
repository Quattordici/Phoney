namespace Phoney.Data;

/// <summary>
/// Matches BCP 47 / .NET culture names such as <c>sr-Latn-RS</c>, <c>zh-Hant-TW</c> or <c>ckb-IQ</c> to the closest
/// locale using the language, script and country metadata faker.js ships with every locale.
/// </summary>
/// <remarks>
/// <para>An explicit script must match, so <c>sr-Cyrl-RS</c> never silently yields Latin Serbian. Among the
/// remaining locales of the language the best is, in order of weight: same country, the language's default script,
/// a language-wide locale (no country), and a regular locale rather than a stylistic variant such as
/// <c>en_AU_ocker</c>. Remaining ties go to the alphabetically first code, so results are deterministic.</para>
/// <para>Works without ICU (no <see cref="System.Globalization.CultureInfo"/> lookups), so it behaves the same
/// with <c>InvariantGlobalization</c> and on every platform.</para>
/// </remarks>
internal static class CultureMatcher
{
    /// <summary>Language codes .NET/CLDR use for languages faker.js files under another language code.</summary>
    private static readonly Dictionary<string, (string Language, string Script)> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ckb"] = ("ku", "Arab"), // Central Kurdish (Sorani)
        ["kmr"] = ("ku", "Latn"), // Northern Kurdish (Kurmanji)
    };

    /// <summary>Default scripts (CLDR likely subtags) for languages written in more than one script.</summary>
    private static readonly Dictionary<string, string> DefaultScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ku"] = "Latn", // plain "ku" is Kurmanji in CLDR
        ["mn"] = "Cyrl",
        ["sr"] = "Cyrl",
        ["uz"] = "Latn",
        ["zh"] = "Hans",
    };

    /// <summary>
    /// Returns the code of the locale that best matches <paramref name="cultureName"/>, or <see langword="null"/>
    /// when no locale has the requested language (and script, if one is given).
    /// </summary>
    public static string? Match(string cultureName, IEnumerable<LocaleInfo> locales)
    {
        if (!TryParse(cultureName, out var language, out var script, out var region))
            return null;

        string? best = null;
        var bestScore = -1;
        foreach (var locale in locales)
        {
            if (!string.Equals(locale.Language, language, StringComparison.OrdinalIgnoreCase))
                continue;
            if (script is not null && !string.Equals(locale.Script, script, StringComparison.OrdinalIgnoreCase))
                continue;

            var score = 0;
            if (region is not null && string.Equals(locale.Country, region, StringComparison.OrdinalIgnoreCase))
                score += 8;
            if (script is null && DefaultScripts.TryGetValue(language, out var defaultScript)
                && string.Equals(locale.Script, defaultScript, StringComparison.OrdinalIgnoreCase))
                score += 4;
            if (locale.Country is null)
                score += 2;
            if (!IsVariant(locale))
                score += 1;

            if (score > bestScore || (score == bestScore && string.CompareOrdinal(locale.Code, best) < 0))
            {
                best = locale.Code;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// Splits a culture name into language, optional script (4 letters, title case) and optional region (2 letters
    /// or 3 digits). Variant subtags are ignored and parsing stops at extensions (<c>-u-…</c>, <c>-x-…</c>).
    /// </summary>
    internal static bool TryParse(string cultureName, out string language, out string? script, out string? region)
    {
        script = null;
        region = null;
        var parts = cultureName.Trim().Split('-', '_');
        language = parts[0].ToLowerInvariant();
        if (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetter))
            return false;

        if (LanguageAliases.TryGetValue(language, out var alias))
            (language, script) = alias;

        foreach (var part in parts.Skip(1))
        {
            if (part.Length == 1)
                break; // extension singleton: -u-ca-gregory, -x-private
            if (part.Length == 4 && part.All(char.IsAsciiLetter) && region is null)
                script ??= char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
            else if ((part.Length == 2 && part.All(char.IsAsciiLetter)) || (part.Length == 3 && part.All(char.IsAsciiDigit)))
                region ??= part.ToUpperInvariant();
        }

        return true;
    }

    /// <summary>
    /// A variant is a locale whose code is more than <c>language</c> or <c>language_COUNTRY</c>, e.g. <c>en_AU_ocker</c>
    /// or <c>sr_RS_latin</c>; regular locales win ties against them.
    /// </summary>
    private static bool IsVariant(LocaleInfo locale) =>
        !string.Equals(locale.Code, locale.Language, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(locale.Code, $"{locale.Language}_{locale.Country}", StringComparison.OrdinalIgnoreCase);
}
