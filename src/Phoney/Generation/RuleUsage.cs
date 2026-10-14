namespace Phoney.Generation;

/// <summary>
/// Records which of a generator's graph-dependent rules (path rules, <c>Subtype</c>, <c>Use</c>, <c>WithConvention</c>) took effect, for
/// <see cref="Generator{T}.VerifyRules"/>. Everything is registered when the generator is compiled and only read afterwards, so
/// concurrent runs just flip flags; a generator that does not verify has no instance and pays nothing.
/// </summary>
internal sealed class RuleUsage
{
    // Looked up while generating; the lists keep the order the rules are reported in (path rules, Subtype, Use, WithConvention), whenever they were registered.
    private readonly Dictionary<string, Entry> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, Entry> _subtypes = [];
    private readonly Dictionary<Type, Entry> _generators = [];
    private readonly Dictionary<CustomConvention, Entry> _conventions = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> _pathEntries = [];
    private readonly List<Entry> _subtypeEntries = [];
    private readonly List<Entry> _generatorEntries = [];
    private readonly List<Entry> _conventionEntries = [];

    /// <summary>Registers the path rule for <paramref name="rule"/> at <paramref name="path"/> (dotted member names below <paramref name="root"/>).</summary>
    public Entry AddPath(Type root, string path, PathRule rule) =>
        Register(_paths, _pathEntries, path, $"{KindName(rule)} rule for {root.Name}.{path}");

    /// <summary>Registers a <c>Subtype</c> for <paramref name="type"/>.</summary>
    public void AddSubtype(Type type) => Register(_subtypes, _subtypeEntries, type, $"Subtype<{type.Name}>");

    /// <summary>Registers a <c>Use</c> generator for <paramref name="type"/>.</summary>
    public void AddGenerator(Type type) => Register(_generators, _generatorEntries, type, $"Use<{type.Name}>");

    /// <summary>Registers a <c>WithConvention</c>; <paramref name="description"/> tells it apart from others of the same type.</summary>
    public void AddConvention(CustomConvention convention, string description) => Register(_conventions, _conventionEntries, convention, description);

    /// <summary>Marks the generator's own subtype registration for <paramref name="type"/> as used (other registrations are not tracked).</summary>
    public void Subtype(Type type) => Mark(_subtypes, type);

    /// <summary>Marks the generator's own <c>Use</c> registration for <paramref name="type"/> as used.</summary>
    public void Generator(Type type) => Mark(_generators, type);

    /// <summary>Marks a convention as used when it matched a member (app-wide conventions are not tracked).</summary>
    public void Convention(CustomConvention convention) => Mark(_conventions, convention);

    /// <summary>The descriptions of the rules that never took effect, in the order they were registered.</summary>
    public List<string> Unused()
    {
        var unused = new List<string>();
        foreach (var entries in (List<Entry>[])[_pathEntries, _subtypeEntries, _generatorEntries, _conventionEntries])
        {
            foreach (var entry in entries)
            {
                if (!entry.Used)
                    unused.Add(entry.Description);
            }
        }

        return unused;
    }

    private static Entry Register<TKey>(Dictionary<TKey, Entry> index, List<Entry> entries, TKey key, string description)
        where TKey : notnull
    {
        var entry = new Entry(description);
        index[key] = entry;
        entries.Add(entry);
        return entry;
    }

    private static void Mark<TKey>(Dictionary<TKey, Entry> index, TKey key)
        where TKey : notnull
    {
        if (index.TryGetValue(key, out var entry))
            entry.MarkUsed();
    }

    private static string KindName(PathRule rule) => rule.Kind switch
    {
        RuleKind.Ignore => "Ignore",
        RuleKind.Constraint => ((ConstraintRequest)rule.Value!).Boundary ? "Boundary" : "Invalid",
        _ => "With",
    };

    /// <summary>One rule and whether it took effect.</summary>
    public sealed class Entry(string description)
    {
        private bool _used;

        /// <summary>What the rule is, as the message about unused rules names it.</summary>
        public string Description { get; } = description;

        /// <summary>Whether the rule took effect for anything that was generated.</summary>
        public bool Used => Volatile.Read(ref _used);

        /// <summary>Records that the rule took effect (a read first, so hot paths don't keep writing a shared cache line).</summary>
        public void MarkUsed()
        {
            if (!Volatile.Read(ref _used))
                Volatile.Write(ref _used, true);
        }
    }
}
