using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>The exact element count an Invalid or Boundary case gives the member's own collection, and only that one.</summary>
public sealed class ForcedCountTests
{
    private static FakeScope Scope(int seed) => new(new Faker(seed: seed), GenerationSettings.Default, null, 0, typeof(object));

    [Test]
    public void The_next_collection_gets_the_forced_count_without_a_draw()
    {
        var scope = Scope(7);

        using (scope.ForceCount(9))
            scope.CollectionCount().ShouldBe(9);

        scope.CollectionCount().ShouldBe(Scope(7).CollectionCount());
    }

    [Test]
    public void A_count_nobody_asked_for_ends_with_its_scope()
    {
        var scope = Scope(7);

        using (scope.ForceCount(9))
        {
            // the member stayed null and never built its collection
        }

        scope.CollectionCount().ShouldBe(Scope(7).CollectionCount());
    }
}
