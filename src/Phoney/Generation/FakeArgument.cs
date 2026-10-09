using System.Reflection;

namespace Phoney.Generation;

/// <summary>
/// A value to generate outside of an object, such as a test method parameter: its name and type pick the
/// convention (<c>email</c> → an email address), and its attributes add data annotations (<c>[Range(1, 10)]</c>).
/// </summary>
/// <param name="Name">Name used for conventions, e.g. <c>firstName</c>.</param>
/// <param name="Type">Type of the value.</param>
/// <param name="Nullable">Whether <see langword="null"/> is allowed (nullable reference types); see <see cref="ArgumentGenerator.NullProbability"/>.</param>
/// <param name="Attributes">Where to read data annotations from, typically the <see cref="ParameterInfo"/> or <see cref="PropertyInfo"/>.</param>
public sealed record FakeArgument(string Name, Type Type, bool Nullable = false, ICustomAttributeProvider? Attributes = null)
{
    /// <summary>Describes a method or constructor parameter, including its nullability and annotations.</summary>
    public static FakeArgument From(ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var nullable = new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;
        return new FakeArgument(parameter.Name ?? $"Arg{parameter.Position}", parameter.ParameterType, nullable, parameter);
    }

    /// <summary>Describes a property, including its nullability and annotations.</summary>
    public static FakeArgument From(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var nullable = new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
        return new FakeArgument(property.Name, property.PropertyType, nullable, property);
    }
}
