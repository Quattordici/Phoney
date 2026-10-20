# Phoney.EntityFrameworkCore

Seed [EF Core](https://learn.microsoft.com/ef/core/) models with [Phoney](https://github.com/Quattordici/Phoney) fake data:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Customer>().Property(c => c.Email).HasMaxLength(100);

    var customers = modelBuilder.Entity<Customer>().HasFakeData(50);
    modelBuilder.Entity<Order>().HasFakeData(200, g => g.With(o => o.Status, OrderStatus.Paid));
    modelBuilder.Entity<Review>().HasFakeData(20, g => g.WithOneOf(r => r.CustomerId, customers, c => c.Id));
}
```

- **Stable migrations**: the data is the same every time the model is built. The seed defaults to one derived from the entity's type name; pass `seed:` for other data.
- **Valid for `HasData`**: navigations, owned and nested objects are left empty, strings are cut to their configured maximum length, and a single integer primary key is numbered 1…n when generated values clash. Other keys must come out unique, or you get an exception that says how to fix it.
- **Linked foreign keys**: a foreign key that matches no seeded principal is pointed at one, reproducibly. Seed principals first, or link explicitly with `WithOneOf`. A key to the same table (`ManagerId`) points at an earlier row, and the first row gets `null` (or itself when the key is required), so the rows insert in order; a one-to-one key points at the row just before (or, when required, at the row itself), so no two rows share a target.
- Call `HasFakeData` after the entity's other configuration (keys, `HasMaxLength`, owned types), which it reads. Seed owned types with `OwnsOne(...).HasData(...)`.
- Source-generated models (`[FakeFor<T>]`) are used when present.
