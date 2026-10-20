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

    /// <summary>The names <see cref="Register"/> was called with: those functions read their arguments as they like, built-ins must use every option given.</summary>
    private static readonly ConcurrentDictionary<string, bool> UserFunctions = new(StringComparer.OrdinalIgnoreCase);

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
        UserFunctions[name] = true;
        Functions[name] = function;
    }

    /// <summary>Whether the function named <paramref name="name"/> was registered with <see cref="Register"/> rather than built in.</summary>
    internal static bool IsRegisteredByUser(string name) => UserFunctions.ContainsKey(name);

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
        var (min, max) = a.IntRange();
        return faker.Number.Long(min ?? defaultMin, max ?? defaultMax, a.IsOptions(0) ? a.Int(0, "multipleOf") ?? 1 : 1);
    }
}

/// <summary>A built-in template function that writes its text into the buffer of the template being evaluated.</summary>
internal delegate void TemplateWriter(Faker faker, TemplateArgs args, ref TextBuilder text);

/// <summary>
/// Pairs a <see cref="TemplateWriter"/> with the <see cref="TemplateFunction"/> registered for it, so a template can find the writer behind the
/// function (and a function someone registers in its place has none).
/// </summary>
internal sealed class WritingFunction
{
    private readonly TemplateWriter _write;

    private WritingFunction(TemplateWriter write) => _write = write;

    /// <summary>A template function that returns what <paramref name="write"/> writes.</summary>
    public static TemplateFunction Of(TemplateWriter write) => new WritingFunction(write).Evaluate;

    /// <summary>The writer behind <paramref name="function"/>, or <see langword="null"/> when it is an ordinary function.</summary>
    public static TemplateWriter? WriterOf(TemplateFunction function) => function.Target is WritingFunction writing ? writing._write : null;

    private string Evaluate(Faker faker, TemplateArgs args)
    {
        var text = new TextBuilder(stackalloc char[128]);
        _write(faker, args, ref text);
        return text.ToStringAndDispose();
    }
}
