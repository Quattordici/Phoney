using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Database-related values (faker.js <c>database</c>).</summary>
public sealed class DatabaseModule : FakerModule
{
    internal DatabaseModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a column name, e.g. <c>createdAt</c>.</summary>
    public string Column() => Faker.Pick(DataKeys.DatabaseColumn);

    /// <summary>Returns a column type, e.g. <c>timestamp</c>.</summary>
    public string Type() => Faker.Pick(DataKeys.DatabaseType);

    /// <summary>Returns a collation, e.g. <c>utf8_unicode_ci</c>.</summary>
    public string Collation() => Faker.Pick(DataKeys.DatabaseCollation);

    /// <summary>Returns a database engine, e.g. <c>InnoDB</c>.</summary>
    public string Engine() => Faker.Pick(DataKeys.DatabaseEngine);

    /// <summary>Returns a MongoDB ObjectId: 24 lower-case hexadecimal characters.</summary>
    public string MongoDbObjectId() => Faker.String.Hexadecimal(24, Casing.Lower, prefix: "");
}
