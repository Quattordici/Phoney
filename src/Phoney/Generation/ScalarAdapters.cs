using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Phoney.Generation;

/// <summary>
/// Value producers for date and time types of other libraries (see <see cref="ConventionRules.AdaptedCategoryOf"/>),
/// registered by add-on packages such as <c>Phoney.NodaTime</c>. Used by the reflection model; source-generated models
/// call the add-on directly.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ScalarAdapters
{
    private static readonly ConcurrentDictionary<Type, Func<FakeScope, ConventionKind, MemberConstraints?, object>> Adapters = new();
    // Thread-safe: concurrent callers wait until the add-ons have registered, so no model caches a missing adapter.
    private static readonly Lazy<bool> AddOnsLoaded = new(LoadAddOns);

    /// <summary>Registers how values of <typeparamref name="T"/> are produced from a member's convention and annotations.</summary>
    public static void Register<T>(Func<FakeScope, ConventionKind, MemberConstraints?, T> produce)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(produce);
        Adapters[typeof(T)] = (s, k, c) => produce(s, k, c);
    }

    /// <summary>Whether <paramref name="type"/> (or its nullable form) is produced by a registered adapter.</summary>
    internal static bool IsAdapted(Type type) => TryGet(Nullable.GetUnderlyingType(type) ?? type, out _);

    /// <summary>The producer for an adapted type; loads a known add-on package the first time one of its types is seen.</summary>
    internal static bool TryGet(Type type, [NotNullWhen(true)] out Func<FakeScope, ConventionKind, MemberConstraints?, object>? produce)
    {
        if (Adapters.TryGetValue(type, out produce))
            return true;
        if (ConventionRules.AdaptedCategoryOf(type.FullName ?? "") == TypeCategory.Other)
            return false;
        _ = AddOnsLoaded.Value;
        return Adapters.TryGetValue(type, out produce);
    }

    /// <summary>Runs the registration of add-on packages that are deployed with the app but not loaded yet.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only add-ons that are present are loaded; their registration types are kept by the module initializer.")]
    [UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Only add-ons that are present are loaded.")]
    private static bool LoadAddOns()
    {
        try
        {
            if (Type.GetType("Phoney.NodaTime.NodaTimeValues, Phoney.NodaTime", throwOnError: false) is { } values)
                RuntimeHelpers.RunClassConstructor(values.TypeHandle);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or TypeLoadException)
        {
            // The add-on is not deployed: the types keep their previous behavior.
        }

        return true;
    }
}
