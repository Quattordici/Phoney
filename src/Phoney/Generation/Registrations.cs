using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>
/// The app-wide registrations (<see cref="FakeConventions"/>, <see cref="FakeSubtypes"/>, <see cref="FakeGenerators"/>) at one moment. Read it once and build everything
/// from it, so a registration made in the middle never ends up in half of a generator. Compared by reference: the snapshots are immutable, so a changed registry
/// is a different snapshot, which is how compiled rules know they are stale.
/// </summary>
internal readonly record struct Registrations(
    ImmutableList<CustomConvention> Conventions,
    ImmutableDictionary<Type, ImmutableArray<Type>> Subtypes,
    ImmutableDictionary<Type, INestedGenerator> Generators)
{
    /// <summary>The registrations right now.</summary>
    public static Registrations Current => new(FakeConventions.All, FakeSubtypes.All, FakeGenerators.All);

    /// <summary>A generator's own conventions in front of the app-wide ones.</summary>
    public ImmutableList<CustomConvention> ConventionsWith(ImmutableList<CustomConvention> own) => own.AddRange(Conventions);

    /// <summary>The app-wide subtypes with a generator's own on top.</summary>
    public ImmutableDictionary<Type, ImmutableArray<Type>> SubtypesWith(ImmutableDictionary<Type, ImmutableArray<Type>> own) => Subtypes.SetItems(own);

    /// <summary>The app-wide type generators with a generator's own on top, or <see langword="null"/> when there are none.</summary>
    public ImmutableDictionary<Type, INestedGenerator>? GeneratorsWith(ImmutableDictionary<Type, INestedGenerator> own)
    {
        var all = Generators.SetItems(own);
        return all.IsEmpty ? null : all;
    }

    /// <summary>The same registrations compared by reference, which is the only way a snapshot can have changed.</summary>
    public bool IsSameAs(Registrations other) =>
        ReferenceEquals(Conventions, other.Conventions) && ReferenceEquals(Subtypes, other.Subtypes) && ReferenceEquals(Generators, other.Generators);
}
