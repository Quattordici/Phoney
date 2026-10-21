namespace Phoney.Tests;

/// <summary>Locale codes for data-driven tests.</summary>
public static class TestLocales
{
    /// <summary>
    /// Every embedded locale code except <c>base</c>, for tests that must hold in all locales.
    /// <c>base</c> only holds locale-independent data (emoji, MIME types…) that every other locale falls back to. Locales that tests register are left out:
    /// some are broken on purpose.
    /// </summary>
    public static IEnumerable<string> All() => Data.LocaleCatalog.Embedded.Where(l => l.Code != "base").Select(l => l.Code).Order(StringComparer.Ordinal);
}
