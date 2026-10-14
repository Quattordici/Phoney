using System.Collections.Concurrent;
using System.Globalization;
using Phoney.Modules;

namespace Phoney.Templates;

/// <summary>A function callable from templates, e.g. <c>{{person.firstName}}</c> or <c>{{string.numeric(4)}}</c>.</summary>
/// <param name="faker">The faker evaluating the template.</param>
/// <param name="args">Arguments given in parentheses; <see cref="TemplateArgs.Count"/> is 0 without parentheses.</param>
public delegate string TemplateFunction(Faker faker, TemplateArgs args);

/// <summary>
/// Registry of the functions templates can call. Names are case-insensitive: write them like the .NET API
/// (<c>{{Person.FirstName}}</c>, <c>{{String.Numeric(4)}}</c>) or like faker.js (<c>{{person.firstName}}</c>), which the
/// imported locale data uses. Register your own to extend templates.
/// </summary>
public static partial class TemplateFunctions
{
    private static readonly ConcurrentDictionary<string, TemplateFunction> Functions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names of all registered functions.</summary>
    public static IReadOnlyCollection<string> Names
    {
        get
        {
            EnsureAllModules();
            return [.. Functions.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Registers (or replaces) a template function, e.g. <c>Register("Shop.Sku", (f, _) => f.Random.ReplaceSymbols("SKU-#####"))</c>; names are case-insensitive.</summary>
    /// <remarks>Register functions before templates that use them are first evaluated; parsed templates are cached.</remarks>
    public static void Register(string name, TemplateFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        if (name.Contains('(') || name.Contains('{'))
            throw new ArgumentException($"'{name}' is not a valid function name.", nameof(name));
        EnsureModuleOf(name); // the built-ins of its module go in first, so they can't replace this one later
        Functions[name] = function;
    }

    /// <summary>Whether a function with this name is registered.</summary>
    public static bool Contains(string name)
    {
        EnsureModuleOf(name);
        return Functions.ContainsKey(name);
    }

    /// <summary>The function registered as <paramref name="name"/>, or <see langword="null"/>.</summary>
    internal static TemplateFunction? Get(string name)
    {
        EnsureModuleOf(name);
        return Functions.TryGetValue(name, out var function) ? function : null;
    }

    /// <summary>faker.js <c>number.int</c> accepts either a max or a <c>{min, max, multipleOf}</c> object.</summary>
    private static long Number(Faker faker, TemplateArgs a, long defaultMin, long defaultMax)
    {
        var min = a.Int(0, "min") ?? defaultMin;
        var max = a.Int(0, "max") ?? a.Int(0) ?? defaultMax;
        return faker.Number.Long(min, max, a.Int(0, "multipleOf") ?? 1);
    }
}
