using System.Collections;
using System.Linq.Expressions;
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
    /// is pointed at one (seed principals first; a key to the same table points at an earlier row).
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
    public static IReadOnlyList<T> HasFakeData<T>(this EntityTypeBuilder<T> builder, int count, Func<ObjectGenerator<T>, ObjectGenerator<T>>? configure = null, long? seed = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var model = ModelRegistry.For<T>();
        var dataSeed = seed ?? StableSeed(typeof(T).FullName ?? typeof(T).Name);
        var generator = IgnoreFilledNavigations(new ObjectGenerator<T>(model).Seed(dataSeed), model, builder.Metadata);
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

    /// <summary>
    /// Navigations and other object collections that only have a getter are filled in place by Phoney, but seed data cannot hold them, so they are ignored (before the
    /// caller's configuration, which can still give them a rule). Settable ones are emptied after generation instead.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    private static ObjectGenerator<T> IgnoreFilledNavigations<T>(ObjectGenerator<T> generator, FakeModel<T> model, IMutableEntityType entityType)
    {
        var navigations = entityType.GetNavigations().Select(n => n.Name).Concat(entityType.GetSkipNavigations().Select(n => n.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var member in model.Members)
        {
            if (!member.FillsExisting || !(navigations.Contains(member.Name) || Seeder<T>.IsObjectCollection(member.Type)))
                continue;
            var parameter = Expression.Parameter(typeof(T), "e");
            var access = Expression.Convert(Expression.PropertyOrField(parameter, member.Name), typeof(object));
            generator = generator.Ignore(Expression.Lambda<Func<T, object>>(access, parameter));
        }

        return generator;
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
                        items[j] = model.Set(items[j], index, text[..(max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)]); // keeps a surrogate pair whole
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

        /// <summary>
        /// Points single-property foreign keys that match no seeded principal at one, reproducibly. A key to the entity's own table (<c>ManagerId</c>) points
        /// at an earlier row, so the rows can be inserted in order; the first row gets <see langword="null"/>, or itself when the key is required.
        /// </summary>
        public void LinkForeignKeys()
        {
            var random = new SeededRandom(seed ^ 0x5EED_F00D);
            var ownTable = new List<(IMutableForeignKey Key, int Index)>();
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey.Properties.Count != 1 || foreignKey.PrincipalKey.Properties.Count != 1 || !TryMember(foreignKey.Properties[0].Name, out var index))
                    continue;
                if (foreignKey.PrincipalEntityType == entityType) // a key to a base type keeps the base's seeded rows
                {
                    ownTable.Add((foreignKey, index));
                    continue;
                }

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
                    if ((value is null && foreignKey.Properties[0].IsNullable) || (value is not null && known.Contains(value)))
                        continue; // optional and unset, or already linked
                    items[j] = model.Set(items[j], index, principals[random.Index(principals.Count)]);
                }
            }

            // After the other keys, so their draws don't move.
            foreach (var (foreignKey, index) in ownTable)
                LinkToEarlierRows(foreignKey, index, random);
        }

        /// <summary>
        /// Links a key to the entity's own table to an earlier row, drawn at random. A one-to-one key (unique index) points at the row just before instead,
        /// so no two rows share a target; when such a key is required every row points at itself.
        /// </summary>
        private void LinkToEarlierRows(IMutableForeignKey foreignKey, int index, SeededRandom random)
        {
            if (!TryMember(foreignKey.PrincipalKey.Properties[0].Name, out var keyIndex))
                return;
            var nullable = foreignKey.Properties[0].IsNullable;
            // Rows an earlier HasFakeData call for this entity seeded are valid targets too.
            var keyName = foreignKey.PrincipalKey.Properties[0].Name;
            var earlier = entityType.GetSeedData().Select(row => row.TryGetValue(keyName, out var key) ? key : null).OfType<object>().ToHashSet();
            var used = new HashSet<object>();
            for (var j = 0; j < items.Count; j++)
            {
                var value = model.Get(items[j], index);
                var linked = value is not null && earlier.Contains(value) && !(foreignKey.IsUnique && used.Contains(value));
                if ((value is not null || !nullable) && !linked) // a required key needs a target even when its CLR type allowed a null
                {
                    var target = foreignKey.IsUnique
                        ? nullable ? j - 1 : j
                        : j > 0 ? random.Index(j) : nullable ? -1 : 0;
                    items[j] = model.Set(items[j], index, target < 0 ? null : model.Get(items[target], keyIndex));
                }

                if (model.Get(items[j], index) is { } link)
                    used.Add(link);
                if (model.Get(items[j], keyIndex) is { } key)
                    earlier.Add(key);
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
        internal static bool IsObjectCollection(Type type)
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
