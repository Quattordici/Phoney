using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Phoney.Generation;

/// <summary>
/// Describes an object on one line, the way a record prints itself (<c>Basket { Discount = 0, Lines = [BasketLine { Sku = "A" }] }</c>), for the
/// counterexample of a failed check. Members are read through the models of the objects' runtime types, so reflection and generated models agree and
/// nothing beyond them is needed in trimmed and Native AOT apps.
/// </summary>
internal static class ObjectDescription
{
    private const int MaxDepth = 4;
    private const int MaxElements = 10;
    private const int MaxText = 80;

    /// <summary>The description of <paramref name="value"/>.</summary>
    public static string Of(object? value)
    {
        var text = new StringBuilder();
        Append(text, value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return text.ToString();
    }

    private static void Append(StringBuilder text, object? value, int depth, HashSet<object> path)
    {
        switch (value)
        {
            case null:
                text.Append("null");
                return;
            case string s:
                AppendQuoted(text, s);
                return;
            case char c:
                text.Append('\'').Append(c).Append('\'');
                return;
            case bool b:
                text.Append(b ? "true" : "false");
                return;
            case IFormattable date when value is DateTime or DateTimeOffset or DateOnly or TimeOnly: // ISO 8601 rather than the invariant culture's month/day order
                text.Append(date.ToString("O", CultureInfo.InvariantCulture));
                return;
            case IFormattable formattable:
                text.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
        }

        if (depth >= MaxDepth || !path.Add(value))
        {
            text.Append('…');
            return;
        }

        if (value is ITuple tuple)
            AppendTuple(text, tuple, depth, path);
        else if (value is IDictionary dictionary && !FakeScope.IsEmpty(dictionary))
            AppendElements(text, Entries(dictionary), depth, path); // entries, not boxed KeyValuePairs, whose ToString hides the values' members
        else if (value is IEnumerable sequence)
            AppendElements(text, sequence, depth, path);
        else
            AppendObject(text, value, depth, path);
        path.Remove(value);
    }

    /// <summary>
    /// The first entries of <paramref name="dictionary"/> in key order (ordinal for strings), so equal dictionaries are described alike and the entries
    /// shown are the same ones whatever order they were added in. Only the <see cref="MaxElements"/> + 1 lowest are kept (the extra one shows that more
    /// follow), so a large dictionary costs no more than a scan. Keys that can't be compared keep the dictionary's order.
    /// </summary>
    private static List<DictionaryEntry> Entries(IDictionary dictionary)
    {
        if (KeyOrder(dictionary) is { } order)
        {
            try
            {
                return Lowest(dictionary, order);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // a key's CompareTo refused another key (a tuple of objects that aren't comparable): describe them unsorted rather than lose the report
            }
        }

        var entries = new List<DictionaryEntry>(MaxElements + 1);
        var enumerator = dictionary.GetEnumerator();
        while (entries.Count <= MaxElements && enumerator.MoveNext())
            entries.Add(enumerator.Entry);
        return entries;
    }

    /// <summary>How the keys of <paramref name="dictionary"/> are ordered: ordinal for strings, their own order for keys of one comparable type, else none.</summary>
    private static Comparison<DictionaryEntry>? KeyOrder(IDictionary dictionary)
    {
        Type? keyType = null;
        foreach (var key in dictionary.Keys)
        {
            keyType ??= key.GetType();
            if (key.GetType() != keyType || key is not IComparable)
                return null;
        }

        return keyType == typeof(string)
            ? (a, b) => string.CompareOrdinal((string)a.Key, (string)b.Key)
            : (a, b) => ((IComparable)a.Key).CompareTo(b.Key);
    }

    /// <summary>The <see cref="MaxElements"/> + 1 lowest entries in <paramref name="order"/>, kept sorted while the dictionary is scanned once.</summary>
    private static List<DictionaryEntry> Lowest(IDictionary dictionary, Comparison<DictionaryEntry> order)
    {
        var lowest = new List<DictionaryEntry>(MaxElements + 2);
        var enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Entry;
            var at = lowest.Count;
            while (at > 0 && order(entry, lowest[at - 1]) < 0)
                at--;
            if (at > MaxElements)
                continue;
            lowest.Insert(at, entry);
            if (lowest.Count > MaxElements + 1)
                lowest.RemoveAt(lowest.Count - 1);
        }

        return lowest;
    }

    private static void AppendTuple(StringBuilder text, ITuple tuple, int depth, HashSet<object> path)
    {
        text.Append('(');
        for (var i = 0; i < tuple.Length; i++)
        {
            if (i > 0)
                text.Append(", ");
            Append(text, tuple[i], depth + 1, path);
        }

        text.Append(')');
    }

    private static void AppendElements(StringBuilder text, IEnumerable sequence, int depth, HashSet<object> path)
    {
        if (FakeScope.IsEmpty(sequence))
        {
            text.Append("[]"); // also a default ImmutableArray<T>, which refuses to be enumerated
            return;
        }

        text.Append('[');
        var count = 0;
        foreach (var element in sequence)
        {
            if (count == MaxElements)
            {
                text.Append(", …");
                break;
            }

            if (count++ > 0)
                text.Append(", ");
            if (element is DictionaryEntry entry)
            {
                Append(text, entry.Key, depth + 1, path);
                text.Append(" = ");
                Append(text, entry.Value, depth + 1, path);
            }
            else
            {
                Append(text, element, depth + 1, path);
            }
        }

        text.Append(']');
    }

    private static void AppendObject(StringBuilder text, object value, int depth, HashSet<object> path)
    {
        var type = value.GetType();
        var framework = type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));
        var model = !framework && ModelRegistry.CanGet(type) ? ModelRegistry.Get(type) : null;
        var readable = model?.Members.Select((member, index) => (member, index)).Where(m => m.member is { IsSettable: true } or { FillsExisting: true }).ToList();
        // A type the models can't read whole (a positional record, a framework type) describes itself better, when it overrides ToString.
        // Called only when the models can't read the object whole: a record's own ToString follows its members without cycle checks.
        if (readable is null || readable.Count == 0 || (readable.Count < model!.Members.Count && value.ToString() is var own && own != type.ToString()))
        {
            text.Append(value.ToString());
            return;
        }

        text.Append(type.Name).Append(" { ");
        for (var i = 0; i < readable.Count; i++)
        {
            if (i > 0)
                text.Append(", ");
            text.Append(readable[i].member.Name).Append(" = ");
            Append(text, model.GetMember(value, readable[i].index), depth + 1, path);
        }

        text.Append(" }");
    }

    private static void AppendQuoted(StringBuilder text, string s)
    {
        text.Append('"');
        var shown = s.Length > MaxText ? s[..MaxText] : s;
        foreach (var c in shown)
        {
            _ = c switch
            {
                '"' => text.Append("\\\""),
                '\\' => text.Append("\\\\"),
                '\n' => text.Append("\\n"),
                '\r' => text.Append("\\r"),
                '\t' => text.Append("\\t"),
                _ => text.Append(c),
            };
        }

        text.Append(s.Length > MaxText ? "…\"" : "\"");
    }
}
