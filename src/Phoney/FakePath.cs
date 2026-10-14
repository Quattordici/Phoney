namespace Phoney;

/// <summary>Marker for rule paths that go through a collection: <c>.With(x =&gt; x.Lines.Each().Sku, "SKU-1")</c>.</summary>
public static class FakePath
{
    /// <summary>
    /// Stands for every element of a collection (a list, array, set, or <c>Dictionary.Values</c>) in the member path of a rule, so the rest of
    /// the path names a member of each element: <c>x =&gt; x.Lines.Each().Sku</c>, <c>x =&gt; x.ByCode.Values.Each().Supplier.City</c>.
    /// </summary>
    /// <remarks>It only has a meaning inside the expression passed to <c>With</c>, <c>WithOneOf</c>, <c>Ignore</c>, <c>Invalid</c> or <c>Boundary</c>; calling it throws.</remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The collection member.</param>
    /// <exception cref="InvalidOperationException">Always: the method is never run, the generator reads it from the expression.</exception>
    public static T Each<T>(this IEnumerable<T> source) =>
        throw new InvalidOperationException("Each() only marks 'every element' inside the member expression of a rule, e.g. .With(x => x.Lines.Each().Sku, \"SKU-1\"); it is not meant to be called.");
}
