using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Phoney.EntityFrameworkCore.Tests;

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<Order> Orders { get; set; } = [];
    public Address? Address { get; set; }
}

public sealed class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Total { get; set; }
    public string? Note { get; set; }
}

public sealed class Product
{
    [Key]
    public int ProductNumber { get; set; }
    public Guid ExternalId { get; set; }
    public string ProductName { get; set; } = "";
}

public sealed class Tag
{
    [Key]
    public string Code { get; set; } = "";
}

public sealed class ShopContext(DbContextOptions<ShopContext> options, Action<ModelBuilder> seed) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().OwnsOne(c => c.Address);
        modelBuilder.Entity<Customer>().Property(c => c.Email).HasMaxLength(12);
        seed(modelBuilder);
    }
}

/// <summary>Uses a model cache key per seed delegate so every test builds its own model.</summary>
public sealed class PerSeedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), context.GetHashCode(), designTime);
}

public sealed class HasFakeDataTests
{
    private static ShopContext Context(SqliteConnection connection, Action<ModelBuilder> seed)
    {
        var options = new DbContextOptionsBuilder<ShopContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, PerSeedModelCacheKeyFactory>()
            .Options;
        return new ShopContext(options, seed);
    }

    [Test]
    public void Seeds_rows_that_the_database_accepts_with_linked_foreign_keys()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Customer> customers = [];
        using var context = Context(connection, mb =>
        {
            customers = mb.Entity<Customer>().HasFakeData(20);
            mb.Entity<Order>().HasFakeData(50);
        });

        context.Database.EnsureCreated().ShouldBeTrue();

        context.Customers.Count().ShouldBe(20);
        context.Orders.Count().ShouldBe(50);
        var ids = customers.Select(c => c.Id).ToHashSet();
        context.Orders.AsEnumerable().ShouldAllBe(o => ids.Contains(o.CustomerId));
        customers.ShouldAllBe(c => c.Orders.Count == 0 && c.Address == null);
        customers.ShouldAllBe(c => c.Email.Length <= 12);
        customers.Select(c => c.Id).ShouldBe(Enumerable.Range(1, 20));
    }

    [Test]
    public void Seed_data_is_the_same_every_time_the_model_is_built()
    {
        static string Seed()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            using var context = Context(connection, mb =>
            {
                mb.Entity<Customer>().HasFakeData(5);
                mb.Entity<Order>().HasFakeData(5);
            });
            var data = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
                .Where(e => !e.IsOwned())
                .OrderBy(e => e.Name)
                .SelectMany(e => e.GetSeedData().Select(row => e.Name + ":" + string.Join(",", row.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))));
            return string.Join("\n", data);
        }

        var first = Seed();
        first.ShouldContain("Email=");
        Seed().ShouldBe(first);
    }

    [Test]
    public void Rules_and_explicit_links_are_kept()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Order> orders = [];
        using var context = Context(connection, mb =>
        {
            var customers = mb.Entity<Customer>().HasFakeData(3, g => g.With(c => c.FirstName, "Ada"));
            orders = mb.Entity<Order>().HasFakeData(10, g => g.WithOneOf(o => o.CustomerId, [customers[1].Id]), seed: 99);
        });

        context.Database.EnsureCreated();
        context.Customers.AsEnumerable().ShouldAllBe(c => c.FirstName == "Ada");
        orders.ShouldAllBe(o => o.CustomerId == 2);
    }

    [Test]
    public void Integer_keys_are_unique_and_other_keys_are_checked()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Product> products = [];
        using var context = Context(connection, mb =>
        {
            products = mb.Entity<Product>().HasFakeData(30, g => g.With(p => p.ProductNumber, 7));
        });

        context.Database.EnsureCreated();
        products.Select(p => p.ProductNumber).ShouldBe(Enumerable.Range(1, 30));

        using var bad = Context(connection, mb => mb.Entity<Tag>().HasFakeData(3, g => g.With(t => t.Code, "same")));
        Should.Throw<InvalidOperationException>(() => bad.Model).Message.ShouldContain("Tag.Code");
    }

    [Test]
    public void Arguments_are_validated()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        using var context = Context(connection, mb => mb.Entity<Customer>().HasFakeData(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => context.Model);
    }
}
