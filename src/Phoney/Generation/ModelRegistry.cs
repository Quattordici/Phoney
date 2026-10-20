using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Phoney.Generation;

/// <summary>
/// Models by type. Source-generated models register themselves at startup (module initializer) and take
/// precedence; other types get a reflection model on first use where dynamic code is available.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ModelRegistry
{
    private static readonly ConcurrentDictionary<Type, IFakeModel> Models = new();

    /// <summary>Registers a (typically source-generated) model; replaces any earlier model for the type.</summary>
    public static void Register<T>(FakeModel<T> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Models[typeof(T)] = model;
    }

    /// <summary>Whether a model has been registered or built for <typeparamref name="T"/>.</summary>
    public static bool IsRegistered<T>() => Models.ContainsKey(typeof(T));

    /// <summary>
    /// Returns the registered source-generated model for <typeparamref name="T"/> without any reflection, so it is safe in trimmed
    /// and Native AOT apps; throws when the type has none.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <c>[FakeFor&lt;T&gt;]</c> model is registered for the type.</exception>
    public static FakeModel<T> Generated<T>() =>
        Models.TryGetValue(typeof(T), out var model) && model is FakeModel<T> typed and not ReflectionModel<T>
            ? typed
            : throw new InvalidOperationException(
                $"No source-generated model for '{typeof(T).FullName}'. Add [FakeFor<{typeof(T).Name}>] to a partial class so Phoney can generate it without reflection, or use Fake.For<{typeof(T).Name}>() where reflection is available.");

    /// <summary>Returns the model for <typeparamref name="T"/>, building a reflection model when none is registered.</summary>
    [RequiresUnreferencedCode("Types without a source-generated model are populated with reflection. Add [FakeFor<T>] to make generation trim and AOT safe.")]
    [RequiresDynamicCode("Types without a source-generated model are populated with reflection. Add [FakeFor<T>] to make generation trim and AOT safe.")]
    public static FakeModel<T> For<T>()
    {
        // A type first seen as a nested object has an untyped reflection model; upgrade it to a typed one.
        var model = Models.GetOrAdd(typeof(T), static _ => new ReflectionModel<T>());
        if (model is FakeModel<T> typed)
            return typed;
        var upgraded = new ReflectionModel<T>();
        Models[typeof(T)] = upgraded;
        return upgraded;
    }

    /// <summary>
    /// Records the subtypes an interface or abstract class lists with <c>[JsonDerivedType]</c>, so they are its default
    /// subtypes without reflection (called by source-generated code).
    /// </summary>
    public static void RegisterDerivedTypes(Type baseType, params Type[] derivedTypes)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        ArgumentNullException.ThrowIfNull(derivedTypes);
        Subtypes.RegisterDefaults(baseType, derivedTypes);
    }

    /// <summary>Whether <see cref="Get"/> can return a model for <paramref name="type"/> without failing: one is registered, or reflection is available.</summary>
    internal static bool CanGet(Type type) => Models.ContainsKey(type) || RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>Returns the model for a nested type, or <see langword="null"/> when the type cannot be created (interfaces, abstract types).</summary>
    internal static IFakeModel? Get(Type type)
    {
        if (Models.TryGetValue(type, out var model))
            return model;
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new InvalidOperationException(
                $"No source-generated model for '{type.FullName}'. In trimmed or Native AOT apps every generated type needs [FakeFor<{type.Name}>].");
        }

        return BuildReflectionModel(type);
    }

    /// <summary>Builds (and caches) a reflection model, or <see langword="null"/> for types that cannot be instantiated.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only reached when dynamic code is supported (checked above); AOT apps use generated models.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Only reached when dynamic code is supported (checked above); AOT apps use generated models.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only reached when dynamic code is supported (checked above); AOT apps use generated models.")]
    private static IFakeModel? BuildReflectionModel(Type type)
    {
        if (type.IsInterface || type.IsAbstract || type == typeof(object) || type.IsPointer || type.IsByRef || type.ContainsGenericParameters)
            return null;
        return Models.GetOrAdd(type, static t => ReflectionTypeModel.Get(t));
    }
}
