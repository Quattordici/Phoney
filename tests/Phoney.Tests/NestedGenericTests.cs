using System.Text.Json;
using Phoney.Generation;

namespace Phoney.Tests;

/// <summary>A response wrapper, the shape of <c>Result&lt;T&gt;</c>, <c>Paged&lt;T&gt;</c> and <c>Response&lt;T&gt;</c> in most APIs.</summary>
public sealed class Envelope<T>
{
    public bool Ok { get; set; }
    public T Value { get; set; } = default!;
    public string Message { get; set; } = "";
    public List<T> Items { get; set; } = [];
}

public sealed record Reply<T>(bool Success, T Payload);

public sealed class Tree<T>
{
    public T Value { get; set; } = default!;
    public Tree<T>? Child { get; set; }
}

public interface IHolder<out T>
{
    T Content { get; }
}

public sealed class ApiReply
{
    public Envelope<Ticket> Data { get; set; } = null!;
    public Envelope<int> Count { get; set; } = null!;
    public Reply<Parcel> Wrapped { get; set; } = null!;
    public Envelope<Envelope<Ticket>> Deep { get; set; } = null!;
    public Tree<string> Branch { get; set; } = null!;
    public List<Envelope<Ticket>> Many { get; set; } = [];
    public Envelope<Ticket>? Maybe { get; set; }
}

[FakeFor<ApiReply>]
public static partial class ApiReplyFakes;

/// <summary>Closed generic classes (<c>Envelope&lt;Ticket&gt;</c>) are objects like any other when they are members.</summary>
public sealed class NestedGenericTests
{
    private static Generator<ApiReply> Generated() => ApiReplyFakes.ApiReply.Seed(31);

    private static Generator<ApiReply> Reflected() => new Generator<ApiReply>(new ReflectionModel<ApiReply>()).Seed(31);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Members_of_a_closed_generic_type_are_created_and_filled(bool generated)
    {
        var replies = (generated ? Generated() : Reflected()).NullProbability(0).Generate(10);

        foreach (var reply in replies)
        {
            reply.Data.ShouldNotBeNull();
            reply.Data.Value.Code.ShouldNotBeNullOrEmpty();
            reply.Data.Message.ShouldNotBeNullOrEmpty();
            reply.Data.Items.ShouldNotBeEmpty();
            reply.Data.Items.ShouldAllBe(t => t.Code.Length > 0);
            reply.Count.Value.ShouldBeGreaterThan(0);
            reply.Count.Items.ShouldAllBe(i => i > 0);
            reply.Wrapped.Payload.Ticket.Code.ShouldNotBeNullOrEmpty(); // a record with constructor parameters
            reply.Deep.Value.Value.Code.ShouldNotBeNullOrEmpty(); // a generic type inside a generic type
            reply.Branch.Value.ShouldNotBeNullOrEmpty();
            reply.Many.ShouldNotBeEmpty();
            reply.Many.ShouldAllBe(e => e.Value.Code.Length > 0);
        }
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_data()
    {
        var generated = JsonSerializer.Serialize(Generated().Generate(8));
        var reflected = JsonSerializer.Serialize(Reflected().Generate(8));

        reflected.ShouldBe(generated);
        generated.ShouldContain("\"Data\":{");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_type_that_contains_itself_stops_instead_of_looping(bool generated)
    {
        var reply = (generated ? Generated() : Reflected()).NullProbability(0).Generate();

        var depth = 0;
        for (var node = reply.Branch; node is not null; node = node.Child)
            depth++;
        depth.ShouldBeInRange(1, 8);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Rules_reach_into_closed_generic_members(bool generated)
    {
        var generator = (generated ? Generated() : Reflected()).NullProbability(0)
            .With(x => x.Data.Message, "fixed")
            .With(x => x.Data.Value.Code, "T-1")
            .With(x => x.Many.Each().Message, "each");

        var reply = generator.Generate();

        reply.Data.Message.ShouldBe("fixed");
        reply.Data.Value.Code.ShouldBe("T-1");
        reply.Many.ShouldAllBe(e => e.Message == "each");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void A_generator_for_the_closed_type_applies_everywhere(bool generated)
    {
        var generator = (generated ? Generated() : Reflected()).NullProbability(0)
            .Use(Fake.For<Envelope<Ticket>>().With(e => e.Message, "from Use"));

        var reply = generator.Generate();

        reply.Data.Message.ShouldBe("from Use");
        reply.Many.ShouldAllBe(e => e.Message == "from Use");
        reply.Deep.Value.Message.ShouldBe("from Use"); // Envelope<Envelope<Ticket>> holds an Envelope<Ticket>
    }

    [Test]
    public void Populate_fills_a_missing_closed_generic_member_and_keeps_an_existing_one()
    {
        var kept = new Envelope<Ticket> { Message = "kept" };

        var reply = Reflected().NullProbability(0).Populate(new ApiReply { Data = kept });

        reply.Data.ShouldBeSameAs(kept);
        reply.Data.Message.ShouldBe("kept");
        reply.Count.ShouldNotBeNull();
        reply.Wrapped.ShouldNotBeNull();
    }

    [Test]
    public void Generic_interfaces_still_come_from_their_subtypes_only()
    {
        // No subtype is known for IHolder<Ticket>, so it stays null as before.
        Fake.For<HolderOwner>().Seed(1).Generate().Holder.ShouldBeNull();
    }

    public sealed class HolderOwner
    {
        public IHolder<Ticket>? Holder { get; set; }
    }
}
