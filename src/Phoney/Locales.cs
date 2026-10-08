using Phoney.Data;

namespace Phoney;

/// <summary>Lists, resolves and customizes the locales Phoney can generate data for.</summary>
public static class Locales
{
    /// <summary>The faker.js version the embedded locale data was imported from.</summary>
    public const string FakerVersion = LocaleCatalog.FakerVersion;

    /// <summary>The default locale, <c>en</c>.</summary>
    public const string Default = "en";

    /// <summary>All available locales, embedded and registered.</summary>
    public static IReadOnlyList<LocaleInfo> All => LocaleStore.All;

    /// <summary>
    /// Returns the locale matching <paramref name="code"/>: a locale code in any casing with <c>-</c> or <c>_</c>
    /// (<c>de-AT</c>, <c>de_AT</c>), or a .NET culture name resolved to the closest locale, including script-tagged
    /// names (<c>sv-SE</c> → <c>sv</c>, <c>sr-Latn-RS</c> → <c>sr_RS_latin</c>, <c>zh-Hant-TW</c> → <c>zh_TW</c>).
    /// A requested script is never substituted with another one.
    /// </summary>
    public static LocaleInfo Get(string code) => LocaleStore.Info(code);

    /// <summary>Whether <paramref name="code"/> resolves to an available locale.</summary>
    public static bool Exists(string code) => LocaleStore.TryResolve(code, out _);

    /// <summary>Returns the merged data of a locale, for direct inspection.</summary>
    public static LocaleData Data(string code) => LocaleStore.Get(code);

    /// <summary>
    /// Registers a new locale (or replaces a registered one).
    /// <code>
    /// Locales.Register("sv_dalarna", l => l
    ///     .FallbackTo("sv")
    ///     .Set("person.first_name.generic", "Kalle", "Anna"));
    /// </code>
    /// </summary>
    public static void Register(string code, Action<LocaleBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LocaleBuilder(code.Replace('-', '_'));
        configure(builder);
        LocaleStore.Register(builder);
    }

    /// <summary>Registers a locale from a faker.js-shaped JSON definition.</summary>
    public static void Register(string code, Stream utf8Json, params string[] fallback) =>
        Register(code, l =>
        {
            if (fallback.Length > 0)
                l.FallbackTo(fallback);
            l.ImportJson(utf8Json);
        });

    /// <summary>
    /// Adds or overrides data of an existing locale. Applies to <see cref="Faker"/>s created afterwards.
    /// <code>
    /// Locales.Extend("en", l => l.Set("commerce.department", "Toys", "Garden"));
    /// </code>
    /// </summary>
    public static void Extend(string code, Action<LocaleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LocaleBuilder(LocaleStore.Resolve(code));
        configure(builder);
        LocaleStore.Extend(code, builder);
    }
}
