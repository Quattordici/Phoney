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

    /// <summary>Probability (0–1) that nullable members of generated objects are left <see langword="null"/>; the generator default (0) when <see langword="null"/>.</summary>
    public double? NullProbability { get; set; }

    /// <summary>
    /// Fewest elements a generated collection gets; the generator default (1) when <see langword="null"/>. When only this is set, the
    /// most is the larger of this and the default (3).
    /// </summary>
    public int? MinCollectionSize { get; set; }

    /// <summary>
    /// Most elements a generated collection gets; the generator default (3) when <see langword="null"/>. When only this is set, the
    /// fewest is the smaller of this and the default (1).
    /// </summary>
    public int? MaxCollectionSize { get; set; }

    /// <summary>How deep nested objects are generated; the generator default (4) when <see langword="null"/>.</summary>
    public int? MaxDepth { get; set; }

    /// <summary>Makes registered generators strict (a scalar member without a rule, convention or annotation throws), see <c>ObjectGenerator&lt;T&gt;.Strict</c>. Off by default.</summary>
    public bool Strict { get; set; }

#pragma warning disable S3236, S3928 // the parameter name is the option that is wrong (MaxCollectionSize…), which is what a caller of AddPhoney needs to see
    /// <summary>Checks the generation options; used by <c>AddPhoney</c> so a mistake shows at registration rather than at the first resolve.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An option is outside its range.</exception>
    internal void Validate()
    {
        if (NullProbability is { } probability)
        {
            if (double.IsNaN(probability))
                throw new ArgumentOutOfRangeException(nameof(NullProbability), probability, "NullProbability must be a number between 0 and 1.");
            ArgumentOutOfRangeException.ThrowIfNegative(probability, nameof(NullProbability));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(probability, 1, nameof(NullProbability));
        }

        if (MinCollectionSize is { } min)
            ArgumentOutOfRangeException.ThrowIfNegative(min, nameof(MinCollectionSize));
        if (MaxCollectionSize is { } max)
            ArgumentOutOfRangeException.ThrowIfNegative(max, nameof(MaxCollectionSize));
        if (MinCollectionSize is { } low && MaxCollectionSize is { } high && high < low)
            throw new ArgumentOutOfRangeException(nameof(MaxCollectionSize), high, $"MaxCollectionSize must not be less than MinCollectionSize ({low}).");
        if (MaxDepth is { } depth)
            ArgumentOutOfRangeException.ThrowIfNegative(depth, nameof(MaxDepth));
    }
#pragma warning restore S3236, S3928
}
