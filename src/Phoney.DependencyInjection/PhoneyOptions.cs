namespace Phoney.DependencyInjection;

/// <summary>How the fakers and generators registered by <c>services.AddPhoney()</c> are set up.</summary>
public sealed class PhoneyOptions
{
    /// <summary>Locale code, e.g. <c>sv</c> or <c>de-AT</c>; <see langword="null"/> uses <see cref="Fake.Locale"/>.</summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Seed for reproducible data; random when <see langword="null"/>. Every scope's <see cref="IFaker"/> starts at this
    /// seed, so a request or test scope gets the same values on every run.
    /// </summary>
    public long? Seed { get; set; }

    /// <summary>Point in time relative dates are computed from; takes precedence over any <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset? ReferenceDate { get; set; }

    /// <summary>
    /// Clock for relative dates. When <see langword="null"/>, a <see cref="System.TimeProvider"/> registered in the
    /// container is used, unless it is <see cref="TimeProvider.System"/> (which would make seeded dates move with the clock).
    /// </summary>
    public TimeProvider? TimeProvider { get; set; }
}
