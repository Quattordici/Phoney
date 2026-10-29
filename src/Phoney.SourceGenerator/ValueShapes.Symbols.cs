using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Phoney.SourceGenerator;

namespace Phoney.Generation;

internal static partial class ValueShapes
{
    /// <summary>The shape of a non-nullable <paramref name="type"/>, read from the symbol as the reflection model reads it from the runtime type.</summary>
    public static ValueShape Of(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return Of(new TypeFacts("", "", TypeForm.Array, 0, array.Rank, array.ElementType.SpecialType == SpecialType.System_Byte, false));

        var named = type as INamedTypeSymbol;
        var arguments = named?.TypeArguments ?? ImmutableArray<ITypeSymbol>.Empty;
        var definition = named?.OriginalDefinition ?? type;
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return Of(new TypeFacts(
            ns + "." + definition.MetadataName, ns, FormOf(type), arguments.Length, 0, arguments.Length > 0 && arguments[0].SpecialType == SpecialType.System_Byte,
            ModelBuilder.ContainsTypeParameter(type)));
    }

    private static TypeForm FormOf(ITypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Enum => TypeForm.Enum,
        TypeKind.Interface => TypeForm.Interface,
        TypeKind.Struct => TypeForm.Struct,
        TypeKind.Class => type.IsAbstract ? TypeForm.Abstract : TypeForm.Class,
        _ => TypeForm.Other,
    };
}
