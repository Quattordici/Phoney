namespace Phoney.Generation;

/// <summary>
/// App-wide defaults for the generation settings, used wherever nothing more specific set them: by <c>Fake.One</c>, <c>Fake.Many</c>, <c>Fake.For</c> and every
/// generator compiled afterwards (nested objects included), by <c>[FakeData]</c>/<c>[FakeCheck]</c> parameters and by <c>Phoney.DependencyInjection</c> generators.
/// </summary>
/// <remarks>
/// <para>
/// A setting on the generator itself (<c>.CollectionSize(2, 2)</c>, also inside <c>Fake.Generators.Configure&lt;T&gt;</c>), a <c>PhoneyOptions</c> value and an
/// attribute option you set win over these; these win over the built-in defaults. Like <see cref="Fake.Conventions"/>, they are read when a generator is first used,
/// so set them at startup (a module initializer, an assembly-level test hook). They are process-wide: a test that changes them affects tests running in parallel.
/// </para>
/// <para>Seeds, reference dates and the locale have their own app-wide settings: <see cref="Fake.Seed"/>, <see cref="Fake.TimeProvider"/> and <see cref="Fake.Locale"/>.</para>
/// </remarks>
/// <example>
/// <code>
/// Fake.Defaults.Strict = true;               // every generator in the test suite
/// Fake.Defaults.CollectionSize = (0, 5);
/// Fake.Defaults.BackReferences = true;
/// </code>
/// </example>
public sealed class FakeDefaults
{
    private static GenerationDefaults s_current = GenerationDefaults.BuiltIn;

    internal FakeDefaults()
    {
    }

    /// <summary>Snapshot of the defaults.</summary>
    internal static GenerationDefaults Current => Volatile.Read(ref s_current);

#pragma warning disable S2325 // instance members on purpose: Fake.Defaults is one registry discovered from Fake., like Fake.Conventions
    /// <summary>Probability (0–1) that nullable members and parameters are left <see langword="null"/>. Built-in: 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double NullProbability
    {
        get => Current.NullProbability;
        set
        {
            GenerationDefaults.CheckNullProbability(value, nameof(NullProbability));
            Update(d => d with { NullProbability = value });
        }
    }

    /// <summary>How many elements generated collections get. Built-in: (1, 3).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><c>Min</c> is negative or <c>Max</c> is less than <c>Min</c>.</exception>
    public (int Min, int Max) CollectionSize
    {
        get => (Current.MinCollectionSize, Current.MaxCollectionSize);
        set
        {
            GenerationDefaults.CheckCollectionSize(value.Min, value.Max, nameof(CollectionSize), nameof(CollectionSize));
            Update(d => d with { MinCollectionSize = value.Min, MaxCollectionSize = value.Max });
        }
    }

    /// <summary>How deep nested objects are generated; deeper members stay <see langword="null"/>. Built-in: 4.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int MaxDepth
    {
        get => Current.MaxDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(MaxDepth));
            Update(d => d with { MaxDepth = value });
        }
    }

    /// <summary>Whether generators fail for scalar members nothing gives a value (see <see cref="ObjectGenerator{T}.Strict"/>). Built-in: <see langword="false"/>.</summary>
    public bool Strict
    {
        get => Current.Strict;
        set => Update(d => d with { Strict = value });
    }

    /// <summary>Whether <c>null</c> object members point back at the nearest containing object (see <see cref="ObjectGenerator{T}.WithBackReferences"/>). Built-in: <see langword="false"/>.</summary>
    public bool BackReferences
    {
        get => Current.BackReferences;
        set => Update(d => d with { BackReferences = value });
    }

    /// <summary>Whether nested objects are created after the object that contains them (see <see cref="ObjectGenerator{T}.WithCausalTimelines"/>). Built-in: <see langword="false"/>.</summary>
    public bool CausalTimelines
    {
        get => Current.CausalTimelines;
        set => Update(d => d with { CausalTimelines = value });
    }

    /// <summary>Whether optional constructor parameters get their declared default (see <see cref="ObjectGenerator{T}.WithParameterDefaults"/>). Built-in: <see langword="false"/>.</summary>
    public bool ParameterDefaults
    {
        get => Current.ParameterDefaults;
        set => Update(d => d with { ParameterDefaults = value });
    }

    /// <summary>Whether members get values from their names (<c>Email</c>, <c>City</c>…) and not only their types (see <see cref="ObjectGenerator{T}.WithNameConventions"/>). Built-in: <see langword="true"/>.</summary>
    public bool NameConventions
    {
        get => Current.NameConventions;
        set => Update(d => d with { NameConventions = value });
    }

    /// <summary>Restores the built-in defaults.</summary>
    public void Reset() => Volatile.Write(ref s_current, GenerationDefaults.BuiltIn);
#pragma warning restore S2325

    private static void Update(Func<GenerationDefaults, GenerationDefaults> change)
    {
        GenerationDefaults current, changed;
        do
        {
            current = Volatile.Read(ref s_current);
            changed = change(current);
        }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref s_current, changed, current), current));
    }
}
