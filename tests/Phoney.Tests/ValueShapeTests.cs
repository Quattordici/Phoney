using Phoney.Generation;
using Phoney.ShapeCases;

namespace Phoney.Tests;

/// <summary>
/// How the reflection model makes a value of each kind of member type. The source generator's tests run the same table (<c>ValueShapeCases</c>) through Roslyn
/// symbols, so a type both models don't make the same way fails one of them.
/// </summary>
public sealed class ValueShapeTests
{
    public static IEnumerable<Func<int>> Rows() => Enumerable.Range(0, ValueShapeCases.All.Length).Select(i => (Func<int>)(() => i));

    [Test]
    [MethodDataSource(nameof(Rows))]
    public void A_member_type_has_the_shape_both_models_give_it(int row)
    {
        var (source, type, shape) = ValueShapeCases.All[row];

        ValueShapes.Of(type).ShouldBe(shape, source);
    }

    [Test]
    [Arguments("NodaTime.Instant", "NodaTime")]
    [Arguments("NodaTime.LocalDate", "NodaTime")]
    public void Date_types_of_add_ons_are_adapted(string name, string ns) =>
        ValueShapes.Of(new TypeFacts(name, ns, TypeForm.Struct, 0, 0, false, false)).ShouldBe(ValueShape.Adapted);

    [Test]
    public void Only_objects_and_subtypes_are_nested() =>
        Enum.GetValues<ValueShape>().Where(ValueShapes.IsNested).ShouldBe([ValueShape.Object, ValueShape.Polymorphic]);
}
