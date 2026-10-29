using Phoney.Generation;

namespace Phoney.Tests;

public sealed class GraphRoot
{
    public int Id { get; set; }
    public Dictionary<string, GraphLeaf> ByName { get; set; } = [];
    public List<GraphLeaf?> Owned { get; } = [];
}

public sealed class GraphLeaf
{
    public int RootId { get; set; }
    public GraphRoot? Root { get; set; }
}

public sealed class GraphNode
{
    public GraphNode? Parent { get; set; }
    public List<GraphNode> Children { get; set; } = [];
}

/// <summary>
/// How back-references and parent keys walk a finished graph, which both do the same way: into dictionary values, into read-only collections that were
/// filled, past null elements, and to the nearest object above the member's owner.
/// </summary>
public sealed class ObjectGraphTests
{
    private static (GraphRoot Root, GraphLeaf Named, GraphLeaf Owned) Graph()
    {
        var named = new GraphLeaf();
        var owned = new GraphLeaf();
        var root = new GraphRoot { Id = 7, ByName = { ["a"] = named } };
        root.Owned.Add(null);
        root.Owned.Add(owned);
        return (root, named, owned);
    }

    private static ParentKey RootId(string collection) => new([collection, nameof(GraphLeaf.RootId)], typeof(GraphRoot), r => ((GraphRoot)r).Id);

    [Test]
    public void Back_references_reach_dictionary_values_and_filled_read_only_collections_past_null_elements()
    {
        var (root, named, owned) = Graph();

        BackReferences.Apply(root);

        named.Root.ShouldBeSameAs(root);
        owned.Root.ShouldBeSameAs(root);
    }

    [Test]
    public void Parent_keys_reach_dictionary_values_and_filled_read_only_collections_past_null_elements()
    {
        var (root, named, owned) = Graph();

        ParentKeys.Apply(root, [RootId(nameof(GraphRoot.ByName)), RootId(nameof(GraphRoot.Owned))]);

        named.RootId.ShouldBe(7);
        owned.RootId.ShouldBe(7);
    }

    [Test]
    public void An_object_is_not_its_own_parent()
    {
        var child = new GraphNode();
        var root = new GraphNode { Children = [child] };

        BackReferences.Apply(root);

        root.Parent.ShouldBeNull();
        child.Parent.ShouldBeSameAs(root);
    }

    [Test]
    public void Parent_keys_are_kept_shallowest_first_and_in_the_order_they_were_added()
    {
        ParentKey Key(params string[] path) => new(path, typeof(GraphRoot), _ => null);
        var deep = Key("A", "B", "C");
        var first = Key("A", "X");
        var second = Key("A", "Y");
        var replaced = Key("A", "X");

        var keys = ParentKeys.Add(ParentKeys.Add(ParentKeys.Add(ParentKeys.Add([], deep), first), second), replaced);

        keys.ShouldBe([second, replaced, deep]);
    }
}
