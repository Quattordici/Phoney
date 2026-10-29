namespace Phoney.Generation;

internal static partial class ValueShapes
{
    /// <summary>The shape of a non-nullable <paramref name="type"/>, read from the runtime type as the source generator reads it from a symbol.</summary>
    public static ValueShape Of(Type type)
    {
        if (type.IsArray)
            return Of(new TypeFacts("", "", TypeForm.Array, 0, type.GetArrayRank(), type.GetElementType() == typeof(byte), false));

        var arguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return Of(new TypeFacts(definition.FullName ?? "", type.Namespace ?? "", FormOf(type), arguments.Length, 0, arguments.Length > 0 && arguments[0] == typeof(byte), type.ContainsGenericParameters));
    }

    private static TypeForm FormOf(Type type) => type switch
    {
        { IsEnum: true } => TypeForm.Enum,
        { IsInterface: true } => TypeForm.Interface,
        { IsValueType: true } => TypeForm.Struct,
        { IsClass: true } when type.IsSubclassOf(typeof(Delegate)) => TypeForm.Other,
        { IsClass: true, IsAbstract: true } => TypeForm.Abstract,
        { IsClass: true } => TypeForm.Class,
        _ => TypeForm.Other,
    };
}
