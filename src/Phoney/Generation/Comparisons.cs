namespace Phoney.Generation;

/// <summary>A model that keeps the <c>[Compare]</c> pairs of its members, so names are matched once per model and not per object.</summary>
internal interface IComparedMembers
{
    /// <summary>The pairs, from <see cref="Comparisons.PairsOf"/>.</summary>
    Comparisons.Pair[] Compared { get; }
}

/// <summary>
/// <c>[Compare]</c>: a member that must equal another (<c>ConfirmPassword</c> and <c>Password</c>) takes its value once the object is made. Each member is
/// still produced on its own first, so nothing drawn after it moves, and this step draws nothing. It runs on every object, root or nested, created or populated,
/// through the model's own members, so reflection and generated models agree.
/// </summary>
internal static class Comparisons
{
    /// <summary>A member with <c>[Compare]</c> and the member it names (<c>-1</c> when that is not a member the model reads).</summary>
    internal readonly record struct Pair(int Member, int Other);

    /// <summary>Makes the compared members of a finished root object equal.</summary>
    public static T Apply<T>(FakeModel<T> model, T instance, FakeScope scope) =>
        instance is null || ((IComparedMembers)model).Compared is not { Length: > 0 } pairs ? instance : Apply(model, instance, scope, pairs);

    /// <summary>Makes the compared members of a finished nested object equal.</summary>
    public static object? Apply(IFakeModel model, object? instance, FakeScope scope)
    {
        var pairs = model is IComparedMembers known ? known.Compared : PairsOf(model.Members);
        return instance is null || pairs.Length == 0 ? instance : Apply(new Untyped(model), instance, scope, pairs);
    }

    /// <summary>
    /// For each pair: a rule, feed or custom convention for the compared member wins; otherwise it takes the other member's value, unless only it was kept by
    /// <c>Populate</c>, which gives its value to the other member instead (when nothing else gives that one its value).
    /// </summary>
    private static T Apply<T, TTarget>(TTarget target, T instance, FakeScope scope, Pair[] pairs)
        where TTarget : IPopulateTarget<T>
    {
        foreach (var (member, other) in pairs)
        {
            if (scope.IsComparisonBroken(member))
            {
                if (other >= 0 && target.ValueAt(instance, member) is string own && Equals(own, target.ValueAt(instance, other)))
                    instance = target.WithValueAt(instance, member, Different(own));
                continue;
            }

            if (scope.HasSource(member))
                continue;
            if (other < 0)
                throw Unpaired(scope, target.Members[member]);

            var memberKept = scope.IsKept(member);
            if (!memberKept)
                instance = target.WithValueAt(instance, member, target.ValueAt(instance, other));
            else if (!scope.IsKept(other) && !scope.HasSource(other) && target.Members[other].IsSettable)
                instance = target.WithValueAt(instance, other, target.ValueAt(instance, member));
        }

        return instance;
    }

    /// <summary>The text with its last character changed (the length stays, so the member's length limits still hold).</summary>
    private static string Different(string text)
    {
        if (text.Length == 0)
            return "x";
        var last = text[^1] == 'z' ? 'a' : (char)(text[^1] + 1);
        return string.Concat(text.AsSpan(0, text.Length - 1), new ReadOnlySpan<char>(in last));
    }

    private static InvalidOperationException Unpaired(FakeScope scope, FakeMember member) => new(
        $"{scope.TypeName}.{member.Name} has [Compare(\"{member.Constraints!.Compare}\")], but Phoney can't make the two equal: {member.Name} can't be set after the object is created, " +
        $"or {member.Constraints.Compare} is not a member it reads. Give {member.Name} a setter or init accessor, or add a rule with .With(...) for it.");

    /// <summary>The members with <c>[Compare]</c> and the members they name.</summary>
    public static Pair[] PairsOf(IReadOnlyList<FakeMember> members)
    {
        List<Pair>? pairs = null;
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].Constraints?.Compare is not { } name)
                continue;
            var other = -1;
            for (var j = 0; j < members.Count && members[i].IsSettable; j++)
            {
                if (j != i && string.Equals(members[j].Name, name, StringComparison.Ordinal) && (members[j].IsSettable || members[j].FillsExisting))
                    other = j; // both read and set through the model
            }

            (pairs ??= []).Add(new Pair(i, other));
        }

        return pairs is null ? [] : [.. pairs];
    }

    /// <summary>A nested object's model, read and set through <see cref="IFakeModel"/>.</summary>
    private readonly struct Untyped(IFakeModel model) : IPopulateTarget<object>
    {
        public IReadOnlyList<FakeMember> Members => model.Members;

        public object? ValueAt(object instance, int member) => model.GetMember(instance, member);

        public object WithValueAt(object instance, int member, object? value) => model.SetMember(instance, member, value);

        public bool IsUnsetAt(object instance, int member) => throw new NotSupportedException();

        public Func<FakeScope, object?> ProducerOf(int member) => throw new NotSupportedException();

        public void FillAt(object instance, int member, object? values, bool replace) => throw new NotSupportedException();
    }
}
