using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// The READMEs' EF Core example, with a model of its own: EF needs keys the docs' shared shop model doesn't declare.
namespace Phoney.EntityFrameworkCore.Tests.Docs;

public sealed class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public List<Order> Orders { get; set; } = [];
}

public enum OrderStatus { Pending, Paid, Shipped }

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class Review
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    [Range(1, 5)] public int Rating { get; set; }
    public string Text { get; set; } = "";
}

public sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Review> Reviews => Set<Review>();

    // snippet: ef-has-fake-data
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Property(c => c.Email).HasMaxLength(100);

        var customers = modelBuilder.Entity<Customer>().HasFakeData(50);
        modelBuilder.Entity<Order>().HasFakeData(200, g => g.With(o => o.Status, OrderStatus.Paid));
        modelBuilder.Entity<Review>().HasFakeData(20, g => g.WithOneOf(r => r.CustomerId, customers, c => c.Id));
    }
    // end snippet
}

public sealed class EntityFrameworkCoreReadmeExamples
{
    [Test]
    public async Task The_seed_data_goes_into_the_database()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ShopContext(new DbContextOptionsBuilder<ShopContext>().UseSqlite(connection).Options);

        await context.Database.EnsureCreatedAsync();

        var customerIds = await context.Customers.Select(c => c.Id).ToListAsync();
        customerIds.Count.ShouldBe(50);
        (await context.Orders.CountAsync(o => o.Status == OrderStatus.Paid)).ShouldBe(200);
        (await context.Orders.Select(o => o.CustomerId).ToListAsync()).ShouldAllBe(id => customerIds.Contains(id));
        (await context.Reviews.Select(r => r.CustomerId).ToListAsync()).ShouldAllBe(id => customerIds.Contains(id));
        (await context.Customers.MaxAsync(c => c.Email.Length)).ShouldBeLessThanOrEqualTo(100);
    }
}
