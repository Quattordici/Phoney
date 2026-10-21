using System.Reflection;
using Phoney.Generation;

namespace Phoney.Tests;

// Types only these tests use: Fake.Subtypes is process-wide, so no other test may generate them while it is changed.
public interface IMedal;

public sealed class GoldMedal : IMedal
{
    public int Carat { get; set; }
}

public sealed class SilverMedal : IMedal
{
    public string Engraving { get; set; } = "";
}

public sealed class BronzeMedal : IMedal
{
    public bool Polished { get; set; }
}

public sealed class TinMedal : IMedal
{
    public double Weight { get; set; }
}

public sealed class Lanyard
{
    public IMedal? Medal { get; set; }
    public string LanyardColor { get; set; } = "";
}

/// <summary>The multi-type overloads and <c>Clear</c> of the app-wide registries, and the matching generator overloads.</summary>
[NotInParallel]
public sealed class AppWideRegistryTests
{
    private static readonly Type[] AllMedals = [typeof(BronzeMedal), typeof(GoldMedal), typeof(SilverMedal), typeof(TinMedal)];

    private static Type[] MedalTypes(ObjectGenerator<Lanyard> generator) =>
        [.. generator.Seed(3).Generate(200).Select(l => l.Medal!.GetType()).Distinct().OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>Runs <paramref name="test"/> and then puts back the registrations of <typeparamref name="TRegistry"/> it had, also those of other tests and module initializers.</summary>
    private static void Restoring<TRegistry>(Action test)
    {
        var field = typeof(TRegistry).GetField("s_all", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = field.GetValue(null);
        try
        {
            test();
        }
        finally
        {
            field.SetValue(null, saved);
        }
    }

    [Test]
    public void Two_three_and_four_app_wide_subtypes_are_all_generated() =>
        Restoring<FakeSubtypes>(() =>
        {
            Fake.Subtypes.Add<IMedal, GoldMedal, SilverMedal>();
            MedalTypes(Fake.For<Lanyard>()).ShouldBe([typeof(GoldMedal), typeof(SilverMedal)]);

            Fake.Subtypes.Remove<IMedal>();
            Fake.Subtypes.Add<IMedal, GoldMedal, SilverMedal, BronzeMedal>();
            MedalTypes(Fake.For<Lanyard>()).ShouldBe([typeof(BronzeMedal), typeof(GoldMedal), typeof(SilverMedal)]);

            Fake.Subtypes.Remove<IMedal>();
            Fake.Subtypes.Add<IMedal, GoldMedal, SilverMedal, BronzeMedal, TinMedal>();
            MedalTypes(Fake.For<Lanyard>()).ShouldBe(AllMedals);
        });

    [Test]
    public void The_order_of_app_wide_subtypes_does_not_change_the_data() =>
        Restoring<FakeSubtypes>(() =>
        {
            Fake.Subtypes.Add<IMedal, TinMedal, BronzeMedal, SilverMedal, GoldMedal>();
            var first = Fake.For<Lanyard>().Seed(9).Generate(50).Select(l => l.Medal!.GetType()).ToList();

            Fake.Subtypes.Remove<IMedal>();
            Fake.Subtypes.Add<IMedal, GoldMedal, SilverMedal, BronzeMedal, TinMedal>();

            Fake.For<Lanyard>().Seed(9).Generate(50).Select(l => l.Medal!.GetType()).ShouldBe(first);
        });

    [Test]
    public void Clearing_the_subtypes_affects_generators_compiled_afterwards() =>
        Restoring<FakeSubtypes>(() =>
        {
            Fake.Subtypes.Add<IMedal, GoldMedal>();
            Fake.One<Lanyard>().Medal.ShouldBeOfType<GoldMedal>();

            Fake.Subtypes.Clear();

            Fake.One<Lanyard>().Medal.ShouldBeNull(); // the facade's cached generator is compiled again
            Fake.For<Lanyard>().Generate().Medal.ShouldBeNull();
        });

    [Test]
    public void Three_and_four_generator_subtypes_are_all_generated()
    {
        MedalTypes(Fake.For<Lanyard>().Subtype<IMedal, GoldMedal, SilverMedal, BronzeMedal>()).ShouldBe([typeof(BronzeMedal), typeof(GoldMedal), typeof(SilverMedal)]);
        MedalTypes(Fake.For<Lanyard>().Subtype<IMedal, GoldMedal, SilverMedal, BronzeMedal, TinMedal>()).ShouldBe(AllMedals);
    }

    [Test]
    public void Clearing_the_conventions_affects_generators_compiled_afterwards() =>
        Restoring<FakeConventions>(() =>
        {
            Fake.Conventions.Add<string>("LanyardColor", _ => "teal");
            Fake.One<Lanyard>().LanyardColor.ShouldBe("teal");

            Fake.Conventions.Clear();

            Fake.One<Lanyard>().LanyardColor.ShouldNotBe("teal");
            Fake.For<Lanyard>().Generate().LanyardColor.ShouldNotBe("teal");
        });
}
