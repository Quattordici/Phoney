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

/// <summary>An entity whose navigation has a getter only: Phoney fills such collections in place, which seed data must not contain.</summary>
public sealed class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Book> Books { get; } = [];
    public List<string> Aliases { get; } = [];
}

public sealed class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int AuthorId { get; set; }
    public Author? Author { get; set; }
}

/// <summary>An entity that refers to another row of its own table.</summary>
public sealed class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ManagerId { get; set; }
    public Employee? Manager { get; set; }
}

/// <summary>An entity with a one-to-one link to another row of its own table, so its foreign key has a unique index.</summary>
public sealed class Apprentice
{
    public int Id { get; set; }
    public int? MentorId { get; set; }
    public Apprentice? Mentor { get; set; }
}

/// <summary>An entity whose link to its own table is required although the CLR property is nullable.</summary>
public sealed class Crew
{
    public int Id { get; set; }
    public int? LeadId { get; set; }
    public Crew? Lead { get; set; }
}

/// <summary>Both sides of a many-to-many link: EF maps the lists as skip navigations through a join table.</summary>
public sealed class Course
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<Student> Students { get; set; } = [];
}

public sealed class Student
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public List<Course> Courses { get; set; } = [];
}

public sealed class ShopContext(DbContextOptions<ShopContext> options, Action<ModelBuilder> seed) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

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
    public void Navigations_with_a_getter_only_are_not_filled()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Author> authors = [];
        using var context = Context(connection, mb =>
        {
            authors = mb.Entity<Author>().HasFakeData(5);
            mb.Entity<Book>().HasFakeData(10);
        });

        context.Database.EnsureCreated().ShouldBeTrue();

        context.Authors.Count().ShouldBe(5);
        context.Books.Count().ShouldBe(10);
        authors.ShouldAllBe(a => a.Books.Count == 0); // a navigation is not seed data
    }

    [Test]
    public void Many_to_many_navigations_are_not_filled()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Course> courses = [];
        IReadOnlyList<Student> students = [];
        using var context = Context(connection, mb =>
        {
            courses = mb.Entity<Course>().HasFakeData(4);
            students = mb.Entity<Student>().HasFakeData(6);
        });

        context.Database.EnsureCreated().ShouldBeTrue();

        context.Set<Course>().Count().ShouldBe(4);
        context.Set<Student>().Count().ShouldBe(6);
        courses.ShouldAllBe(c => c.Students.Count == 0 && c.Title.Length > 0); // skip navigations are not seed data
        students.ShouldAllBe(s => s.Courses.Count == 0 && s.FirstName.Length > 0);
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

    [Test]
    public void Foreign_keys_to_the_same_table_point_at_an_earlier_seeded_row()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Employee> employees = [];
        using var context = Context(connection, mb =>
        {
            mb.Entity<Employee>().HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId);
            employees = mb.Entity<Employee>().HasFakeData(10);
        });

        context.Database.EnsureCreated().ShouldBeTrue();

        context.Set<Employee>().Count().ShouldBe(10);
        employees[0].ManagerId.ShouldBeNull(); // the first row has no earlier one to refer to
        for (var i = 1; i < employees.Count; i++)
            employees[i].ManagerId.ShouldBeOneOf([null, .. employees.Take(i).Select(e => (int?)e.Id)]);
        employees.Skip(1).ShouldContain(e => e.ManagerId != null);
    }

    [Test]
    public void Strings_are_cut_without_splitting_a_surrogate_pair()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        IReadOnlyList<Customer> customers = [];
        using var context = Context(connection, mb => customers = mb.Entity<Customer>().HasFakeData(1, g => g.With(c => c.Email, "abcdefghijk\U0001F600")));

        _ = context.Model;

        customers[0].Email.ShouldBe("abcdefghijk");
    }

    [Test]
    public void One_to_one_foreign_keys_to_the_same_table_stay_unique()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Apprentice> apprentices = [];
        using var context = Context(connection, mb =>
        {
            mb.Entity<Apprentice>().HasOne(a => a.Mentor).WithOne().HasForeignKey<Apprentice>(a => a.MentorId);
            apprentices = mb.Entity<Apprentice>().HasFakeData(10);
        });

        context.Database.EnsureCreated().ShouldBeTrue(); // the unique index rejects two rows with the same mentor

        var mentors = apprentices.Where(a => a.MentorId != null).Select(a => a.MentorId).ToList();
        mentors.ShouldBeUnique();
        mentors.ShouldNotBeEmpty();
    }

    [Test]
    public void Required_links_to_the_same_table_are_set_even_when_the_generated_value_is_null()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Crew> crews = [];
        using var context = Context(connection, mb =>
        {
            mb.Entity<Crew>().HasOne(c => c.Lead).WithMany().HasForeignKey(c => c.LeadId).IsRequired();
            crews = mb.Entity<Crew>().HasFakeData(6, g => g.With(c => c.LeadId, (int?)null));
        });

        context.Database.EnsureCreated().ShouldBeTrue(); // a null in the required column would fail

        crews[0].LeadId.ShouldBe(crews[0].Id); // the first row can only point at itself
        for (var i = 1; i < crews.Count; i++)
            crews[i].LeadId.ShouldBeOneOf([.. crews.Take(i).Select(c => (int?)c.Id)]);
    }

    [Test]
    public void Links_to_rows_of_an_earlier_call_for_the_same_table_are_kept()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        IReadOnlyList<Employee> second = [];
        using var context = Context(connection, mb =>
        {
            mb.Entity<Employee>().HasOne(e => e.Manager).WithMany().HasForeignKey(e => e.ManagerId);
            mb.Entity<Employee>().HasFakeData(3);
            second = mb.Entity<Employee>().HasFakeData(3, g => g.Sequence(e => e.Id, i => (int)i + 100).With(e => e.ManagerId, (int?)1));
        });

        context.Database.EnsureCreated().ShouldBeTrue();

        second.ShouldAllBe(e => e.ManagerId == 1);
        context.Set<Employee>().Count().ShouldBe(6);
    }
}
