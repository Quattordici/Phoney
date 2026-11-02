namespace Phoney.Generation;

/// <summary>
/// The values of the generation settings an app can set once in <see cref="Fake.Defaults"/>: the built-in ones, or the app's. A generator resolves its own
/// <see cref="SettingOverrides"/> against them when it compiles, so the defaults only fill what nothing more specific set.
/// </summary>
internal sealed record GenerationDefaults(
    double NullProbability,
    int MinCollectionSize,
    int MaxCollectionSize,
    int MaxDepth,
    bool Strict,
    bool BackReferences,
    bool CausalTimelines,
    bool ParameterDefaults,
    bool NameConventions)
{
    /// <summary>What a generator does when neither it nor the app says otherwise.</summary>
    public static readonly GenerationDefaults BuiltIn = new(0, 1, 3, 4, Strict: false, BackReferences: false, CausalTimelines: false, ParameterDefaults: false, NameConventions: true);

    /// <summary>These defaults with what <paramref name="overrides"/> set on top.</summary>
    public GenerationDefaults With(SettingOverrides overrides) => new(
        overrides.NullProbability ?? NullProbability,
        overrides.MinCollectionSize ?? MinCollectionSize,
        overrides.MaxCollectionSize ?? MaxCollectionSize,
        overrides.MaxDepth ?? MaxDepth,
        overrides.Strict ?? Strict,
        overrides.BackReferences ?? BackReferences,
        overrides.CausalTimelines ?? CausalTimelines,
        overrides.ParameterDefaults ?? ParameterDefaults,
        overrides.NameConventions ?? NameConventions);

    /// <summary>The run settings with these values (Strict and back-references are the generator's own steps, not run settings).</summary>
    public GenerationSettings ApplyTo(GenerationSettings settings) => settings with
    {
        WithNameConventions = NameConventions,
        NullProbability = NullProbability,
        MinCollectionSize = MinCollectionSize,
        MaxCollectionSize = MaxCollectionSize,
        MaxDepth = MaxDepth,
        CausalTimelines = CausalTimelines,
        ParameterDefaults = ParameterDefaults,
    };

    /// <summary>Validates a null probability like <c>NullProbability(p)</c> does.</summary>
    public static void CheckNullProbability(double probability, string parameterName)
    {
        if (probability is not (>= 0 and <= 1))
            throw new ArgumentOutOfRangeException(parameterName, probability, "Must be between 0 and 1.");
    }

    /// <summary>Validates a collection size like <c>CollectionSize(min, max)</c> does.</summary>
    public static void CheckCollectionSize(int min, int max, string minName, string maxName)
    {
        if (min < 0)
            throw new ArgumentOutOfRangeException(minName, min, "The minimum must not be negative.");
        if (max < min)
            throw new ArgumentOutOfRangeException(maxName, max, $"The maximum must be at least the minimum, {min}.");
    }
}

/// <summary>The generation settings a generator was given itself; <see langword="null"/> where it takes <see cref="Fake.Defaults"/>.</summary>
internal readonly record struct SettingOverrides(
    double? NullProbability = null,
    int? MinCollectionSize = null,
    int? MaxCollectionSize = null,
    int? MaxDepth = null,
    bool? Strict = null,
    bool? BackReferences = null,
    bool? CausalTimelines = null,
    bool? ParameterDefaults = null,
    bool? NameConventions = null);
