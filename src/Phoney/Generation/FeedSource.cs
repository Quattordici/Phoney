namespace Phoney.Generation;

/// <summary>The feed of <c>ObjectGenerator&lt;T&gt;.WithFeed</c> and how it is used.</summary>
internal sealed record FeedSettings(DataFeed Feed, FeedOrder Order, bool IgnoreUnknownColumns);

/// <summary>A data feed as a source of <see cref="MemberPlan"/>: the members it gives values to, with every value converted once.</summary>
internal static class FeedSource
{
    /// <summary>
    /// Gives each member that has a column in the feed (and no rule of its own) its values, converted once now so that a bad value fails here, with its row and column,
    /// instead of in the middle of a run. The slot picks the row from the object's position.
    /// </summary>
    /// <param name="typeName">The name of the generated type, for messages.</param>
    /// <param name="settings">The feed and its order.</param>
    /// <param name="baseSeed">The generator's base seed, which a random order is derived from.</param>
    /// <param name="members">The members of the generated type.</param>
    /// <param name="rules">The members that have a rule, which wins over the feed.</param>
    /// <exception cref="InvalidOperationException">A column has no member, or a value can't be converted to its member's type.</exception>
    public static Dictionary<int, SlotDecision> Decisions(string typeName, FeedSettings settings, long baseSeed, IReadOnlyList<FakeMember> members, IReadOnlyDictionary<int, SlotDecision> rules)
    {
        var feed = settings.Feed;
        var decisions = new Dictionary<int, SlotDecision>();
        var unknown = new List<string>();
        for (var column = 0; column < feed.Columns.Count; column++)
        {
            var index = MemberIndex(members, feed.Columns[column]);
            if (index < 0)
            {
                unknown.Add(feed.Columns[column]);
                continue;
            }

            if (rules.ContainsKey(index))
                continue; // a rule for the member wins

            var values = Column(typeName, feed, column, members[index]);
            var rowOf = RowSelector(settings.Order, baseSeed, values.Length);
            decisions[index] = new SlotDecision(RuleKind.Convention, (Func<FakeScope, object?>)(scope => values[rowOf(scope.Index)]));
        }

        if (unknown.Count > 0 && !settings.IgnoreUnknownColumns)
        {
            throw new InvalidOperationException(
                $"The feed has columns that {typeName} has no member for: {string.Join(", ", unknown.Select(c => $"'{c}'"))}. Its members are {string.Join(", ", members.Select(m => m.Name))}. "
                + "Rename the columns (case, spaces and underscores don't matter), or pass ignoreUnknownColumns: true to skip them.");
        }

        return decisions;
    }

    /// <summary>The index of the member a feed column is for (case, spaces and underscores don't matter), or -1 when there is none.</summary>
    private static int MemberIndex(IReadOnlyList<FakeMember> members, string column)
    {
        var name = DataFeed.Normalize(column);
        for (var i = 0; i < members.Count; i++)
        {
            if (DataFeed.Normalize(members[i].Name) == name)
                return i;
        }

        return -1;
    }

    /// <summary>Every value of a feed column converted to the member's type.</summary>
    /// <exception cref="InvalidOperationException">A value can't be converted; the message names its row and column.</exception>
    private static object?[] Column(string typeName, DataFeed feed, int column, FakeMember member)
    {
        var values = new object?[feed.Count];
        for (var row = 0; row < values.Length; row++)
        {
            try
            {
                values[row] = FeedValues.Convert(feed.Value(row, column), member.Type);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException(
                    $"Feed row {row + 1}, column '{feed.Columns[column]}' (value '{feed.Value(row, column)}') can't be used for {typeName}.{member.Name} ({member.Type.Name}): {e.Message}.", e);
            }
        }

        return values;
    }

    /// <summary>The row an object takes: its position modulo the number of rows, or a hash of the generator's seed and the position.</summary>
    private static Func<long, int> RowSelector(FeedOrder order, long baseSeed, int rows)
    {
        if (order == FeedOrder.InOrder)
            return index => (int)(index % rows);

        var seed = unchecked(baseSeed ^ 0x5EEDFEEDL);
        return index => (int)((ulong)SeededRandom.DeriveSeed(seed, index) % (ulong)rows);
    }
}
