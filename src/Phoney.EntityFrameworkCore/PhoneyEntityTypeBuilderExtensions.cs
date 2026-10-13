using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Phoney;
using Phoney.Generation;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Seeds EF Core entity types with Phoney data through <c>HasData</c>.</summary>
public static class PhoneyEntityTypeBuilderExtensions
{
    private const string ReflectionMessage = "Builds EF Core seed data with Phoney generators, which use reflection for types without a source-generated model.";

    /// <summary>
    /// Generates <paramref name="count"/> entities and registers them as seed data (<c>HasData</c>), ready for migrations
    /// and <c>EnsureCreated</c>. Returns them so other entities can refer to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The data is the same every time the model is built (the seed defaults to one derived from the entity's type name),
    /// so migrations don't change between builds. Navigations, owned and nested objects are left empty because
    /// <c>HasData</c> only takes scalar values; strings are cut to their configured maximum length; a single integer
    /// primary key is numbered 1…n when its values are not unique; and a foreign key that matches no seeded principal
    /// is pointed at one (seed principals first).
    /// </para>
    /// <para>Call it after the entity's other configuration (keys, maximum lengths, owned types), which it reads.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var customers = modelBuilder.Entity&lt;Customer&gt;().HasFakeData(50);
    /// modelBuilder.Entity&lt;Order&gt;().HasFakeData(200, g =&gt; g.With(o =&gt; o.Status, OrderStatus.Paid));
    /// </code>
    /// </example>
    /// <param name="builder">The entity type to seed.</param>
    /// <param name="count">How many entities to generate.</param>
    /// <param name="configure">Configures the generator, e.g. rules or a locale; it is already seeded.</param>
    /// <param name="seed">Seed for the data; default: derived from the entity's type name.</param>
    /// <exception cref="InvalidOperationException">A key other than a single integer has default or duplicate values.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static IReadOnlyList<T> HasFakeData<T>(this EntityTypeBuilder<T> builder, int count, Func<Generator<T>, Generator<T>>? configure = null, long? seed = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var model = ModelRegistry.For<T>();
        var dataSeed = seed ?? StableSeed(typeof(T).FullName ?? typeof(T).Name);
        var generator = new Generator<T>(model).Seed(dataSeed);
        if (configure is not null)
            generator = configure(generator) ?? throw new InvalidOperationException($"The configure delegate for {typeof(T).Name} returned null; return the configured generator.");

        var items = generator.Generate(count);
        var entityType = builder.Metadata;
        var seeder = new Seeder<T>(entityType, model, items, dataSeed);
        seeder.ClearNavigations();
        seeder.ClipStrings();
        seeder.FixKeys();
        seeder.LinkForeignKeys();
        builder.HasData(items);
        return items;
    }

    /// <summary>FNV-1a hash of a name: stable across processes, unlike <see cref="string.GetHashCode()"/>.</summary>
    internal static long StableSeed(string name)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in name)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return unchecked((long)hash);
    }

    /// <summary>Adjusts generated entities to what <c>HasData</c> and the database accept; reads the property types of the EF model with reflection.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    private sealed class Seeder<T>(IMutableEntityType entityType, FakeModel<T> model, List<T> items, long seed)
    {
        private readonly Dictionary<string, int> _members = model.Members.Select((m, i) => (m.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

        /// <summary>Navigations, owned types and other objects are seeded separately (or not at all), so they are emptied.</summary>
        public void ClearNavigations()
        {
            var navigations = entityType.GetNavigations().Select(n => n.Name)
                .Concat(entityType.GetSkipNavigations().Select(n => n.Name))
                .ToHashSet(StringComparer.Ordinal);
            var complex = ComplexPropertyNames();
            for (var i = 0; i < model.Members.Count; i++)
            {
                var member = model.Members[i];
                if (!member.IsSettable || complex.Contains(member.Name))
                    continue;
                if (!navigations.Contains(member.Name) && !member.IsNested && !IsObjectCollection(member.Type))
                    continue;
                for (var j = 0; j < items.Count; j++)
                {
                    // Collections are emptied in place (EF accepts empty ones), so the returned entities stay usable.
                    if (model.Get(items[j], i) is IList { IsReadOnly: false, IsFixedSize: false } list)
                        list.Clear();
                    else
                        items[j] = model.Set(items[j], i, null);
                }
            }
        }

        /// <summary>Cuts strings to the maximum length configured in the model.</summary>
        public void ClipStrings()
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType != typeof(string) || property.GetMaxLength() is not { } max || !TryMember(property.Name, out var index))
                    continue;
                for (var j = 0; j < items.Count; j++)
                {
                    if (model.Get(items[j], index) is string text && text.Length > max)
                        items[j] = model.Set(items[j], index, text[..max]);
                }
            }
        }

        /// <summary>Numbers a single integer key 1…n when its values clash; checks other keys.</summary>
        public void FixKeys()
        {
            if (entityType.FindPrimaryKey() is not { } key || items.Count == 0)
                return;
            var indices = new List<int>();
            foreach (var property in key.Properties)
            {
                if (!TryMember(property.Name, out var index))
                    return; // shadow key: EF reports what's missing
                indices.Add(index);
            }

            var values = items.Select(item => string.Join("|", indices.Select(i => model.Get(item, i)))).ToList();
            var valid = values.Distinct().Count() == values.Count && items.All(item => indices.All(i => !model.IsUnset(item, i)));
            if (valid)
                return;

            if (indices.Count == 1 && IsInteger(key.Properties[0].ClrType))
            {
                var type = Nullable.GetUnderlyingType(key.Properties[0].ClrType) ?? key.Properties[0].ClrType;
                for (var j = 0; j < items.Count; j++)
                    items[j] = model.Set(items[j], indices[0], Convert.ChangeType(j + 1, type, System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            var names = string.Join(", ", key.Properties.Select(p => $"{typeof(T).Name}.{p.Name}"));
            throw new InvalidOperationException(
                $"Seed data for {typeof(T).Name} needs unique, non-default keys, but {names} has default or duplicate values. Make it unique with .Unique(x => x.{key.Properties[0].Name}) or set it with .With(...).");
        }

        /// <summary>Points single-property foreign keys that match no seeded principal at one, reproducibly.</summary>
        public void LinkForeignKeys()
        {
            var random = new Randomizer(seed ^ 0x5EED_F00D);
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey.Properties.Count != 1 || foreignKey.PrincipalKey.Properties.Count != 1 || !TryMember(foreignKey.Properties[0].Name, out var index))
                    continue;
                var principalName = foreignKey.PrincipalKey.Properties[0].Name;
                var principals = foreignKey.PrincipalEntityType.GetSeedData()
                    .Select(row => row.TryGetValue(principalName, out var value) ? value : null)
                    .Where(value => value is not null)
                    .ToList();
                if (principals.Count == 0)
                    continue;
                var known = principals.ToHashSet();
                for (var j = 0; j < items.Count; j++)
                {
                    var value = model.Get(items[j], index);
                    if (value is null || known.Contains(value))
                        continue; // optional and unset, or already linked
                    items[j] = model.Set(items[j], index, principals[random.Index(principals.Count)]);
                }
            }
        }

        private bool TryMember(string name, out int index) => _members.TryGetValue(name, out index) && model.Members[index].IsSettable;

        /// <summary>Complex properties are values of the entity itself, so they are kept.</summary>
        private HashSet<string> ComplexPropertyNames() => entityType.GetComplexProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        private static bool IsInteger(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort);
        }

        /// <summary>Collections of objects (not of strings, numbers…), which are navigations or unmapped.</summary>
        private static bool IsObjectCollection(Type type)
        {
            if (type == typeof(string) || type == typeof(byte[]) || !typeof(IEnumerable).IsAssignableFrom(type))
                return false;
            var element = type.IsArray
                ? type.GetElementType()
                : type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
            if (element is null)
                return false;
            element = Nullable.GetUnderlyingType(element) ?? element;
            return !(element.IsPrimitive || element.IsEnum || element == typeof(string) || element == typeof(decimal) || element == typeof(Guid)
                || element == typeof(DateTime) || element == typeof(DateTimeOffset) || element == typeof(DateOnly) || element == typeof(TimeOnly)
                || element == typeof(TimeSpan) || element == typeof(Uri) || element == typeof(byte[]));
        }
    }
}
