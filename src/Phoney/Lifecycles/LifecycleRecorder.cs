namespace Phoney.Generation;

/// <summary>
/// Collects what <see cref="ObjectGenerator{T}.GenerateHistory"/> needs while one object is made: the lifecycle walks the scopes draw, in draw order, and the
/// object each scope made. It lives on the faker (<see cref="Faker.Recorder"/>), which nested generators inherit, so objects of other generators are recorded
/// too. It draws nothing while the object is made; <see cref="History{T}"/> then walks the finished graph, so the walks of attempts a condition rejected and
/// of objects that were left out are never part of it.
/// </summary>
internal sealed class LifecycleRecorder
{
    private readonly Dictionary<FakeScope, List<(LifecycleDefinition Lifecycle, LifecycleWalk Walk, int Order)>> _walks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, FakeScope> _scopes = new(ReferenceEqualityComparer.Instance);
    private FakeScope? _root;
    private int _order;

    /// <summary>Records that <paramref name="scope"/> drew <paramref name="walk"/> through <paramref name="lifecycle"/>.</summary>
    public void Walked(FakeScope scope, LifecycleDefinition lifecycle, LifecycleWalk walk)
    {
        if (!_walks.TryGetValue(scope, out var walks))
            _walks[scope] = walks = [];
        walks.Add((lifecycle, walk, _order++));
    }

    /// <summary>Records that <paramref name="scope"/> made <paramref name="made"/>; the last root object made is the one the run returns.</summary>
    public void Made(object? made, FakeScope scope, bool root)
    {
        if (root)
            _root = scope;
        if (made is not null && !made.GetType().IsValueType)
            _scopes[made] = scope;
    }

    /// <summary>The transitions of <paramref name="made"/>, the object just made, earliest first, and its creation time; the recorder is then ready for the next one.</summary>
    public (DateTimeOffset Created, Transition[] Transitions) Finish(object? made)
    {
        var root = _root ?? throw new InvalidOperationException("The object was made without a scope."); // every run makes its root through a scope
        var graph = new Graph(this, root);
        if (made is not null)
            graph.Walk(made);
        var transitions = graph.Transitions;
        transitions.Sort(static (a, b) => (a.Transition.At, a.Order, a.Step).CompareTo((b.Transition.At, b.Order, b.Step)));
        var result = (root.Created, transitions.Select(t => t.Transition).ToArray());

        _walks.Clear();
        _scopes.Clear();
        _root = null;
        _order = 0;
        return result;
    }

    /// <summary>The walk over the finished graph that turns each object's walks into transitions, with the path to its status member.</summary>
    private sealed class Graph(LifecycleRecorder recorder, FakeScope root) : ObjectGraph
    {
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

        public List<(Transition Transition, int Order, int Step)> Transitions { get; } = [];

        public void Walk(object made) => Visit(made);

        protected override void Visit(object node)
        {
            if (!_visited.Add(node))
                return;
            var scope = Depth == 0 ? root : recorder._scopes.GetValueOrDefault(node);
            if (scope is not null && recorder._walks.TryGetValue(scope, out var walks))
                Record(node, scope, walks);

            if (ModelOf(node) is not { } model)
                return;
            for (var i = 0; i < model.Members.Count; i++)
            {
                var member = model.Members[i];
                if (!IsWalkable(member) || !(member.IsNested || CollectionTypes.IsGeneratedCollection(member.Type)))
                    continue;
                if (model.GetMember(node, i) is { } value)
                    Descend(node, member, value);
            }
        }

        private void Record(object owner, FakeScope scope, List<(LifecycleDefinition Lifecycle, LifecycleWalk Walk, int Order)> walks)
        {
            var created = scope.Created; // drawn now when no member asked for it: the object is finished, so nothing in it moves
            var reference = scope.Faker.ReferenceDate;
            foreach (var (lifecycle, walk, order) in walks)
            {
                var path = PathTo(lifecycle.Member, positions: true);
                var named = PathTo(lifecycle.Member, positions: false);
                var times = walk.Times(created, reference);
                for (var step = 1; step < walk.States.Length; step++)
                {
                    var transition = new Transition(
                        lifecycle.Member, path, named, owner, lifecycle.StateAt(walk.States[step - 1]), lifecycle.StateAt(walk.States[step]), times[step]);
                    Transitions.Add((transition, order, step));
                }
            }
        }
    }
}
