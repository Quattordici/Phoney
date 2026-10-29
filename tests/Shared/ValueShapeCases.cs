using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using Phoney.Generation;

// Linked into Phoney.Tests (which reads the types through System.Type) and Phoney.SourceGenerator.Tests (which reads the same types through Roslyn symbols),
// so both models are held to one table of how a member type is made.
namespace Phoney.ShapeCases;

public class ShapeClass;

public struct ShapeStruct;

public record ShapeRecord(int Id);

public class ShapeGeneric<T>
{
    public T? Value { get; set; }
}

public interface IShape;

public abstract class ShapeBase;

public delegate void ShapeHandler();

internal static class ValueShapeCases
{
    /// <summary>A member type as C# source, the same type, and how a value of it is made.</summary>
    public static readonly (string Source, Type Type, ValueShape Shape)[] All =
    [
        ("int", typeof(int), ValueShape.Scalar),
        ("string", typeof(string), ValueShape.Scalar),
        ("decimal", typeof(decimal), ValueShape.Scalar),
        ("System.DayOfWeek", typeof(DayOfWeek), ValueShape.Scalar),
        ("System.Guid", typeof(Guid), ValueShape.Scalar),
        ("System.Uri", typeof(Uri), ValueShape.Scalar),
        ("System.Text.Rune", typeof(Rune), ValueShape.Scalar),
        ("byte[]", typeof(byte[]), ValueShape.Bytes),
        ("System.Memory<byte>", typeof(Memory<byte>), ValueShape.Bytes),
        ("System.ReadOnlyMemory<byte>", typeof(ReadOnlyMemory<byte>), ValueShape.Bytes),
        ("System.Memory<int>", typeof(Memory<int>), ValueShape.Framework),
        ("(int, string)", typeof(ValueTuple<int, string>), ValueShape.Composite),
        ("System.Tuple<int, string>", typeof(Tuple<int, string>), ValueShape.Composite),
        ("System.Collections.Generic.KeyValuePair<string, int>", typeof(KeyValuePair<string, int>), ValueShape.Composite),
        ("int[]", typeof(int[]), ValueShape.Array),
        ("global::Phoney.ShapeCases.ShapeClass[]", typeof(ShapeClass[]), ValueShape.Array),
        ("int[,]", typeof(int[,]), ValueShape.Grid),
        ("int[,,]", typeof(int[,,]), ValueShape.None),
        ("System.Collections.Generic.List<int>", typeof(List<int>), ValueShape.Collection),
        ("System.Collections.Generic.HashSet<string>", typeof(HashSet<string>), ValueShape.Collection),
        ("System.Collections.Generic.Dictionary<string, int>", typeof(Dictionary<string, int>), ValueShape.Collection),
        ("System.Collections.Generic.IReadOnlyList<int>", typeof(IReadOnlyList<int>), ValueShape.Collection),
        ("System.Collections.Generic.IEnumerable<int>", typeof(IEnumerable<int>), ValueShape.Collection),
        ("System.Collections.Immutable.ImmutableArray<int>", typeof(ImmutableArray<int>), ValueShape.Collection),
        ("System.Collections.Frozen.FrozenSet<int>", typeof(FrozenSet<int>), ValueShape.Collection),
        ("System.Collections.ObjectModel.ReadOnlyCollection<int>", typeof(ReadOnlyCollection<int>), ValueShape.Collection),
        ("object", typeof(object), ValueShape.Framework),
        ("System.Text.StringBuilder", typeof(StringBuilder), ValueShape.Framework),
        ("System.Threading.CancellationToken", typeof(CancellationToken), ValueShape.Framework),
        ("System.IDisposable", typeof(IDisposable), ValueShape.Framework),
        ("System.Action", typeof(Action), ValueShape.Framework),
        ("global::Phoney.ShapeCases.ShapeClass", typeof(ShapeClass), ValueShape.Object),
        ("global::Phoney.ShapeCases.ShapeStruct", typeof(ShapeStruct), ValueShape.Object),
        ("global::Phoney.ShapeCases.ShapeRecord", typeof(ShapeRecord), ValueShape.Object),
        ("global::Phoney.ShapeCases.ShapeGeneric<int>", typeof(ShapeGeneric<int>), ValueShape.Object),
        ("global::Phoney.ShapeCases.ShapeGeneric<global::Phoney.ShapeCases.ShapeClass>", typeof(ShapeGeneric<ShapeClass>), ValueShape.Object),
        ("global::Phoney.ShapeCases.IShape", typeof(IShape), ValueShape.Polymorphic),
        ("global::Phoney.ShapeCases.ShapeBase", typeof(ShapeBase), ValueShape.Polymorphic),
        ("global::Phoney.ShapeCases.ShapeHandler", typeof(ShapeHandler), ValueShape.None),
    ];
}
