namespace Phoney.Tests;

/// <summary>Locale codes for data-driven tests.</summary>
public static class TestLocales
{
    /// <summary>
    /// Every embedded locale code except <c>base</c>, for tests that must hold in all locales.
    /// <c>base</c> only holds locale-independent data (emoji, MIME types…) that every other locale falls back to.
    /// </summary>
    public static IEnumerable<string> All() => Locales.All.Where(l => l.Code != "base").Select(l => l.Code);
}
