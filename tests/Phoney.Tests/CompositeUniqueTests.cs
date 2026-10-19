using Phoney.Generation;

namespace Phoney.Tests;

public sealed class UserRole
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime GrantedAt { get; set; }
}

public sealed record TenantUser(int TenantId, string Email, string FirstName);

[FakeFor<UserRole>]
[FakeFor<TenantUser>]
public static partial class CompositeUniqueFakes;

/// <summary><c>Unique(x =&gt; new { x.A, x.B })</c>: the combination is unique, each member on its own may repeat (join tables, per-tenant keys).</summary>
public sealed class CompositeUniqueTests
{
    private static ObjectGenerator<UserRole> Roles(ObjectGenerator<UserRole> generator) => generator
        .With(x => x.UserId, f => f.Number.Int(1, 20))
        .With(x => x.RoleId, f => f.Number.Int(1, 5));

    [Test]
    public void Combinations_are_unique_while_the_members_repeat()
    {
        var rows = Roles(Fake.For<UserRole>()).Seed(1).Unique(x => new { x.UserId, x.RoleId }).Generate(90);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBe(90);
        rows.Select(r => r.UserId).Distinct().Count().ShouldBeLessThan(90);
    }

    [Test]
    public void Without_the_rule_the_same_seed_has_duplicate_pairs()
    {
        // The case the rule exists for: 60 rows over 20 × 5 pairs collide.
        var rows = Roles(Fake.For<UserRole>()).Seed(1).Generate(60);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBeLessThan(60);
    }

    [Test]
    public void Objects_without_a_duplicate_are_the_ones_you_get_without_the_rule()
    {
        var plain = Roles(Fake.For<UserRole>()).Seed(2).Generate(100);
        var unique = Roles(Fake.For<UserRole>()).Seed(2).Unique(x => new { x.UserId, x.RoleId }).Generate(100);

        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < plain.Count && seen.Add((plain[i].UserId, plain[i].RoleId)); i++)
            unique[i].GrantedAt.ShouldBe(plain[i].GrantedAt);
        seen.Count.ShouldBeGreaterThan(1);
    }

    [Test]
    public void Every_pair_can_be_used_up()
    {
        var rows = Roles(Fake.For<UserRole>()).Seed(3).Unique(x => new { x.UserId, x.RoleId }).Generate(100);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBe(100);
    }

    [Test]
    public void Running_out_of_combinations_names_the_members()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            Roles(Fake.For<UserRole>()).Seed(4).Unique(x => new { x.UserId, x.RoleId }, maxAttempts: 50).Generate(101));

        ex.Message.ShouldContain("unique combination of UserId and RoleId");
        ex.Message.ShouldContain("50 attempts");
    }

    [Test]
    public void Constructor_parameters_and_identity_members_can_be_part_of_the_key()
    {
        var users = Fake.For<TenantUser>().Seed(5)
            .With(x => x.TenantId, f => f.Number.Int(1, 2))
            .Unique(x => new { x.TenantId, x.FirstName })
            .Generate(1_000);

        users.Select(u => (u.TenantId, u.FirstName)).Distinct().Count().ShouldBe(1_000);
    }

    [Test]
    public void Reflection_and_generated_models_agree()
    {
        var reflection = Roles(new ObjectGenerator<UserRole>(new ReflectionModel<UserRole>())).Seed(6).Unique(x => new { x.UserId, x.RoleId }).Generate(80);
        var generated = Roles(CompositeUniqueFakes.UserRole).Seed(6).Unique(x => new { x.UserId, x.RoleId }).Generate(80);

        generated.Select(r => (r.UserId, r.RoleId, r.GrantedAt)).ShouldBe(reflection.Select(r => (r.UserId, r.RoleId, r.GrantedAt)));

        var reflectedUsers = new ObjectGenerator<TenantUser>(new ReflectionModel<TenantUser>()).Seed(7)
            .With(x => x.TenantId, f => f.Number.Int(1, 2)).Unique(x => new { x.TenantId, x.FirstName }).Generate(300);
        var generatedUsers = CompositeUniqueFakes.TenantUser.Seed(7)
            .With(x => x.TenantId, f => f.Number.Int(1, 2)).Unique(x => new { x.TenantId, x.FirstName }).Generate(300);
        generatedUsers.ShouldBe(reflectedUsers);
    }

    [Test]
    public void Values_set_after_creation_are_what_must_be_unique()
    {
        var rows = Fake.For<UserRole>().Seed(8)
            .With(x => x.UserId, f => f.Number.Int(1, 3))
            .With(x => x.RoleId, (f, r) => r.UserId * 10 + f.Number.Int(1, 3))
            .Unique(x => new { x.UserId, x.RoleId })
            .Generate(9);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBe(9);
    }

    [Test]
    public void Populate_keeps_the_combinations_unique()
    {
        var generator = Roles(Fake.For<UserRole>()).Seed(9).Unique(x => new { x.UserId, x.RoleId });
        var rows = generator.Populate(Enumerable.Range(0, 100).Select(_ => new UserRole()).ToList(), PopulateMode.All);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBe(100);
    }

    [Test]
    public void Parallel_generation_keeps_the_combinations_unique()
    {
        var rows = Roles(Fake.For<UserRole>()).Seed(10).Unique(x => new { x.UserId, x.RoleId }).GenerateParallel(100);

        rows.Select(r => (r.UserId, r.RoleId)).Distinct().Count().ShouldBe(100);
    }

#pragma warning disable PHONEY007 // the analyzer reports these at compile time; the test pins what happens at runtime
    [Test]
    public void Members_of_nested_objects_or_computed_values_are_refused()
    {
        Should.Throw<ArgumentException>(() => Fake.For<UserRole>().Unique(x => new { x.UserId, Next = x.RoleId + 1 }))
            .Message.ShouldContain("must select members of UserRole");
        Should.Throw<ArgumentException>(() => Fake.For<UserRole>().Unique(x => new { x.UserId, x.GrantedAt.Year }))
            .Message.ShouldContain("must select members of UserRole");
    }
#pragma warning restore PHONEY007

    [Test]
    public void Keyed_generation_refuses_combinations_like_single_members() =>
        Should.Throw<InvalidOperationException>(() => Fake.For<UserRole>().Unique(x => new { x.UserId, x.RoleId }).GenerateFor("k"))
            .Message.ShouldContain("Unique can't be combined with GenerateFor");
}
