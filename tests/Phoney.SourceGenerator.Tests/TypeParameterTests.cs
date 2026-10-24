using Microsoft.CodeAnalysis;

namespace Phoney.SourceGenerator.Tests;

/// <summary>Which types refer to a type parameter, so that no model is written for them: the one answer used for members and for type-argument roots.</summary>
public sealed class TypeParameterTests
{
    private const string Source = """
        using System.Collections.Generic;
        public class Outer<T>
        {
            public class Inner { }
            public T Parameter;
            public Inner InOuter;
            public T[] Array;
            public List<T[]> ListOfArrays;
            public Dictionary<string, List<T>> Deep;
            public int Plain;
            public int[] PlainArray;
            public List<string> Closed;
        }
        public class Holder
        {
            public Outer<int> Constructed;
            public Outer<int>.Inner ConstructedInner;
        }
        """;

    private static ITypeSymbol MemberType(string type, string member) =>
        GeneratorHarness.Compile(Source).GetTypeByMetadataName(type)!.GetMembers(member).OfType<IFieldSymbol>().Single().Type;

    [Test]
    [Arguments("Parameter")]
    [Arguments("InOuter")]
    [Arguments("Array")]
    [Arguments("ListOfArrays")]
    [Arguments("Deep")]
    public void A_type_that_reaches_a_type_parameter_through_anything_contains_one(string member) =>
        ModelBuilder.ContainsTypeParameter(MemberType("Outer`1", member)).ShouldBeTrue(member);

    [Test]
    [Arguments("Outer`1", "Plain")]
    [Arguments("Outer`1", "PlainArray")]
    [Arguments("Outer`1", "Closed")]
    [Arguments("Holder", "Constructed")]
    [Arguments("Holder", "ConstructedInner")]
    public void A_closed_type_contains_none(string owner, string member) =>
        ModelBuilder.ContainsTypeParameter(MemberType(owner, member)).ShouldBeFalse(member);
}
