using System.Collections.Immutable;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The snapshot of the app-wide registrations a generator is built from.</summary>
public sealed class RegistrationsTests
{
    private static Registrations Empty() =>
        new(ImmutableList<CustomConvention>.Empty, ImmutableDictionary<Type, ImmutableArray<Type>>.Empty, ImmutableDictionary<Type, INestedGenerator>.Empty);

    [Test]
    public void A_snapshot_is_the_same_only_while_nothing_was_registered()
    {
        var snapshot = Empty();

        snapshot.IsSameAs(snapshot with { }).ShouldBeTrue();
        snapshot.IsSameAs(snapshot with { Subtypes = snapshot.Subtypes.Add(typeof(IDisposable), [typeof(MemoryStream)]) }).ShouldBeFalse();
    }

    [Test]
    public void A_generators_own_subtypes_win_over_the_app_wide_ones()
    {
        var snapshot = Empty() with { Subtypes = ImmutableDictionary<Type, ImmutableArray<Type>>.Empty.Add(typeof(IDisposable), [typeof(MemoryStream)]) };
        var own = ImmutableDictionary<Type, ImmutableArray<Type>>.Empty.Add(typeof(IDisposable), [typeof(StringReader)]);

        snapshot.SubtypesWith(own)[typeof(IDisposable)].ShouldBe([typeof(StringReader)]);
        snapshot.SubtypesWith(ImmutableDictionary<Type, ImmutableArray<Type>>.Empty)[typeof(IDisposable)].ShouldBe([typeof(MemoryStream)]);
    }

    [Test]
    public void No_type_generators_at_all_is_null_so_nothing_is_carried_around()
    {
        Empty().GeneratorsWith(ImmutableDictionary<Type, INestedGenerator>.Empty).ShouldBeNull();
    }
}
