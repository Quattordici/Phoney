namespace Phoney.Generation;

/// <summary>
/// A type-wide generator registered with <see cref="FakeGenerators.Configure{T}"/>: a delegate applied to the plain generator of whichever model the caller uses.
/// The facade applies it to its own prototype (<see cref="Apply(Registrations, ObjectGenerator{T})"/>); nested objects use one generator on the typed model,
/// built on first use, so it needs no reflection annotations of its own.
/// </summary>
internal sealed class TypeConfiguration<T>(Func<ObjectGenerator<T>, ObjectGenerator<T>> configure) : INestedGenerator
{
    private ObjectGenerator<T>? _nested;

    /// <inheritdoc />
    public Type GeneratedType => typeof(T);

    /// <inheritdoc />
    public string? CannotBeNested => null; // checked when the configuration is applied: the delegate has not run yet

    /// <inheritdoc />
    public object? CreateNested(FakeScope parent) => ((INestedGenerator)Nested).CreateNested(parent);

    /// <summary>The generator for nested objects, shared like one registered with <see cref="FakeGenerators.Add{TNested}"/>.</summary>
    private ObjectGenerator<T> Nested
    {
        get
        {
            if (Volatile.Read(ref _nested) is { } built)
                return built;
            var configured = Apply(new ObjectGenerator<T>(ModelRegistry.Nested<T>()));
            return Interlocked.CompareExchange(ref _nested, configured, null) ?? configured;
        }
    }

    /// <summary><paramref name="plain"/> configured by the registration for <typeparamref name="T"/> in <paramref name="registrations"/>, or as it is without one.</summary>
    public static ObjectGenerator<T> Apply(Registrations registrations, ObjectGenerator<T> plain) =>
        registrations.Generators.TryGetValue(typeof(T), out var registered) && registered is TypeConfiguration<T> configuration ? configuration.Apply(plain) : plain;

    /// <summary>Runs the delegate on <paramref name="plain"/> and refuses a result that would silently misbehave somewhere the configuration reaches.</summary>
    /// <exception cref="InvalidOperationException">The delegate returned <see langword="null"/>, set a seed, or added rules a nested generator can't have.</exception>
    public ObjectGenerator<T> Apply(ObjectGenerator<T> plain)
    {
        var configured = configure(plain) ?? throw Refused("returned null; return the configured generator.");
        if (configured.ConfiguredSeed != plain.ConfiguredSeed)
        {
            throw Refused(
                "sets a seed, but a type-wide generator takes its seed from where the object is made (Fake.Seeded, the generator it is nested in). Remove Seed from the configuration and seed the caller.");
        }

        return ((INestedGenerator)configured).CannotBeNested is { } reason ? throw Refused(reason) : configured;
    }

    private static InvalidOperationException Refused(string reason) =>
        new($"The {typeof(T).Name} generator from Fake.Generators.Configure<{typeof(T).Name}> {reason}");
}
