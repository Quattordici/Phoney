using System.Text.RegularExpressions;

namespace Phony.DataCompiler;

/// <summary>
/// Resolves every <c>{{...}}</c> expression in the locale data the way faker.js does (module methods first,
/// then locale data) and fails on references that cannot be resolved within a locale's fallback chain.
/// </summary>
internal sealed partial class TemplateValidator(IReadOnlyList<RawLocale> locales, IReadOnlyCollection<string> fakerMethods)
{
    private readonly Dictionary<string, RawLocale> _byCode = locales.ToDictionary(l => l.Code, StringComparer.Ordinal);
    private readonly HashSet<string> _methods = [.. fakerMethods];

    /// <summary>faker.js method names referenced by the data, mapped to one example expression each.</summary>
    public SortedDictionary<string, string> MethodReferences { get; } = new(StringComparer.Ordinal);

    /// <summary>Problems found by <see cref="Validate"/>; empty when the data is consistent.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Checks every template expression in every locale against that locale's merged data.</summary>
    public void Validate()
    {
        foreach (var locale in locales)
        {
            var merged = Merge(locale);
            foreach (var leaf in locale.Leaves.Values.Where(l => l.HasTemplates))
            {
                var values = leaf.Strings.Concat(leaf.Rows.SelectMany(r => r).OfType<string>());
                foreach (var value in values)
                    foreach (var expression in Expressions(value))
                        Check(locale.Code, leaf.Path, expression, merged);
            }
        }
    }

    /// <summary>Builds the locale's merged view: each entry group comes whole from the first locale in the chain defining it.</summary>
    public Dictionary<string, LeafEntry> Merge(RawLocale locale)
    {
        var merged = new Dictionary<string, LeafEntry>(StringComparer.Ordinal);
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in locale.Fallback.Prepend(locale.Code))
        {
            var source = _byCode[code];
            foreach (var group in source.Groups)
            {
                if (!seenGroups.Add(group))
                    continue;
                foreach (var leaf in source.Leaves.Values.Where(l => l.Group == group))
                    merged[leaf.Path] = leaf;
            }
        }

        return merged;
    }

    /// <summary>Extracts expressions the way faker.js <c>helpers.fake</c> finds them: <c>{{</c> followed by a lowercase letter, up to the next <c>}}</c>.</summary>
    public static IEnumerable<string> Expressions(string value)
    {
        foreach (Match match in ExpressionRegex().Matches(value))
            yield return match.Groups[1].Value;
    }

    /// <summary>Resolves one expression like faker.js: method call, method name, data path, or record field.</summary>
    private void Check(string code, string path, string expression, Dictionary<string, LeafEntry> merged)
    {
        var parenthesis = expression.IndexOf('(');
        if (parenthesis >= 0)
        {
            var method = expression[..parenthesis];
            if (_methods.Contains(method))
                MethodReferences.TryAdd(method, expression);
            else
                Errors.Add($"{code}: '{path}' calls unknown faker method '{expression}'.");
            return;
        }

        if (_methods.Contains(expression))
        {
            MethodReferences.TryAdd(expression, expression);
            return;
        }

        if (merged.TryGetValue(expression, out var leaf))
        {
            if (leaf.Kind == EntryKind.Unavailable)
                Errors.Add($"{code}: '{path}' references '{expression}', which is explicitly unavailable in this locale.");
            return;
        }

        var lastDot = expression.LastIndexOf('.');
        if (lastDot > 0 && merged.TryGetValue(expression[..lastDot], out var table) && table.Kind == EntryKind.Records
            && table.Fields.Contains(expression[(lastDot + 1)..]))
            return;

        Errors.Add($"{code}: '{path}' references '{expression}', which is neither a faker method nor available locale data.");
    }

    /// <summary><c>{{</c> + lower-case letter … first <c>}}</c>, as faker.js <c>helpers.fake</c> scans.</summary>
    [GeneratedRegex(@"\{\{([a-z].*?)\}\}")]
    private static partial Regex ExpressionRegex();
}
