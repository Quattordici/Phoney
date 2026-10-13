using System.Diagnostics.CodeAnalysis;
using Phoney.Generation;

namespace Phoney;

// Object generation entry points of the static facade.
public static partial class Fake
{
    private const string ReflectionMessage =
        "Uses reflection for types without a source-generated model. For trimmed or Native AOT apps declare [FakeFor<T>] on a partial class and use its generated generators.";

    /// <summary>App-wide custom conventions, e.g. <c>Fake.Conventions.Add&lt;string&gt;("Sku", f =&gt; …)</c>.</summary>
    public static FakeConventions Conventions { get; } = new();

    /// <summary>App-wide subtypes for interfaces and base classes, e.g. <c>Fake.Subtypes.Add&lt;IShape, Circle, Square&gt;()</c>.</summary>
    public static FakeSubtypes Subtypes { get; } = new();

    /// <summary>Generates one populated <typeparamref name="T"/>.</summary>
    /// <example><code>var customer = Fake.One&lt;Customer&gt;();</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static T One<T>() => For<T>().Generate();

    /// <summary>Generates <paramref name="count"/> populated <typeparamref name="T"/> objects.</summary>
    /// <example><code>List&lt;Customer&gt; customers = Fake.Many&lt;Customer&gt;(100);</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Many<T>(int count) => For<T>().Generate(count);

    /// <summary>
    /// Fills in the members of <paramref name="instance"/> that are still unset (or all of them, see <paramref name="mode"/>),
    /// keeping what you already set: <c>Fake.Populate(new Order { CustomerId = 42 })</c>.
    /// </summary>
    /// <returns><paramref name="instance"/>, or a populated copy when <typeparamref name="T"/> is a struct.</returns>
    /// <example><code>var order = Fake.Populate(new Order { CustomerId = 42 });</code></example>
    /// <seealso cref="Generator{T}.Populate(T, PopulateMode)"/>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static T Populate<T>(T instance, PopulateMode mode = PopulateMode.MissingOnly) => For<T>().Populate(instance, mode);

    /// <summary>
    /// Starts configuring a generator for <typeparamref name="T"/>. Uses a source-generated model when one is
    /// registered (see <see cref="FakeForAttribute{T}"/>), otherwise reflection. When the facade is seeded
    /// (<see cref="Seeded"/>, <see cref="Seed"/>), the generator is seeded from it.
    /// </summary>
    /// <example>
    /// <code>
    /// var customers = Fake.For&lt;Customer&gt;()
    ///     .Locale("sv").Seed(42)
    ///     .With(x =&gt; x.Age, f =&gt; f.Number.Int(18, 65))
    ///     .Generate(100);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Generator<T> For<T>() => Configured(new Generator<T>(ModelRegistry.For<T>()));

    /// <summary>
    /// Like <see cref="For{T}"/> for a type with a source-generated model (<see cref="FakeForAttribute{T}"/>), but without any
    /// reflection: trim and Native AOT safe, and seeded from the facade the same way. Throws an
    /// <see cref="InvalidOperationException"/> that says how to fix it when the type has no generated model.
    /// </summary>
    /// <example><code>var order = Fake.Populate(new Order { CustomerId = 42 }); // reflection
    /// var safe = Fake.Generated&lt;Order&gt;().Populate(new Order { CustomerId = 42 }); // AOT safe</code></example>
    public static Generator<T> Generated<T>() => Configured(new Generator<T>(ModelRegistry.Generated<T>()));

    /// <summary>Applies the facade's seed, locale, reference date and clock to a new generator.</summary>
    private static Generator<T> Configured<T>(Generator<T> generator)
    {
        if (!IsSeeded)
            return TimeProvider is { } clock ? generator.TimeProvider(clock) : generator;

        // A seeded facade seeds its generators from its own sequence, so they are reproducible too.
        var faker = Faker;
        return generator.Seed(faker.Random.Long(long.MinValue, long.MaxValue)).ReferenceDate(faker.ReferenceDate).Locale(faker.Locale);
    }

    /// <summary>
    /// Starts a generator for the parameters of <paramref name="method"/>, e.g. to feed a test method. The values of a
    /// row agree with each other like the members of one object (<c>firstName</c> and <c>email</c> describe the same person).
    /// </summary>
    /// <example><code>object?[] args = Fake.Arguments(typeof(OrderTests).GetMethod("Places_order")!).Generate();</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static ArgumentGenerator Arguments(System.Reflection.MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Arguments(method.GetParameters().Select(FakeArgument.From));
    }

    /// <summary>Starts a generator for <paramref name="arguments"/>; seeded from the facade when it is seeded.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static ArgumentGenerator Arguments(IEnumerable<FakeArgument> arguments)
    {
        var generator = new ArgumentGenerator(arguments);
        if (!IsSeeded)
            return TimeProvider is { } clock ? generator.TimeProvider(clock) : generator;

        var faker = Faker;
        return generator.Seed(faker.Random.Long(long.MinValue, long.MaxValue)).ReferenceDate(faker.ReferenceDate).Locale(faker.Locale);
    }
}
