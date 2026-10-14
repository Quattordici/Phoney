namespace Phoney;

/// <summary>
/// A generated object that breaks, or sits exactly on, one limit of one member, as listed by
/// <see cref="ObjectGenerator{T}.InvalidCases"/> and <see cref="ObjectGenerator{T}.BoundaryCases"/>. Every other member holds the value
/// the generator gives a valid object for the same seed.
/// </summary>
/// <typeparam name="T">The generated type.</typeparam>
/// <param name="Member">The member whose limit the case is about.</param>
/// <param name="Description">What the value is, e.g. <c>above the maximum (10)</c>.</param>
/// <param name="Value">The object.</param>
public sealed record ConstraintCase<T>(string Member, string Description, T Value)
{
    /// <summary>The member and what is special about its value, e.g. <c>Guests: above the maximum (10)</c>; handy as a test case name.</summary>
    public override string ToString() => $"{Member}: {Description}";
}
