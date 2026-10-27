using System.Reflection;
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

    /// <summary>App-wide generators for types, e.g. <c>Fake.Generators.Add(Fake.For&lt;Address&gt;().Locale("sv"))</c>: every nested <c>Address</c> is created by it.</summary>
    public static FakeGenerators Generators { get; } = new();

    /// <summary>Generates one populated <typeparamref name="T"/>.</summary>
    /// <example><code>var customer = Fake.One&lt;Customer&gt;();</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static T One<T>() => For<T>().Generate();

    /// <summary>
    /// Creates one object of a type that is only known at runtime, like <see cref="One{T}"/>: <c>Fake.One(typeof(Customer))</c>. Useful for tools, test helpers
    /// and code that works from <see cref="Type"/> values. Prefer the generic overload when the type is known at compile time.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="type"/> is an open generic type.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static object One(Type type) => Invoke(nameof(One), type, null)!;

    /// <summary>Generates <paramref name="count"/> populated <typeparamref name="T"/> objects.</summary>
    /// <example><code>List&lt;Customer&gt; customers = Fake.Many&lt;Customer&gt;(100);</code></example>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Many<T>(int count) => For<T>().Generate(count);

    /// <summary>Generates between <paramref name="min"/> and <paramref name="max"/> populated <typeparamref name="T"/> objects (both included); see <see cref="ObjectGenerator{T}.Generate(int, int)"/>.</summary>
    /// <example><code>List&lt;Order&gt; orders = Fake.Many&lt;Order&gt;(3, 10);</code></example>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative or <paramref name="max"/> is less than <paramref name="min"/>.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<T> Many<T>(int min, int max) => For<T>().Generate(min, max);

    /// <summary>Creates <paramref name="count"/> objects of a type that is only known at runtime, like <see cref="Many{T}(int)"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="type"/> is an open generic type.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static List<object> Many(Type type, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var list = (System.Collections.IList)Invoke(nameof(Many), type, [count])!;
        var result = new List<object>(list.Count);
        foreach (var item in list)
            result.Add(item);
        return result;
    }

    /// <summary>Calls the generic method <paramref name="name"/> (<c>One&lt;T&gt;</c> or <c>Many&lt;T&gt;</c>) with <paramref name="type"/>, without wrapping its exceptions.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    private static object? Invoke(string name, Type type, object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.ContainsGenericParameters)
            throw new ArgumentException($"{type.Name} is an open generic type: pass a closed one such as List<string>.", nameof(type));
        var method = typeof(Fake).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == (arguments?.Length ?? 0));
        return method.MakeGenericMethod(type).Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null);
    }

    /// <summary>
    /// Fills in the members of <paramref name="instance"/> that are still unset (or all of them, see <paramref name="mode"/>),
    /// keeping what you already set: <c>Fake.Populate(new Order { CustomerId = 42 })</c>.
    /// </summary>
    /// <returns><paramref name="instance"/>, or a populated copy when <typeparamref name="T"/> is a struct.</returns>
    /// <example><code>var order = Fake.Populate(new Order { CustomerId = 42 });</code></example>
    /// <seealso cref="ObjectGenerator{T}.Populate(T, PopulateMode)"/>
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
    public static ObjectGenerator<T> For<T>() => Configured(Plain<T>.Get(ModelRegistry.For<T>()));

    /// <summary>
    /// Like <see cref="For{T}"/> for a type with a source-generated model (<see cref="FakeForAttribute{T}"/>), but without any
    /// reflection: trim and Native AOT safe, and seeded from the facade the same way. Throws an
    /// <see cref="InvalidOperationException"/> that says how to fix it when the type has no generated model.
    /// </summary>
    /// <example><code>var order = Fake.Populate(new Order { CustomerId = 42 }); // reflection
    /// var safe = Fake.ForGenerated&lt;Order&gt;().Populate(new Order { CustomerId = 42 }); // AOT safe</code></example>
    public static ObjectGenerator<T> ForGenerated<T>() => Configured(Plain<T>.Get(ModelRegistry.Generated<T>()));

    /// <summary>A new unconfigured generator per call that reuses the rules compiled for the type while the model and the app-wide registrations stay the same.</summary>
    private static class Plain<T>
    {
        private sealed record Entry(FakeModel<T> Model, Registrations Registrations, ObjectGenerator<T> Prototype);

#pragma warning disable S2743 // one per T on purpose: each closed type keeps its own prototype, read without a lookup
        private static Entry? s_entry;
#pragma warning restore S2743

        public static ObjectGenerator<T> Get(FakeModel<T> model)
        {
            var registrations = Registrations.Current;
            var entry = Volatile.Read(ref s_entry);
            if (entry is null || !ReferenceEquals(entry.Model, model) || !entry.Registrations.IsSameAs(registrations))
            {
                entry = new Entry(model, registrations, new ObjectGenerator<T>(model));
                Volatile.Write(ref s_entry, entry);
            }

            return entry.Prototype.Fresh();
        }
    }

    /// <summary>Applies the facade's seed, locale, reference date and clock to a new generator.</summary>
    private static ObjectGenerator<T> Configured<T>(ObjectGenerator<T> generator) =>
        Facade() switch
        {
            { Seed: { } seed } facade => generator.Seed(seed).ReferenceDate(facade.ReferenceDate).Locale(facade.Locale!),
            { Clock: { } clock } => generator.TimeProvider(clock),
            _ => generator,
        };

    /// <summary>
    /// What a new generator takes from the facade: when it is seeded a seed from its own sequence (so the generator is reproducible too, and this is the
    /// only draw it costs), its reference date and locale; otherwise only its clock.
    /// </summary>
    private static FacadeSettings Facade()
    {
        if (!IsSeeded)
            return new FacadeSettings(null, default, null, TimeProvider);

        var faker = Faker;
        return new FacadeSettings(faker.Random.Long(long.MinValue, long.MaxValue), faker.ReferenceDate, faker.Locale, null);
    }

    private readonly record struct FacadeSettings(long? Seed, DateTimeOffset ReferenceDate, string? Locale, TimeProvider? Clock);

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
        return Facade() switch
        {
            { Seed: { } seed } facade => generator.Seed(seed).ReferenceDate(facade.ReferenceDate).Locale(facade.Locale!),
            { Clock: { } clock } => generator.TimeProvider(clock),
            _ => generator,
        };
    }
}
