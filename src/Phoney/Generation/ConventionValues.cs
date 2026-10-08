using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Phoney.Generation;

/// <summary>
/// Produces member values from a <see cref="ConventionKind"/> and the member's type. Both the reflection model and
/// source-generated models call these typed entry points, so both paths generate identical data.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConventionValues
{
    /// <summary>Returns a string for <paramref name="kind"/>; a single word when there is no convention.</summary>
    public static string String(FakeScope scope, ConventionKind kind)
    {
        var f = scope.Faker;
        try
        {
            return Effective(scope, kind) switch
            {
                ConventionKind.FirstName => scope.Identity.FirstName,
                ConventionKind.LastName => scope.Identity.LastName,
                ConventionKind.MiddleName => f.Person.MiddleName(scope.Identity.Sex),
                ConventionKind.FullName => f.Person.FullName(scope.Identity.FirstName, scope.Identity.LastName, scope.Identity.Sex),
                ConventionKind.Prefix => f.Person.Prefix(scope.Identity.Sex),
                ConventionKind.Suffix => f.Person.Suffix(),
                ConventionKind.Gender => f.Person.Gender(),
                ConventionKind.Sex => f.Person.SexLabel(),
                ConventionKind.JobTitle => f.Person.JobTitle(),
                ConventionKind.Bio => f.Person.Bio(),
                ConventionKind.Username => scope.Identity.Username,
                ConventionKind.DisplayName => f.Internet.DisplayName(scope.Identity.FirstName, scope.Identity.LastName),
                ConventionKind.Email => scope.Identity.Email,
                ConventionKind.Password => f.Internet.Password(),
                ConventionKind.Phone => f.Phone.Number(),
                ConventionKind.StreetAddress => f.Location.StreetAddress(),
                ConventionKind.SecondaryAddress => f.Location.SecondaryAddress(),
                ConventionKind.City => f.Location.City(),
                ConventionKind.ZipCode => f.Location.ZipCode(),
                ConventionKind.State => f.Location.State(),
                ConventionKind.County => f.Location.County(),
                ConventionKind.Country => f.Location.Country(),
                ConventionKind.CountryCode => f.Location.CountryCode(),
                ConventionKind.TimeZone => f.Location.TimeZone(),
                ConventionKind.Language => f.Location.Language().Alpha2,
                ConventionKind.Latitude => f.Location.Latitude().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConventionKind.Longitude => f.Location.Longitude().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConventionKind.CompanyName => f.Company.Name(),
                ConventionKind.Department => f.Commerce.Department(),
                ConventionKind.ProductName => f.Commerce.ProductName(),
                ConventionKind.ProductDescription => f.Commerce.ProductDescription(),
                ConventionKind.Title => f.Lorem.Sentence(f.Random.Int(2, 5)).TrimEnd('.'),
                ConventionKind.Description => f.Lorem.Sentences(2),
                ConventionKind.Paragraph => f.Lorem.Paragraph(),
                ConventionKind.Word => f.Lorem.Word(),
                ConventionKind.Slug => f.Lorem.Slug(),
                ConventionKind.Url => f.Internet.Url(),
                ConventionKind.DomainName => f.Internet.DomainName(),
                ConventionKind.Ip => f.Internet.Ip(),
                ConventionKind.Ipv4 => f.Internet.Ipv4(),
                ConventionKind.Ipv6 => f.Internet.Ipv6(),
                ConventionKind.Mac => f.Internet.Mac(),
                ConventionKind.UserAgent => f.Internet.UserAgent(),
                ConventionKind.Avatar => f.Image.Avatar(scope.Identity.Sex),
                ConventionKind.ImageUrl => f.Image.Url(),
                ConventionKind.Color => f.Color.Human(),
                ConventionKind.HexColor => f.Color.Hex(),
                ConventionKind.Emoji => f.Internet.Emoji(),
                ConventionKind.CurrencyCode => f.Finance.CurrencyCode(),
                ConventionKind.Iban => f.Finance.Iban(),
                ConventionKind.Bic => f.Finance.Bic(),
                ConventionKind.CreditCardNumber => f.Finance.CreditCardNumber(),
                ConventionKind.Cvv => f.Finance.CreditCardCvv(),
                ConventionKind.AccountNumber => f.Finance.AccountNumber(),
                ConventionKind.RoutingNumber => f.Finance.RoutingNumber(),
                ConventionKind.Pin => f.Finance.Pin(),
                ConventionKind.Isbn => f.Commerce.Isbn(),
                ConventionKind.Upc => f.Commerce.Upc(),
                ConventionKind.Sku => f.Random.Replace("???-#####"),
                ConventionKind.Vin => f.Vehicle.Vin(),
                ConventionKind.LicensePlate => f.Vehicle.Vrm(),
                ConventionKind.Uuid or ConventionKind.Id or ConventionKind.ReferenceId => f.Random.Guid().ToString(),
                ConventionKind.Token => f.String.AlphaNumeric(32),
                ConventionKind.Version => f.System.Semver(),
                ConventionKind.FileName => f.System.CommonFileName(),
                ConventionKind.FilePath => f.System.FilePath(),
                ConventionKind.MimeType => f.System.MimeType(),
                ConventionKind.FileExtension => f.System.CommonFileExt(),
                _ => f.Lorem.Word(),
            };
        }
        catch (PhoneyDataUnavailableException)
        {
            return ""; // e.g. a Prefix member in a locale without name prefixes
        }
    }

    /// <summary>Returns a number for <paramref name="kind"/>; 1–1000 (two decimals for non-integers) when there is no convention.</summary>
    public static T Number<T>(FakeScope scope, ConventionKind kind)
        where T : INumber<T>
    {
        var f = scope.Faker;
        var fractional = typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Half);
        return Effective(scope, kind) switch
        {
            ConventionKind.Id => T.CreateSaturating(scope.IsNested ? f.Random.Long(1, int.MaxValue) : scope.Index + 1),
            ConventionKind.ReferenceId => T.CreateSaturating(f.Random.Long(1, 100_000)),
            ConventionKind.Age => T.CreateSaturating(f.Random.Int(18, 80)),
            ConventionKind.Price => T.CreateSaturating(f.Commerce.Price()),
            ConventionKind.Quantity => T.CreateSaturating(f.Random.Int(1, 100)),
            ConventionKind.Rating => fractional ? T.CreateSaturating(f.Number.Double(1, 5, 1)) : T.CreateSaturating(f.Random.Int(1, 5)),
            ConventionKind.Year => T.CreateSaturating(f.Random.Int(1950, f.ReferenceDate.Year)),
            ConventionKind.Percentage => fractional ? T.CreateSaturating(f.Number.Double(0, 100, 2)) : T.CreateSaturating(f.Random.Int(0, 100)),
            ConventionKind.Port => T.CreateSaturating(f.Internet.Port()),
            ConventionKind.Latitude => T.CreateSaturating(f.Location.Latitude()),
            ConventionKind.Longitude => T.CreateSaturating(f.Location.Longitude()),
            _ => typeof(T) == typeof(decimal)
                ? T.CreateSaturating(f.Number.Decimal(1, 1000, 2))
                : fractional ? T.CreateSaturating(f.Number.Double(1, 1000, 2)) : T.CreateSaturating(f.Random.Int(1, 1000)),
        };
    }

    /// <summary>Returns a moment for <paramref name="kind"/>: birthdate, past, recent or future; within the past year otherwise.</summary>
    public static DateTimeOffset DateTimeOffset(FakeScope scope, ConventionKind kind)
    {
        var date = scope.Faker.Date;
        return Effective(scope, kind) switch
        {
            ConventionKind.BirthDate => new DateTimeOffset(date.Birthdate().ToDateTime(global::System.TimeOnly.MinValue), global::System.TimeSpan.Zero),
            ConventionKind.RecentDate => date.Recent(30),
            ConventionKind.FutureDate => date.Future(),
            _ => date.Past(),
        };
    }

    /// <summary>Like <see cref="DateTimeOffset"/> as a UTC <see cref="global::System.DateTime"/>.</summary>
    public static DateTime DateTime(FakeScope scope, ConventionKind kind) => DateTimeOffset(scope, kind).UtcDateTime;

    /// <summary>Like <see cref="DateTimeOffset"/> without time of day.</summary>
    public static DateOnly DateOnly(FakeScope scope, ConventionKind kind) =>
        global::System.DateOnly.FromDateTime(DateTimeOffset(scope, kind).UtcDateTime);

    /// <summary>Returns a random time of day.</summary>
    public static TimeOnly TimeOnly(FakeScope scope, ConventionKind kind) => scope.Faker.Date.TimeOfDay();

    /// <summary>Returns a duration between 0 and 24 hours, in whole seconds.</summary>
    public static TimeSpan TimeSpan(FakeScope scope, ConventionKind kind) =>
        global::System.TimeSpan.FromSeconds(scope.Faker.Random.Int(0, 24 * 60 * 60));

    /// <summary>Returns a random boolean.</summary>
    public static bool Bool(FakeScope scope, ConventionKind kind) => scope.Faker.Random.Bool();

    /// <summary>Returns a random letter.</summary>
    public static char Char(FakeScope scope, ConventionKind kind) => scope.Faker.Random.Letter();

    /// <summary>Returns a random version 4 GUID.</summary>
    public static Guid Guid(FakeScope scope, ConventionKind kind) => scope.Faker.Random.Guid();

    /// <summary>Returns a URL; an avatar or image URL when the member name says so.</summary>
    public static Uri Uri(FakeScope scope, ConventionKind kind) => new(Effective(scope, kind) switch
    {
        ConventionKind.Avatar => scope.Faker.Image.Avatar(scope.Identity.Sex),
        ConventionKind.ImageUrl => scope.Faker.Image.Url(),
        _ => scope.Faker.Internet.Url(),
    });

    /// <summary>
    /// Returns a random enum value. For sex/gender enums with <c>Female</c>/<c>Male</c> members the value matches
    /// the object's identity, so <c>Gender</c> agrees with <c>FirstName</c>.
    /// </summary>
    public static TEnum Enum<TEnum>(FakeScope scope, ConventionKind kind)
        where TEnum : struct, Enum
    {
        var values = EnumCache<TEnum>.Values;
        if (values.Length == 0)
            return default;
        if (EnumCache<TEnum>.Female is { } female && EnumCache<TEnum>.Male is { } male &&
            (kind is ConventionKind.Sex or ConventionKind.Gender || EnumCache<TEnum>.IsSexEnum))
            return scope.Identity.Sex == Modules.Sex.Female ? female : male;
        return values[scope.Faker.Random.Index(values.Length)];
    }

    /// <summary>Dispatches to the typed entry points for the reflection model.</summary>
    [RequiresDynamicCode("Generates enum values for types only known at runtime.")]
    internal static object? Boxed(FakeScope scope, Type type, TypeCategory category, ConventionKind kind) => category switch
    {
        TypeCategory.String => String(scope, kind),
        TypeCategory.Char => Char(scope, kind),
        TypeCategory.Bool => Bool(scope, kind),
        TypeCategory.Byte => Number<byte>(scope, kind),
        TypeCategory.SByte => Number<sbyte>(scope, kind),
        TypeCategory.Int16 => Number<short>(scope, kind),
        TypeCategory.UInt16 => Number<ushort>(scope, kind),
        TypeCategory.Int32 => Number<int>(scope, kind),
        TypeCategory.UInt32 => Number<uint>(scope, kind),
        TypeCategory.Int64 => Number<long>(scope, kind),
        TypeCategory.UInt64 => Number<ulong>(scope, kind),
        TypeCategory.Single => Number<float>(scope, kind),
        TypeCategory.Double => Number<double>(scope, kind),
        TypeCategory.Decimal => Number<decimal>(scope, kind),
        TypeCategory.Guid => Guid(scope, kind),
        TypeCategory.DateTime => DateTime(scope, kind),
        TypeCategory.DateTimeOffset => DateTimeOffset(scope, kind),
        TypeCategory.DateOnly => DateOnly(scope, kind),
        TypeCategory.TimeOnly => TimeOnly(scope, kind),
        TypeCategory.TimeSpan => TimeSpan(scope, kind),
        TypeCategory.Uri => Uri(scope, kind),
        TypeCategory.Enum => BoxedEnum(scope, type, kind),
        _ => null,
    };

    /// <summary>Random enum value for a runtime type, matching the identity for sex/gender enums.</summary>
    [RequiresDynamicCode("Reads enum values for types only known at runtime.")]
    private static object? BoxedEnum(FakeScope scope, Type type, ConventionKind kind)
    {
        var values = global::System.Enum.GetValues(type);
        if (values.Length == 0)
            return global::System.Enum.ToObject(type, 0);
        var names = global::System.Enum.GetNames(type);
        var female = System.Array.FindIndex(names, n => n.Equals("Female", StringComparison.OrdinalIgnoreCase));
        var male = System.Array.FindIndex(names, n => n.Equals("Male", StringComparison.OrdinalIgnoreCase));
        if (female >= 0 && male >= 0 && (kind is ConventionKind.Sex or ConventionKind.Gender || IsSexEnumName(type.Name)))
            return global::System.Enum.Parse(type, names[scope.Identity.Sex == Modules.Sex.Female ? female : male]);
        return values.GetValue(scope.Faker.Random.Index(values.Length));
    }

    /// <summary>Applies the generator's "use conventions" switch.</summary>
    private static ConventionKind Effective(FakeScope scope, ConventionKind kind) =>
        scope.Settings.UseConventions ? kind : ConventionKind.None;

    /// <summary>Whether an enum's name suggests it describes sex or gender.</summary>
    private static bool IsSexEnumName(string name) =>
        name.Contains("Gender", StringComparison.OrdinalIgnoreCase) || name.Contains("Sex", StringComparison.OrdinalIgnoreCase);

    /// <summary>Per-enum cache so enum generation allocates nothing.</summary>
    private static class EnumCache<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly TEnum[] Values = global::System.Enum.GetValues<TEnum>();
        public static readonly TEnum? Female = Find("Female");
        public static readonly TEnum? Male = Find("Male");
        public static readonly bool IsSexEnum = IsSexEnumName(typeof(TEnum).Name);

        /// <summary>The defined value named <paramref name="name"/>, ignoring case.</summary>
        private static TEnum? Find(string name) =>
            global::System.Enum.TryParse<TEnum>(name, ignoreCase: true, out var value) && global::System.Enum.IsDefined(value) ? value : null;
    }
}
