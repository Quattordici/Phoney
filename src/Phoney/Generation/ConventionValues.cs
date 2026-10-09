using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Phoney.Generation;

/// <summary>
/// Produces member values from a <see cref="ConventionKind"/> and the member's type. Both the reflection model and
/// source-generated models call these typed entry points, so both paths generate identical data.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConventionValues
{
    /// <summary>
    /// Returns a string for <paramref name="kind"/> (a single word when there is no convention) that satisfies
    /// <paramref name="constraints"/>: allowed values, pattern, format, base64, length and required.
    /// </summary>
    public static string String(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
    {
        if (constraints is null)
            return StringCore(scope, kind);

        return Avoiding(scope, constraints, () =>
        {
            if (constraints.AllowedValues is { Length: > 0 } allowed)
                return Text(scope.Faker.Random.Element(allowed));
            if (constraints.Pattern is { } pattern)
                return FromPattern(scope, pattern);
            if (constraints.Base64)
            {
                var bytes = new byte[FitByteCount(constraints)];
                scope.Faker.Random.Bytes(bytes);
                return Convert.ToBase64String(bytes);
            }

            return FitLength(scope, constraints, () => StringCore(scope, constraints.Format ?? kind));
        });
    }

    /// <summary>The convention value itself, before constraints.</summary>
    private static string StringCore(FakeScope scope, ConventionKind kind)
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
                ConventionKind.StreetAddress => scope.Identity.Address.Street,
                ConventionKind.SecondaryAddress => f.Location.SecondaryAddress(),
                ConventionKind.City => scope.Identity.Address.City,
                ConventionKind.ZipCode => scope.Identity.Address.ZipCode ?? "",
                ConventionKind.State => scope.Identity.Address.State ?? "",
                ConventionKind.County => f.Location.County(),
                ConventionKind.Country => scope.Identity.Address.Country ?? f.Location.Country(),
                ConventionKind.CountryCode => scope.Identity.Address.CountryCode ?? f.Location.CountryCode(),
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

    /// <summary>
    /// Returns a number for <paramref name="kind"/> (1–1000, two decimals for non-integers, when there is no convention),
    /// or uniformly within <c>[Range]</c> / one of <c>[AllowedValues]</c> when <paramref name="constraints"/> say so.
    /// </summary>
    public static T Number<T>(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
        where T : INumber<T>
    {
        if (constraints is null)
            return NumberCore<T>(scope, kind);

        return Avoiding(scope, constraints, () =>
        {
            if (constraints.AllowedValues is { Length: > 0 } allowed)
                return T.CreateSaturating(Convert.ToDecimal(scope.Faker.Random.Element(allowed), CultureInfo.InvariantCulture));
            if (TryRange(constraints, out double min, out double max))
                return InRange<T>(scope, min, max);
            return NumberCore<T>(scope, kind);
        });
    }

    /// <summary>The convention value itself, before constraints.</summary>
    private static T NumberCore<T>(FakeScope scope, ConventionKind kind)
        where T : INumber<T>
    {
        var f = scope.Faker;
        var fractional = typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Half);
        return Effective(scope, kind) switch
        {
            ConventionKind.Id => T.CreateSaturating(scope.IsNested ? f.Random.Long(1, int.MaxValue) : scope.Index + 1),
            ConventionKind.ReferenceId => T.CreateSaturating(f.Random.Long(1, 100_000)),
            ConventionKind.Age => T.CreateSaturating(scope.Identity.Age), // matches a BirthDate member
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

    /// <summary>
    /// Returns a moment for <paramref name="kind"/>, consistent within the object: the birthdate matches the age,
    /// the first "created" date is the object's creation time, "updated" dates fall between it and the reference
    /// date, and future dates come after both. Within <c>[Range]</c> when <paramref name="constraints"/> has one.
    /// </summary>
    public static DateTimeOffset DateTimeOffset(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
    {
        var date = scope.Faker.Date;
        if (constraints is not null && TryDateRange(constraints, out var from, out var to))
            return date.Between(from, to);

        var reference = scope.Faker.ReferenceDate;
        switch (Effective(scope, kind))
        {
            case ConventionKind.BirthDate:
                return new DateTimeOffset(scope.Identity.BirthDate.ToDateTime(global::System.TimeOnly.MinValue), global::System.TimeSpan.Zero);
            case ConventionKind.RecentDate:
            {
                // Recently, but never before the object was created.
                var earliest = scope.Created > reference.AddDays(-30) ? scope.Created : reference.AddDays(-30);
                return date.Between(earliest, reference);
            }

            case ConventionKind.FutureDate:
                return date.Future();
            case ConventionKind.PastDate when !scope.CreatedClaimed:
                scope.CreatedClaimed = true;
                return scope.Created;
            default:
                return date.Past();
        }
    }

    /// <summary>Like <see cref="DateTimeOffset"/> as a UTC <see cref="global::System.DateTime"/>.</summary>
    public static DateTime DateTime(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        DateTimeOffset(scope, kind, constraints).UtcDateTime;

    /// <summary>Like <see cref="DateTimeOffset"/> without time of day.</summary>
    public static DateOnly DateOnly(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        global::System.DateOnly.FromDateTime(DateTimeOffset(scope, kind, constraints).UtcDateTime);

    /// <summary>Returns a random time of day.</summary>
    public static TimeOnly TimeOnly(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Date.TimeOfDay();

    /// <summary>Returns a duration between 0 and 24 hours, in whole seconds.</summary>
    public static TimeSpan TimeSpan(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        global::System.TimeSpan.FromSeconds(scope.Faker.Random.Int(0, 24 * 60 * 60));

    /// <summary>Returns a random boolean.</summary>
    public static bool Bool(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Random.Bool();

    /// <summary>Returns a random letter.</summary>
    public static char Char(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Random.Letter();

    /// <summary>Returns a random version 4 GUID.</summary>
    public static Guid Guid(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Random.Guid();

    /// <summary>Returns a URL; an avatar or image URL when the member name says so.</summary>
    public static Uri Uri(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => new(Effective(scope, kind) switch
    {
        ConventionKind.Avatar => scope.Faker.Image.Avatar(scope.Identity.Sex),
        ConventionKind.ImageUrl => scope.Faker.Image.Url(),
        _ => scope.Faker.Internet.Url(),
    });

    /// <summary>
    /// Returns a random enum value. For sex/gender enums with <c>Female</c>/<c>Male</c> members the value matches
    /// the object's identity, so <c>Gender</c> agrees with <c>FirstName</c>.
    /// </summary>
    public static TEnum Enum<TEnum>(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
        where TEnum : struct, Enum
    {
        if (constraints is { AllowedValues.Length: > 0 })
            return Avoiding(scope, constraints, () => (TEnum)global::System.Enum.ToObject(typeof(TEnum), scope.Faker.Random.Element(constraints.AllowedValues)!));
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
    internal static object? Boxed(FakeScope scope, Type type, TypeCategory category, ConventionKind kind, MemberConstraints? c = null) => category switch
    {
        TypeCategory.String => String(scope, kind, c),
        TypeCategory.Char => Char(scope, kind, c),
        TypeCategory.Bool => Bool(scope, kind, c),
        TypeCategory.Byte => Number<byte>(scope, kind, c),
        TypeCategory.SByte => Number<sbyte>(scope, kind, c),
        TypeCategory.Int16 => Number<short>(scope, kind, c),
        TypeCategory.UInt16 => Number<ushort>(scope, kind, c),
        TypeCategory.Int32 => Number<int>(scope, kind, c),
        TypeCategory.UInt32 => Number<uint>(scope, kind, c),
        TypeCategory.Int64 => Number<long>(scope, kind, c),
        TypeCategory.UInt64 => Number<ulong>(scope, kind, c),
        TypeCategory.Single => Number<float>(scope, kind, c),
        TypeCategory.Double => Number<double>(scope, kind, c),
        TypeCategory.Decimal => Number<decimal>(scope, kind, c),
        TypeCategory.Guid => Guid(scope, kind, c),
        TypeCategory.DateTime => DateTime(scope, kind, c),
        TypeCategory.DateTimeOffset => DateTimeOffset(scope, kind, c),
        TypeCategory.DateOnly => DateOnly(scope, kind, c),
        TypeCategory.TimeOnly => TimeOnly(scope, kind, c),
        TypeCategory.TimeSpan => TimeSpan(scope, kind, c),
        TypeCategory.Uri => Uri(scope, kind, c),
        TypeCategory.Enum => c is { AllowedValues.Length: > 0 }
            ? Avoiding(scope, c, () => global::System.Enum.ToObject(type, scope.Faker.Random.Element(c.AllowedValues)!))
            : BoxedEnum(scope, type, kind),
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

    /// <summary>Retries <paramref name="produce"/> while it returns a <c>[DeniedValues]</c> value (up to 100 attempts).</summary>
    private static T Avoiding<T>(FakeScope scope, MemberConstraints constraints, Func<T> produce)
    {
        if (constraints.DeniedValues is not { Length: > 0 } denied)
            return produce();
        for (var attempt = 0; ; attempt++)
        {
            var value = produce();
            if (attempt >= 100 || !Array.Exists(denied, d => Equals(Normalize(d), Normalize(value))))
                return value;
        }

        static object? Normalize(object? v) => v is IConvertible c && v is not string ? c.ToDecimal(CultureInfo.InvariantCulture) : v;
    }

    /// <summary>
    /// Produces a string within the length limits: retries the convention a few times, then shortens (at a word
    /// boundary when possible) or pads with letters. Required strings are never empty.
    /// </summary>
    private static string FitLength(FakeScope scope, MemberConstraints c, Func<string> produce)
    {
        var min = Math.Max(c.MinLength ?? 0, c.Required ? 1 : 0);
        var max = c.MaxLength ?? int.MaxValue;
        var value = produce();
        for (var attempt = 0; attempt < 10 && (value.Length < min || value.Length > max); attempt++)
            value = produce();

        if (value.Length > max)
        {
            var cut = value.LastIndexOf(' ', Math.Min(max, value.Length - 1));
            value = (cut >= min && cut > 0 ? value[..cut] : value[..max]).TrimEnd();
        }

        if (value.Length < min)
        {
            var sb = new StringBuilder(value);
            while (sb.Length < min)
                sb.Append(scope.Faker.Random.Letter());
            value = sb.ToString();
        }

        return value;
    }

    /// <summary>Byte count whose base64 text fits the length limits (base64 uses 4 characters per 3 bytes).</summary>
    private static int FitByteCount(MemberConstraints c)
    {
        var maxBytes = c.MaxLength is { } max ? Math.Max(1, max / 4 * 3) : 32;
        var minBytes = c.MinLength is { } min ? (min + 3) / 4 * 3 : 1;
        return Math.Clamp(24, minBytes, Math.Max(minBytes, maxBytes));
    }

    /// <summary>Generates a string for a <c>[RegularExpression]</c> pattern (implicitly anchored, as the attribute is).</summary>
    private static string FromPattern(FakeScope scope, string pattern)
    {
        try
        {
            return scope.Faker.Helpers.FromRegExp(pattern);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Phoney cannot generate values for [RegularExpression(\"{pattern}\")]: {ex.Message} Add a rule with .With(...) for this member.", ex);
        }
    }

    /// <summary>Parses a numeric <c>[Range]</c>.</summary>
    private static bool TryRange(MemberConstraints c, out double min, out double max)
    {
        min = max = 0;
        return c.RangeMin is { } a && c.RangeMax is { } b
            && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out min)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out max);
    }

    /// <summary>Parses a date <c>[Range(typeof(DateTime), "2020-01-01", "2020-12-31")]</c>.</summary>
    private static bool TryDateRange(MemberConstraints c, out DateTimeOffset from, out DateTimeOffset to)
    {
        from = to = default;
        return c.RangeMin is { } a && c.RangeMax is { } b
            && global::System.DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out from)
            && global::System.DateTimeOffset.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out to)
            && from <= to;
    }

    /// <summary>A uniform value of <typeparamref name="T"/> in [<paramref name="min"/>, <paramref name="max"/>], clamped to the type's range.</summary>
    private static T InRange<T>(FakeScope scope, double min, double max)
        where T : INumber<T>
    {
        if (typeof(T) == typeof(decimal))
        {
            var low = (decimal)Math.Max(min, -7.9e28);
            var high = (decimal)Math.Min(max, 7.9e28);
            return T.CreateSaturating(scope.Faker.Number.Decimal(low, high, 2));
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half))
        {
            var value = Math.Clamp(Math.Round(scope.Faker.Random.Double(min, max), 2), min, max);
            return T.CreateSaturating(value);
        }

        // Integers: whole numbers inside the range and inside the type (CreateSaturating maps ±∞ to the type's limits).
        var typeMin = double.CreateSaturating(T.CreateSaturating(double.NegativeInfinity));
        var typeMax = double.CreateSaturating(T.CreateSaturating(double.PositiveInfinity));
        var from = ToLong(Math.Max(Math.Ceiling(min), typeMin));
        var to = ToLong(Math.Min(Math.Floor(max), typeMax));
        return T.CreateSaturating(to < from ? from : scope.Faker.Random.Long(from, to));

        // Explicit clamping: casting an out-of-range double to long is undefined in C#.
        static long ToLong(double d) => d >= 9.2233720368547748E18 ? long.MaxValue : d <= -9.2233720368547758E18 ? long.MinValue : (long)d;
    }

    /// <summary>An allowed value as invariant text.</summary>
    private static string Text(object? value) => value switch
    {
        null => "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

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
