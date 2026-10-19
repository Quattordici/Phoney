using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>Members of whole-number types smaller than the default range of 1 to 1000.</summary>
public sealed class SmallNumbers
{
    public byte Alpha { get; set; }
    public sbyte Beta { get; set; }
    public short Gamma { get; set; }
}

[FakeFor<SmallNumbers>]
public static partial class SmallNumberFakes;

public sealed class SmallNumberTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Bytes_spread_over_their_range_instead_of_piling_up_at_the_largest_value(bool generated)
    {
        var values = (generated ? SmallNumberFakes.SmallNumbers : new ObjectGenerator<SmallNumbers>(new ReflectionModel<SmallNumbers>())).Seed(2).Generate(2000);

        values.Count(v => v.Alpha == byte.MaxValue).ShouldBeLessThan(40); // 1 in 255, not 3 in 4
        values.Count(v => v.Beta == sbyte.MaxValue).ShouldBeLessThan(40);
        values.ShouldAllBe(v => v.Alpha >= 1 && v.Beta >= 1);
        values.Select(v => v.Alpha).Distinct().Count().ShouldBeGreaterThan(200);
        values.Max(v => v.Gamma).ShouldBeGreaterThan((short)900); // types that hold 1000 keep the old range
    }
}
