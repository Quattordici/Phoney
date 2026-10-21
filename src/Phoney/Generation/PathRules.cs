using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Phoney.Generation;

/// <summary>A rule given to a member of a nested object, e.g. <c>x =&gt; x.Billing.City</c>; <paramref name="Value"/> is what the rule kind needs.</summary>
internal sealed record PathRule(RuleKind Kind, object? Value, Func<Faker, object?>? Boxed);

/// <summary>A configured path rule: its member names from the root, and the rule.</summary>
internal sealed record PathSpec(string[] Segments, PathRule Rule);

/// <summary>The slots and onward path nodes of one nested object, indexed by its model's members.</summary>
internal sealed record PathBinding(RuleSlot?[] Slots, PathNode?[]? Children);

/// <summary>
/// A position in the object graph that path rules reach: the rules for the members of the object there, and the positions below it
/// by member name. The same type at another position (<c>Billing</c> and <c>Shipping</c> are both addresses) is another node.
/// </summary>
internal sealed class PathNode
{
    private readonly Dictionary<string, (PathRule Rule, RuleUsage.Entry? Usage)> _rules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PathNode> _children = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, PathBinding> _bound = new();

    /// <summary>The position below this one reached through <paramref name="member"/>, created on first use.</summary>
    public PathNode Child(string member)
    {
        if (!_children.TryGetValue(member, out var child))
        {
            child = new PathNode();
            _children[member] = child;
        }

        return child;
    }

    /// <summary>Sets (or replaces) the rule for <paramref name="member"/> of the object at this position; <paramref name="usage"/> is marked when an object there gets it.</summary>
    public void Set(string member, PathRule rule, RuleUsage.Entry? usage) => _rules[member] = (rule, usage);

    /// <summary>The onward nodes indexed by <paramref name="members"/>, or <see langword="null"/> when none is below this one.</summary>
    public PathNode?[]? ChildrenFor(IReadOnlyList<FakeMember> members)
    {
        PathNode?[]? children = null;
        for (var i = 0; i < members.Count; i++)
        {
            if (_children.TryGetValue(members[i].Name, out var child))
            {
                children ??= new PathNode?[members.Count];
                children[i] = child;
            }
        }

        return children;
    }

    /// <summary>
    /// The slots for an object of <paramref name="type"/> at this position: the generator's custom conventions, with the path rules on top,
    /// bound once per type (a subtype of an interface has its own member indices). Slots hold no state, so they are shared across threads.
    /// </summary>
    public PathBinding Bind(Type type, IFakeModel model, NestedConventions? conventions) =>
        _bound.TryGetValue(type, out var bound) ? bound : _bound.GetOrAdd(type, Create(type, model, conventions));

    private PathBinding Create(Type type, IFakeModel model, NestedConventions? conventions)
    {
        var members = model.Members;
        var fromConventions = conventions?.SlotsFor(type, model);
        var slots = fromConventions is null ? new RuleSlot?[members.Count] : (RuleSlot?[])fromConventions.Clone();
        for (var i = 0; i < members.Count; i++)
        {
            if (!_rules.TryGetValue(members[i].Name, out var found))
                continue;
            var (rule, usage) = found;
            usage?.MarkUsed(); // the model gets or skips every member of an object created here, so binding is applying
            var value = rule.Kind == RuleKind.Constraint ? ConstraintCases.Resolve(type.Name, members[i], (ConstraintRequest)rule.Value!) : rule.Value;
            slots[i] = new RuleSlot(rule.Kind, value, unique: false, maxAttempts: 0, rule.Boxed);
        }

        RuleSlot.MarkGivenValues(members, slots);
        return new PathBinding(slots, ChildrenFor(members));
    }
}

/// <summary>Builds the path nodes of a generator from its path rules and checks the paths against the models they name.</summary>
internal static class PathRules
{
    /// <summary>
    /// The nodes below the root object, indexed by the root model's members (<see langword="null"/> without path rules). Every path is checked as far as
    /// the types are known: a member that doesn't exist or can't be set, a scalar in the middle of a path, a constraint rule without annotations.
    /// Below an interface or abstract class the subtype decides, so the rest of the path is checked when the nested object is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">A path or rule can't be generated.</exception>
    public static PathNode?[]? Build(Type rootType, IReadOnlyList<FakeMember> rootMembers, ImmutableDictionary<string, PathSpec> rules, RuleUsage? usage = null)
    {
        if (rules.IsEmpty)
            return null;

        var root = new PathNode();
        foreach (var key in rules.Keys.Order(StringComparer.Ordinal))
            Insert(root, rootType, rootMembers, rules[key], usage?.AddPath(rootType, key, rules[key].Rule));
        return root.ChildrenFor(rootMembers);
    }

    private static void Insert(PathNode root, Type rootType, IReadOnlyList<FakeMember> rootMembers, PathSpec spec, RuleUsage.Entry? usage)
    {
        var node = root;
        var owner = rootType;
        IReadOnlyList<FakeMember>? members = rootMembers;
        for (var i = 0; i < spec.Segments.Length; i++)
        {
            var name = spec.Segments[i];
            var member = members?.FirstOrDefault(m => m.Name == name);
            if (members is not null && member is null)
                throw new InvalidOperationException($"{PathTo(rootType, spec, i)} can't be generated: {owner.Name} has no public settable member or constructor parameter '{name}'.");

            if (i == spec.Segments.Length - 1)
            {
                if (member is not null && spec.Rule.Kind == RuleKind.Constraint)
                    ConstraintCases.Resolve(owner.Name, member, (ConstraintRequest)spec.Rule.Value!); // fail now, not when the first nested object is created
                node.Set(name, spec.Rule, usage);
                return;
            }

            node = node.Child(name);
            if (member is null)
                continue; // below an interface or abstract class: nothing to check against

            var (type, isObject) = HeldType(member);
            if (!isObject)
                throw new InvalidOperationException($"{PathTo(rootType, spec, i)} is {type.Name}, not an object with members, so the rule for {PathTo(rootType, spec, spec.Segments.Length - 1)} can't be generated.");

            owner = type;
            members = ModelRegistry.Get(owner)?.Members;
        }
    }

    /// <summary>The path up to segment <paramref name="last"/> for messages (<c>Order.Billing.City</c>).</summary>
    private static string PathTo(Type rootType, PathSpec spec, int last) => rootType.Name + "." + string.Join('.', spec.Segments, 0, last + 1);

    /// <summary>The type of the values a member holds (its element type for a collection), and whether they are objects: a scalar has no members to put rules on.</summary>
    private static (Type Type, bool IsObject) HeldType(FakeMember member)
    {
        var type = Nullable.GetUnderlyingType(member.Type) ?? member.Type;
        if (CollectionTypes.ElementType(type, out _) is { } element)
            type = Nullable.GetUnderlyingType(element) ?? element;
        var scalar = type == typeof(byte[]) || FakeMember.ScalarCategory(type) != TypeCategory.Other || ScalarAdapters.IsAdapted(type);
        return (type, !scalar);
    }
}
