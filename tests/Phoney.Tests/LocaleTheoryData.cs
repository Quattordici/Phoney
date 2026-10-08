namespace Phoney.Tests;

/// <summary>
/// Every embedded locale code except <c>base</c>, for theories that must hold in all locales.
/// <c>base</c> only holds locale-independent data (emoji, MIME types…) that every other locale falls back to.
/// </summary>
public sealed class AllLocales : TheoryData<string>
{
    public AllLocales()
    {
        foreach (var locale in Locales.All.Where(l => l.Code != "base"))
            Add(locale.Code);
    }
}
