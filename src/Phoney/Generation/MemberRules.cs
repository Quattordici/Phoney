using System.Linq.Expressions;
using System.Numerics;

namespace Phoney;

/// <summary>
/// The rules of one member, from <see cref="ObjectGenerator{T}.Member{TValue}"/>: <c>.Member(c =&gt; c.Email, m =&gt; m.OneOf(emails).Unique())</c>. Each method
/// adds the rule of the generator method it is named after (<see cref="Is"/> is <c>With(member, value)</c>, <see cref="OneOf(IReadOnlyList{TValue})"/> is
/// <c>WithOneOf</c>…), with the same checks and the same objects for a seed; the member is selected once.
/// </summary>
/// <remarks>
/// <para>
/// Only the rules a member's type can take are offered: <see cref="MemberRules.Normal{T, TNumber}(MemberRules{T, TNumber}, double, double, double?, double?, int?)"/>
/// for numbers and <c>SubsetOf</c> for collections are extension methods of <see cref="MemberRules"/> that appear on members of those types only.
/// </para>
/// <para>
/// The rules apply in the order they are written, like the generator's own: a later rule that gives the member its value replaces an earlier one
/// (<c>m.OneOf(a).Is(b)</c> keeps <c>Is(b)</c>), and <see cref="Unique"/> and <see cref="Where"/> combine with it. The view is immutable like the generator.
/// </para>
/// </remarks>
/// <typeparam name="T">The generated type.</typeparam>
/// <typeparam name="TValue">The type of the member.</typeparam>
public readonly struct MemberRules<T, TValue>
{
    private readonly ObjectGenerator<T>? _generator;

    internal MemberRules(ObjectGenerator<T> generator, Expression<Func<T, TValue>> member) => (_generator, Selector) = (generator, member);

    /// <summary>The generator with the rules so far.</summary>
    internal ObjectGenerator<T> Generator =>
        _generator ?? throw new InvalidOperationException("These member rules are empty: use the rules that Member(x => x.Name, m => …) gives its lambda.");

    /// <summary>The member the rules are for, as it was selected.</summary>
    internal Expression<Func<T, TValue>> Selector { get; }

    /// <summary>The view after one more rule.</summary>
    internal MemberRules<T, TValue> Then(Func<ObjectGenerator<T>, Expression<Func<T, TValue>>, ObjectGenerator<T>> rule) => new(rule(Generator, Selector), Selector);

    /// <summary>Sets the member to a fixed value; <see cref="ObjectGenerator{T}.With{TValue}(Expression{Func{T, TValue}}, TValue)"/>.</summary>
    public MemberRules<T, TValue> Is(TValue value) => Then((g, m) => g.With(m, value));

    /// <summary>Generates the member with <paramref name="factory"/>, e.g. <c>m.From(f =&gt; f.Number.Int(18, 65))</c>; <see cref="ObjectGenerator{T}.With{TValue}(Expression{Func{T, TValue}}, Func{Faker, TValue})"/>.</summary>
    public MemberRules<T, TValue> From(Func<Faker, TValue> factory) => Then((g, m) => g.With(m, factory));

    /// <summary>
    /// Generates the member from the object after its other members are set, e.g. <c>m.From((f, x) =&gt; f.Internet.Email(x.FirstName, x.LastName))</c>;
    /// <see cref="ObjectGenerator{T}.With{TValue}(Expression{Func{T, TValue}}, Func{Faker, T, TValue})"/>.
    /// </summary>
    public MemberRules<T, TValue> From(Func<Faker, T, TValue> factory) => Then((g, m) => g.With(m, factory));

    /// <summary>Generates a nested member with its own generator, also of a subtype; <see cref="ObjectGenerator{T}.With{TValue, TGenerated}(Expression{Func{T, TValue}}, ObjectGenerator{TGenerated})"/>.</summary>
    public MemberRules<T, TValue> From<TGenerated>(ObjectGenerator<TGenerated> generator)
        where TGenerated : TValue => Then((g, m) => g.With(m, generator));

    /// <summary>Sets the member to one of <paramref name="values"/>; <see cref="ObjectGenerator{T}.WithOneOf{TValue}(Expression{Func{T, TValue}}, IReadOnlyList{TValue})"/>.</summary>
    public MemberRules<T, TValue> OneOf(IReadOnlyList<TValue> values) => Then((g, m) => g.WithOneOf(m, values));

    /// <summary>Sets the member to one of the values, by weight: <c>m.OneOf((Status.Paid, 70), (Status.Failed, 5))</c>; <see cref="ObjectGenerator{T}.WithOneOf{TValue}(Expression{Func{T, TValue}}, ValueTuple{TValue, double}[])"/>.</summary>
    public MemberRules<T, TValue> OneOf(params (TValue Value, double Weight)[] choices) => Then((g, m) => g.WithOneOf(m, choices));

    /// <summary>Sets the member to one of <paramref name="values"/>, each as often as its weight says; <see cref="ObjectGenerator{T}.WithOneOf{TValue}(Expression{Func{T, TValue}}, IReadOnlyList{TValue}, IReadOnlyList{double})"/>.</summary>
    public MemberRules<T, TValue> OneOf(IReadOnlyList<TValue> values, IReadOnlyList<double> weights) => Then((g, m) => g.WithOneOf(m, values, weights));

    /// <summary>Sets the member from a random item of <paramref name="source"/>: <c>m.OneOf(customers, c =&gt; c.Id)</c>; <see cref="ObjectGenerator{T}.WithOneOf{TSource, TValue}(Expression{Func{T, TValue}}, IReadOnlyList{TSource}, Func{TSource, TValue})"/>.</summary>
    public MemberRules<T, TValue> OneOf<TSource>(IReadOnlyList<TSource> source, Func<TSource, TValue> select) => Then((g, m) => g.WithOneOf(m, source, select));

    /// <summary>Sets the member from a random item of <paramref name="source"/>, heavier items more often; <see cref="ObjectGenerator{T}.WithOneOf{TSource, TValue}(Expression{Func{T, TValue}}, IReadOnlyList{TSource}, Func{TSource, TValue}, Func{TSource, double})"/>.</summary>
    public MemberRules<T, TValue> OneOf<TSource>(IReadOnlyList<TSource> source, Func<TSource, TValue> select, Func<TSource, double> weight) => Then((g, m) => g.WithOneOf(m, source, select, weight));

    /// <summary>Makes the member unique across everything the generator produces; <see cref="ObjectGenerator{T}.Unique{TValue}(Expression{Func{T, TValue}}, int)"/>.</summary>
    public MemberRules<T, TValue> Unique(int maxAttempts = 1000) => Then((g, m) => g.Unique(m, maxAttempts));

    /// <summary>Generates the member again until it satisfies <paramref name="predicate"/>; <see cref="ObjectGenerator{T}.Where{TValue}(Expression{Func{T, TValue}}, Func{TValue, bool}, int)"/>.</summary>
    public MemberRules<T, TValue> Where(Func<TValue, bool> predicate, int maxAttempts = 1000) => Then((g, m) => g.Where(m, predicate, maxAttempts));

    /// <summary>Gives the member a value for the object's position: <c>m.Sequence(i =&gt; $"C{i + 1:D4}")</c>; <see cref="ObjectGenerator{T}.Sequence{TValue}"/>.</summary>
    public MemberRules<T, TValue> Sequence(Func<long, TValue> factory) => Then((g, m) => g.Sequence(m, factory));

    /// <summary>Gives the member the <paramref name="values"/> in turn, by the object's position; <see cref="ObjectGenerator{T}.Cycle{TValue}"/>.</summary>
    public MemberRules<T, TValue> Cycle(IReadOnlyList<TValue> values) => Then((g, m) => g.Cycle(m, values));

    /// <summary>Sets the member to a value that breaks one of its data annotations; <see cref="ObjectGenerator{T}.Invalid{TValue}"/>.</summary>
    public MemberRules<T, TValue> Invalid() => Then((g, m) => g.Invalid(m));

    /// <summary>Sets the member exactly on a limit of its data annotations; <see cref="ObjectGenerator{T}.Boundary{TValue}"/>.</summary>
    public MemberRules<T, TValue> Boundary() => Then((g, m) => g.Boundary(m));

    /// <summary>Leaves the member at its default value; <see cref="ObjectGenerator{T}.Ignore{TValue}"/>.</summary>
    public MemberRules<T, TValue> Ignore() => Then((g, m) => g.Ignore(m));
}

/// <summary>
/// The member rules that only some member types can take, so they appear on <see cref="MemberRules{T, TValue}"/> only for members of those types:
/// <c>Normal</c> for numbers and <c>SubsetOf</c> for collections.
/// </summary>
public static class MemberRules
{
    /// <summary>
    /// Gives a number member values from a normal distribution: <c>m.Normal(175, 8)</c>;
    /// <see cref="ObjectGenerator{T}.WithNormalDistribution{TNumber}(Expression{Func{T, TNumber}}, double, double, double?, double?, int?)"/>.
    /// </summary>
    /// <param name="rules">The member's rules.</param>
    /// <param name="mean">The centre of the distribution.</param>
    /// <param name="standardDeviation">The spread; zero gives the mean every time.</param>
    /// <param name="min">The smallest value, or <see langword="null"/> for none.</param>
    /// <param name="max">The largest value, or <see langword="null"/> for none.</param>
    /// <param name="decimals">The number of decimals to round to (0 to 15), or <see langword="null"/> for the type's default.</param>
    public static MemberRules<T, TNumber> Normal<T, TNumber>(this MemberRules<T, TNumber> rules, double mean, double standardDeviation, double? min = null, double? max = null, int? decimals = null)
        where TNumber : INumber<TNumber> => rules.Then((g, m) => g.WithNormalDistribution(m, mean, standardDeviation, min, max, decimals));

    /// <inheritdoc cref="Normal{T, TNumber}(MemberRules{T, TNumber}, double, double, double?, double?, int?)"/>
    public static MemberRules<T, TNumber?> Normal<T, TNumber>(this MemberRules<T, TNumber?> rules, double mean, double standardDeviation, double? min = null, double? max = null, int? decimals = null)
        where TNumber : struct, INumber<TNumber> => rules.Then((g, m) => g.WithNormalDistribution(m, mean, standardDeviation, min, max, decimals));

    /// <summary>Gives an array member a random subset of <paramref name="values"/>: <c>m.SubsetOf(tags, (1, 3))</c>; <see cref="ObjectGenerator{T}.WithSubsetOf{TValue}(Expression{Func{T, TValue[]}}, IReadOnlyList{TValue}, SubsetSize)"/>.</summary>
    /// <param name="rules">The member's rules.</param>
    /// <param name="values">The values to take a subset of.</param>
    /// <param name="size">How many values: a number (<c>3</c>) or an inclusive range (<c>(1, 3)</c>).</param>
    public static MemberRules<T, TElement[]> SubsetOf<T, TElement>(this MemberRules<T, TElement[]> rules, IReadOnlyList<TElement> values, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, size));

    /// <summary>Gives an array member a weighted random subset of <paramref name="values"/>; <see cref="ObjectGenerator{T}.WithSubsetOf{TValue}(Expression{Func{T, TValue[]}}, IReadOnlyList{TValue}, IReadOnlyList{double}, SubsetSize)"/>.</summary>
    /// <param name="rules">The member's rules.</param>
    /// <param name="values">The values to take a subset of.</param>
    /// <param name="weights">One weight per value (relative; zero is never included).</param>
    /// <param name="size">How many values: a number (<c>3</c>) or an inclusive range (<c>(1, 3)</c>).</param>
    public static MemberRules<T, TElement[]> SubsetOf<T, TElement>(this MemberRules<T, TElement[]> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, weights, size));

    /// <summary>Gives a <see cref="HashSet{T}"/> member a random subset of <paramref name="values"/>; see <see cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)"/>.</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, HashSet<TElement>> SubsetOf<T, TElement>(this MemberRules<T, HashSet<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, size));

    /// <summary>Gives a <see cref="HashSet{T}"/> member a weighted random subset of <paramref name="values"/>.</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, HashSet<TElement>> SubsetOf<T, TElement>(this MemberRules<T, HashSet<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, weights, size));

    /// <summary>Gives an <see cref="ISet{T}"/> member a random subset of <paramref name="values"/> (a <see cref="HashSet{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, ISet<TElement>> SubsetOf<T, TElement>(this MemberRules<T, ISet<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, size));

    /// <summary>Gives an <see cref="ISet{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="HashSet{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, ISet<TElement>> SubsetOf<T, TElement>(this MemberRules<T, ISet<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) =>
        rules.Then((g, m) => g.WithSubsetOf(m, values, weights, size));

    /// <summary>Gives a <see cref="List{T}"/> member a random subset of <paramref name="values"/>.</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, List<TElement>> SubsetOf<T, TElement>(this MemberRules<T, List<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives a <see cref="List{T}"/> member a weighted random subset of <paramref name="values"/>.</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, List<TElement>> SubsetOf<T, TElement>(this MemberRules<T, List<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>Gives an <see cref="IList{T}"/> member a random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IList<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IList<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives an <see cref="IList{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IList<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IList<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>Gives an <see cref="ICollection{T}"/> member a random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, ICollection<TElement>> SubsetOf<T, TElement>(this MemberRules<T, ICollection<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives an <see cref="ICollection{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, ICollection<TElement>> SubsetOf<T, TElement>(this MemberRules<T, ICollection<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>Gives an <see cref="IReadOnlyList{T}"/> member a random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IReadOnlyList<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IReadOnlyList<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives an <see cref="IReadOnlyList{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IReadOnlyList<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IReadOnlyList<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>Gives an <see cref="IReadOnlyCollection{T}"/> member a random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IReadOnlyCollection<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IReadOnlyCollection<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives an <see cref="IReadOnlyCollection{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IReadOnlyCollection<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IReadOnlyCollection<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>Gives an <see cref="IEnumerable{T}"/> member a random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IEnumerable<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IEnumerable<TElement>> rules, IReadOnlyList<TElement> values, SubsetSize size) => AsList(rules, values, null, size);

    /// <summary>Gives an <see cref="IEnumerable{T}"/> member a weighted random subset of <paramref name="values"/> (a <see cref="List{T}"/>).</summary>
    /// <inheritdoc cref="SubsetOf{T, TElement}(MemberRules{T, TElement[]}, IReadOnlyList{TElement}, IReadOnlyList{double}, SubsetSize)" path="/param"/>
    public static MemberRules<T, IEnumerable<TElement>> SubsetOf<T, TElement>(this MemberRules<T, IEnumerable<TElement>> rules, IReadOnlyList<TElement> values, IReadOnlyList<double> weights, SubsetSize size) => AsList(rules, values, weights, size);

    /// <summary>
    /// The list-shaped subsets go through the generator's <c>IEnumerable&lt;T&gt;</c> overload, which makes a <see cref="List{T}"/>: the member is selected as that
    /// interface, the conversion the compiler adds when the flat call is written (the member path reads through it).
    /// </summary>
    private static MemberRules<T, TCollection> AsList<T, TCollection, TElement>(MemberRules<T, TCollection> rules, IReadOnlyList<TElement> values, IReadOnlyList<double>? weights, SubsetSize size)
        where TCollection : IEnumerable<TElement> =>
        rules.Then((g, m) =>
        {
            var member = Expression.Lambda<Func<T, IEnumerable<TElement>>>(Expression.Convert(m.Body, typeof(IEnumerable<TElement>)), m.Parameters);
            return weights is null ? g.WithSubsetOf(member, values, size) : g.WithSubsetOf(member, values, weights, size);
        });
}
