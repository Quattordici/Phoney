using Phoney.Generation;
using TUnit.Core;

namespace Phoney.TUnit;

/// <summary>The parameters and properties TUnit asks the attributes to fill, as the arguments Phoney generates.</summary>
internal static class TestArguments
{
    /// <summary>
    /// Describes a parameter or property TUnit asks for, preferring its reflection info (which carries data
    /// annotations and nullability) and falling back to the type and name from TUnit's metadata.
    /// </summary>
    public static FakeArgument From(IMemberMetadata member) => member switch
    {
        ParameterMetadata parameter => TryReflectionInfo(() => parameter.ReflectionInfo) is { } info
            ? FakeArgument.From(info)
            : new FakeArgument(parameter.Name, parameter.Type, parameter.IsNullable),
        PropertyMetadata property => TryReflectionInfo(() => property.ReflectionInfo) is { } info
            ? FakeArgument.From(info)
            : new FakeArgument(property.Name, property.Type, property.IsNullable),
        _ => throw new NotSupportedException($"Phoney can't generate '{member.Name}' ({member.GetType().Name})."),
    };

    /// <summary>Reflection info can be unavailable (or fail to resolve) in TUnit's source-generated mode.</summary>
    private static T? TryReflectionInfo<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or MissingMemberException)
        {
            return null;
        }
    }
}
