using System.Collections.Concurrent;
using Phoney.Schemes;

namespace Phoney;

/// <summary>
/// The registry of national identifier schemes (personnummer, SSN, CPF, organisation numbers, VAT numbers…): look
/// up, validate and register schemes. <see cref="Modules.NationalIdModule"/> generates values from it.
/// </summary>
/// <example>
/// <code>
/// NationalIds.IsValid(NationalIdKind.Person, "SE", "19560703-6190");
/// NationalIds.Supported(NationalIdKind.Vat);   // ["DK", "FI", "FR", "GB", "NO", "PL", "SE"]
/// </code>
/// </example>
public static class NationalIds
{
    private static readonly ConcurrentDictionary<(NationalIdKind Kind, string Country), NationalIdScheme> Schemes = new();

    /// <summary>The country most speakers of a language-wide locale (<c>sv</c>, <c>en</c>…) live in.</summary>
    private static readonly Dictionary<string, string> LanguageCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sv"] = "SE",
        ["da"] = "DK",
        ["fi"] = "FI",
        ["nb"] = "NO",
        ["en"] = "US",
        ["fr"] = "FR",
        ["nl"] = "NL",
        ["pl"] = "PL",
        ["es"] = "ES",
        ["pt"] = "BR",
    };

    static NationalIds()
    {
        foreach (var scheme in BuiltInSchemes.All())
            Schemes[(scheme.Kind, scheme.Country)] = scheme;
    }

    /// <summary>Adds a scheme, or replaces the registered scheme for the same kind and country.</summary>
    public static void Register(NationalIdScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        Schemes[(scheme.Kind, scheme.Country)] = scheme;
    }

    /// <summary>Removes the scheme for <paramref name="kind"/> and <paramref name="country"/>; false when there was none.</summary>
    public static bool Remove(NationalIdKind kind, string country) => Schemes.TryRemove((kind, Normalize(country)), out _);

    /// <summary>Returns the scheme for <paramref name="kind"/> and <paramref name="country"/>, or <see langword="null"/> when there is none.</summary>
    public static NationalIdScheme? Find(NationalIdKind kind, string country) =>
        Schemes.TryGetValue((kind, Normalize(country)), out var scheme) ? scheme : null;

    /// <summary>Returns the scheme for <paramref name="kind"/> and <paramref name="country"/>.</summary>
    /// <exception cref="ArgumentException">There is no such scheme; the message lists the supported countries.</exception>
    public static NationalIdScheme Get(NationalIdKind kind, string country) =>
        Find(kind, country) ?? throw new ArgumentException(
            $"There is no {kind} identifier scheme for '{country}'. Supported countries: {string.Join(", ", Supported(kind))}. Add one with NationalIds.Register.",
            nameof(country));

    /// <summary>Whether <paramref name="value"/> is a valid identifier of the given kind and country.</summary>
    /// <exception cref="ArgumentException">There is no such scheme.</exception>
    public static bool IsValid(NationalIdKind kind, string country, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Get(kind, country).IsValid(value);
    }

    /// <summary>The countries that have a scheme of <paramref name="kind"/>, sorted.</summary>
    public static IReadOnlyList<string> Supported(NationalIdKind kind) =>
        [.. Schemes.Keys.Where(k => k.Kind == kind).Select(k => k.Country).Order(StringComparer.Ordinal)];

    /// <summary>The country a locale stands for: its own, or the main country of its language; <see langword="null"/> when unknown.</summary>
    internal static string? CountryOf(LocaleInfo locale)
    {
        if (locale.Country is { Length: > 0 } country)
            return country.ToUpperInvariant();
        return locale.Language is { } language && LanguageCountry.TryGetValue(language, out var main) ? main : null;
    }

    private static string Normalize(string country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return country.Trim().ToUpperInvariant();
    }
}
