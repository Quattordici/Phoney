using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using Phoney.Modules;

namespace Phoney.Generation;

/// <summary>
/// Produces member values from a <see cref="ConventionKind"/> and the member's type. Both the reflection model and
/// source-generated models call these typed entry points, so both paths generate identical data.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConventionValues
{
    /// <summary>
    /// Whether the value of <paramref name="kind"/> is read from the object's <see cref="Identity"/>, which is generated once per object (drawing such a
    /// member again gives the same value).
    /// </summary>
    internal static bool IsIdentityBound(ConventionKind kind) => kind is ConventionKind.FirstName or ConventionKind.LastName or ConventionKind.Username
        or ConventionKind.Email or ConventionKind.StreetAddress or ConventionKind.City or ConventionKind.ZipCode or ConventionKind.State or ConventionKind.Country
        or ConventionKind.CountryCode or ConventionKind.NationalId or ConventionKind.Age or ConventionKind.BirthDate or ConventionKind.Gender or ConventionKind.Sex;

    /// <summary>
    /// Returns a string for <paramref name="kind"/> (a single word when there is no convention) that satisfies
    /// <paramref name="constraints"/>: allowed values, pattern, format, base64, length and required.
    /// </summary>
    public static string String(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
    {
        // The lambdas live in their own method: a lambda here would allocate its closure on every call, constrained or not.
        return constraints is null ? StringCore(scope, Effective(scope, kind)) : ConstrainedString(scope, kind, constraints);
    }

    private static string ConstrainedString(FakeScope scope, ConventionKind kind, MemberConstraints constraints)
    {
        return Avoiding(constraints, () =>
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

            if (TryRange(constraints, out var range))
                return NumberText(scope, range);

            // A format annotation is a validation rule, not a name convention: it holds also when the conventions are off.
            var format = constraints.Format ?? Effective(scope, kind);
            return FitLength(scope, constraints, format, () => StringCore(scope, format));
        });
    }

    /// <summary>A number inside a numeric <c>[Range]</c> on a string member, as invariant text: whole numbers when both limits are, else two decimals.</summary>
    private static string NumberText(FakeScope scope, NumericRange range) =>
        double.IsInteger(range.Min) && double.IsInteger(range.Max)
            ? InRange<long>(scope, range).ToString(CultureInfo.InvariantCulture)
            : InRange<decimal>(scope, range).ToString(CultureInfo.InvariantCulture);

    /// <summary>The convention value itself, before constraints; <paramref name="kind"/> has the generator's conventions switch applied.</summary>
    private static string StringCore(FakeScope scope, ConventionKind kind)
    {
        var f = scope.Faker;
        try
        {
            return kind switch
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
                ConventionKind.Country => scope.Identity.Country(),
                ConventionKind.CountryCode => scope.Identity.CountryCode(),
                ConventionKind.TimeZone => f.Location.TimeZone(),
                ConventionKind.Language => f.Location.Language().Alpha2,
                ConventionKind.Latitude => f.Location.Latitude().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConventionKind.Longitude => f.Location.Longitude().ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConventionKind.CompanyName => f.Company.Name(),
                ConventionKind.Department => f.Commerce.Department(),
                ConventionKind.ProductName => f.Commerce.ProductName(),
                ConventionKind.ProductDescription => f.Commerce.ProductDescription(),
                ConventionKind.Title => f.Lorem.Title(f.Random.Int(2, 5)),
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
                ConventionKind.Sku => f.Random.ReplaceSymbols("???-#####"),
                ConventionKind.Vin => f.Vehicle.Vin(),
                ConventionKind.LicensePlate => f.Vehicle.Vrm(),
                ConventionKind.Uuid or ConventionKind.Id or ConventionKind.ReferenceId => f.Random.Guid().ToString(),
                ConventionKind.Token => f.String.AlphaNumeric(32),
                ConventionKind.Version => f.System.Semver(),
                ConventionKind.FileName => f.System.CommonFileName(),
                ConventionKind.FilePath => f.System.FilePath(),
                ConventionKind.MimeType => f.System.MimeType(),
                ConventionKind.FileExtension => f.System.CommonFileExt(),
                ConventionKind.NationalId => scope.Identity.NationalId,
                ConventionKind.CompanyId => f.NationalId.Company(),
                ConventionKind.VatNumber => f.NationalId.Vat(),
                _ => f.Lorem.Word(),
            };
        }
        catch (PhoneyDataUnavailableException)
        {
            return ""; // e.g. a Prefix member in a locale without name prefixes
        }
    }

    /// <summary>
    /// Returns a number for <paramref name="kind"/> (1–1000, or up to the largest value of a smaller type, two decimals for non-integers, when there is no convention),
    /// or uniformly within <c>[Range]</c> / one of <c>[AllowedValues]</c> when <paramref name="constraints"/> say so.
    /// </summary>
    public static T Number<T>(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
        where T : INumber<T>
    {
        return constraints is null ? NumberCore<T>(scope, kind) : ConstrainedNumber<T>(scope, kind, constraints);
    }

    private static T ConstrainedNumber<T>(FakeScope scope, ConventionKind kind, MemberConstraints constraints)
        where T : INumber<T>
    {
        return Avoiding(constraints, () =>
        {
            if (constraints.AllowedValues is { Length: > 0 } allowed)
                return T.CreateSaturating(Convert.ToDecimal(scope.Faker.Random.Element(allowed), CultureInfo.InvariantCulture));
            if (TryRange(constraints, out var range))
                return InRange<T>(scope, range);
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
            _ when typeof(T) == typeof(decimal) => T.CreateSaturating(f.Number.Decimal(1, 1000, 2)),
            _ when fractional => T.CreateSaturating(f.Number.Double(1, 1000, 2)),
            _ => T.CreateSaturating(f.Random.Int(1, int.CreateSaturating(T.CreateSaturating(1000)))), // 1 to 255 for a byte, not mostly 255
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
        if (constraints is not null && TryOpenDateRange(constraints, out var from, out var to))
            return date.Between(from, to);

        var reference = scope.Faker.ReferenceDate;
        switch (Effective(scope, kind))
        {
            case ConventionKind.BirthDate:
                return new DateTimeOffset(scope.Identity.BirthDate.ToDateTime(global::System.TimeOnly.MinValue), global::System.TimeSpan.Zero);
            case ConventionKind.RecentDate:
            {
                // Recently, but never before the object was created (a creation time it was given may lie after the reference date).
                var earliest = FakeScope.Later(scope.Created, reference.AddDays(-30));
                return date.Between(earliest, FakeScope.Later(earliest, reference));
            }

            case ConventionKind.FutureDate:
                // After the reference date, and after a creation time the object was given that lies beyond it (the same single draw).
                return scope.GivenCreated is { } given && given > reference ? date.Future(referenceDate: given) : date.Future();
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
    public static DateOnly DateOnly(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
    {
        // An excluded limit removes a whole day: a moment just after midnight still has the excluded date.
        if (constraints is { RangeMinExclusive: true } or { RangeMaxExclusive: true } && TryDateRange(constraints, out var from, out var to))
        {
            var first = global::System.DateOnly.FromDateTime(from.UtcDateTime).AddDays(constraints.RangeMinExclusive ? 1 : 0);
            var last = global::System.DateOnly.FromDateTime(to.UtcDateTime).AddDays(constraints.RangeMaxExclusive ? -1 : 0);
            if (first > last)
                throw NoValueInRange(constraints, typeof(DateOnly));
            return scope.Faker.Date.Between(first, last);
        }

        return global::System.DateOnly.FromDateTime(DateTimeOffset(scope, kind, constraints).UtcDateTime);
    }

    /// <summary>Returns a random time of day, within <c>[Range(typeof(TimeOnly), "09:00", "17:00")]</c> when <paramref name="constraints"/> has one.</summary>
    public static TimeOnly TimeOnly(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        constraints is not null && TryDurationRange(constraints, typeof(TimeOnly), out var from, out var to)
            ? new TimeOnly(DurationBetween(scope, from, to).Ticks)
            : scope.Faker.Date.TimeOfDay();

    /// <summary>Returns a duration between 0 and 24 hours, in whole seconds, or within <c>[Range(typeof(TimeSpan), …)]</c> when <paramref name="constraints"/> has one.</summary>
    public static TimeSpan TimeSpan(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        constraints is not null && TryDurationRange(constraints, typeof(TimeSpan), out var from, out var to)
            ? DurationBetween(scope, from, to)
            : global::System.TimeSpan.FromSeconds(scope.Faker.Random.Int(0, 24 * 60 * 60));

    /// <summary>
    /// Parses a <c>[Range]</c> of durations or times of day, with the excluded limits moved one tick inside; throws when nothing is left.
    /// A time of day is read as the time since midnight.
    /// </summary>
    private static bool TryDurationRange(MemberConstraints c, Type type, out TimeSpan from, out TimeSpan to)
    {
        from = to = default;
        if (c.RangeMin is not { } a || c.RangeMax is not { } b || !TryParse(a, out from) || !TryParse(b, out to))
            return false;
        if (c.RangeMinExclusive)
            from += global::System.TimeSpan.FromTicks(1);
        if (c.RangeMaxExclusive)
            to -= global::System.TimeSpan.FromTicks(1);
        return from <= to ? true : throw NoValueInRange(c, type);

        bool TryParse(string text, out TimeSpan value)
        {
            if (type == typeof(TimeOnly))
            {
                var parsed = global::System.TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
                value = time.ToTimeSpan();
                return parsed;
            }

            return global::System.TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>A uniform duration in [<paramref name="from"/>, <paramref name="to"/>] in whole milliseconds, or in ticks when no whole millisecond lies inside.</summary>
    private static TimeSpan DurationBetween(FakeScope scope, TimeSpan from, TimeSpan to)
    {
        const long Millisecond = global::System.TimeSpan.TicksPerMillisecond;
        var first = from.Ticks >= 0 ? (from.Ticks + Millisecond - 1) / Millisecond : from.Ticks / Millisecond;
        var last = to.Ticks >= 0 ? to.Ticks / Millisecond : (to.Ticks - Millisecond + 1) / Millisecond;
        return first <= last
            ? global::System.TimeSpan.FromTicks(scope.Faker.Random.Long(first, last) * Millisecond)
            : global::System.TimeSpan.FromTicks(scope.Faker.Random.Long(from.Ticks, to.Ticks));
    }

    /// <summary>Returns a random boolean.</summary>
    public static bool Bool(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Random.Bool();

    /// <summary>Returns a random letter, or a character inside a numeric <c>[Range('a', 'f')]</c> when <paramref name="constraints"/> has one.</summary>
    public static char Char(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        constraints is not null && TryRange(constraints, out var range) ? InRange<char>(scope, range) : scope.Faker.Random.Letter();

    /// <summary>Returns a random version 4 GUID.</summary>
    public static Guid Guid(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => scope.Faker.Random.Guid();

    /// <summary>Returns a URL; an avatar or image URL when the member name says so.</summary>
    public static Uri Uri(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => new(Effective(scope, kind) switch
    {
        ConventionKind.Avatar => scope.Faker.Image.Avatar(scope.Identity.Sex),
        ConventionKind.ImageUrl => scope.Faker.Image.Url(),
        _ => scope.Faker.Internet.Url(),
    });

    /// <summary>Returns a letter as a <see cref="global::System.Text.Rune"/>.</summary>
    public static global::System.Text.Rune Rune(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) => new(scope.Faker.Random.Letter());

    /// <summary>Returns an IPv4 address; an IPv6 one when the member name says so (<c>Ipv6Address</c>), and either when it just says <c>Ip</c>.</summary>
    public static global::System.Net.IPAddress IPAddress(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        global::System.Net.IPAddress.Parse(Effective(scope, kind) switch
        {
            ConventionKind.Ipv6 => scope.Faker.Internet.Ipv6(),
            ConventionKind.Ip => scope.Faker.Internet.Ip(),
            _ => scope.Faker.Internet.Ipv4(),
        });

    /// <summary>Returns a version such as <c>3.14.2</c>.</summary>
    public static global::System.Version Version(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        global::System.Version.Parse(scope.Faker.System.Semver());

    /// <summary>
    /// Returns a random enum value. For sex/gender enums with <c>Female</c>/<c>Male</c> members the value matches
    /// the object's identity, so <c>Gender</c> agrees with <c>FirstName</c>.
    /// </summary>
    public static TEnum Enum<TEnum>(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null)
        where TEnum : struct, Enum
    {
        if (constraints is { AllowedValues.Length: > 0 })
            return AllowedEnum<TEnum>(scope, constraints);
        var values = EnumCache<TEnum>.Values;
        if (values.Length == 0)
            return default;
        if (EnumCache<TEnum>.Female is { } female && EnumCache<TEnum>.Male is { } male &&
            (kind is ConventionKind.Sex or ConventionKind.Gender || EnumCache<TEnum>.IsSexEnum))
        {
            var matching = scope.Identity.Sex == Modules.Sex.Female ? female : male;
            if (constraints is null || !IsDenied(constraints, matching))
                return matching;
        }

        return constraints is { DeniedValues.Length: > 0 }
            ? Avoiding(constraints, () => values[scope.Faker.Random.Index(values.Length)])
            : values[scope.Faker.Random.Index(values.Length)];
    }

    /// <summary>One of the <c>[AllowedValues]</c> of an enum member, avoiding <c>[DeniedValues]</c>.</summary>
    private static TEnum AllowedEnum<TEnum>(FakeScope scope, MemberConstraints constraints)
        where TEnum : struct, Enum =>
        Avoiding(constraints, () => (TEnum)AllowedEnumValue(typeof(TEnum), scope.Faker.Random.Element(constraints.AllowedValues!)));

    /// <summary>
    /// An allowed value as a value of the enum <paramref name="type"/>: a value of the enum, its number, or its name (any case).
    /// </summary>
    /// <exception cref="InvalidOperationException">The value is none of these.</exception>
    private static object AllowedEnumValue(Type type, object? value) => value switch
    {
        _ when value?.GetType() == type => value,
        string name when global::System.Enum.TryParse(type, name, ignoreCase: true, out var parsed) => parsed,
        sbyte or byte or short or ushort or int or uint or long or ulong => global::System.Enum.ToObject(type, value),
        _ => throw new InvalidOperationException(
            $"Phoney cannot generate values for [AllowedValues] of a {type.Name} member: '{value ?? "null"}' is not one of {string.Join(", ", global::System.Enum.GetNames(type))}. " +
            "List the enum's values or names, or add a rule with .With(...) for this member."),
    };

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
        TypeCategory.Half => Number<Half>(scope, kind, c),
        TypeCategory.Int128 => Number<Int128>(scope, kind, c),
        TypeCategory.UInt128 => Number<UInt128>(scope, kind, c),
        TypeCategory.BigInteger => Number<System.Numerics.BigInteger>(scope, kind, c),
        TypeCategory.IntPtr => Number<nint>(scope, kind, c),
        TypeCategory.UIntPtr => Number<nuint>(scope, kind, c),
        TypeCategory.Rune => Rune(scope, kind, c),
        TypeCategory.IPAddress => IPAddress(scope, kind, c),
        TypeCategory.Version => Version(scope, kind, c),
        TypeCategory.Enum => c is { AllowedValues.Length: > 0 } ? AllowedBoxedEnum(scope, type, c) : BoxedEnum(scope, type, kind, c),
        _ => null,
    };

    /// <summary>One of the <c>[AllowedValues]</c> of an enum member of a runtime type, avoiding <c>[DeniedValues]</c>.</summary>
    [RequiresDynamicCode("Generates enum values for types only known at runtime.")]
    private static object AllowedBoxedEnum(FakeScope scope, Type type, MemberConstraints c) =>
        Avoiding(c, () => AllowedEnumValue(type, scope.Faker.Random.Element(c.AllowedValues!)));

    /// <summary>Random enum value for a runtime type, matching the identity for sex/gender enums.</summary>
    [RequiresDynamicCode("Reads enum values for types only known at runtime.")]
    private static object? BoxedEnum(FakeScope scope, Type type, ConventionKind kind, MemberConstraints? constraints)
    {
        var values = global::System.Enum.GetValues(type);
        if (values.Length == 0)
            return global::System.Enum.ToObject(type, 0);
        var names = global::System.Enum.GetNames(type);
        var female = System.Array.FindIndex(names, n => n.Equals("Female", StringComparison.OrdinalIgnoreCase));
        var male = System.Array.FindIndex(names, n => n.Equals("Male", StringComparison.OrdinalIgnoreCase));
        if (female >= 0 && male >= 0 && (kind is ConventionKind.Sex or ConventionKind.Gender || IsSexEnumName(type.Name)))
        {
            var matching = global::System.Enum.Parse(type, names[scope.Identity.Sex == Modules.Sex.Female ? female : male]);
            if (constraints is null || !IsDenied(constraints, matching))
                return matching;
        }

        return constraints is { DeniedValues.Length: > 0 }
            ? Avoiding(constraints, () => values.GetValue(scope.Faker.Random.Index(values.Length)))
            : values.GetValue(scope.Faker.Random.Index(values.Length));
    }

    /// <summary>Retries <paramref name="produce"/> while it returns a <c>[DeniedValues]</c> value (up to 100 attempts).</summary>
    /// <exception cref="InvalidOperationException">Every attempt gave a denied value: the annotations leave (almost) nothing to generate.</exception>
    private static T Avoiding<T>(MemberConstraints constraints, Func<T> produce)
    {
        if (constraints.DeniedValues is not { Length: > 0 } denied)
            return produce();
        for (var attempt = 0; attempt <= 100; attempt++)
        {
            var value = produce();
            if (!IsDenied(constraints, value))
                return value;
        }

        throw new InvalidOperationException(
            $"Phoney cannot generate a value that is not one of the [DeniedValues({string.Join(", ", denied.Select(Text))})]: 101 values in a row were denied. " +
            "Allow more values, or add a rule with .With(...) for this member.");
    }

    /// <summary>Whether <paramref name="value"/> is one of the <c>[DeniedValues]</c> (numbers and enum values compare by value, whatever their type).</summary>
    private static bool IsDenied(MemberConstraints constraints, object? value)
    {
        if (constraints.DeniedValues is not { Length: > 0 } denied)
            return false;
        var normalized = Normalize(value);
        return Array.Exists(denied, d => Equals(Normalize(d), normalized));

        // Numbers (and enum values) as decimals, so 1, 1L and 1.0 are the same value; text, characters and dates compare as they are.
        static object? Normalize(object? v) => v switch
        {
            global::System.Enum or sbyte or byte or short or ushort or int or uint or long or ulong or decimal => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
            float or double when Convert.ToDouble(v, CultureInfo.InvariantCulture) is var d && double.IsFinite(d) && Math.Abs(d) < 7.9e28 => (decimal)d,
            _ => v,
        };
    }

    /// <summary>
    /// Produces a string within the length limits: retries the convention a few times, then shortens (at a word
    /// boundary when possible) or pads with letters. Required strings are never empty.
    /// </summary>
    private static string FitLength(FakeScope scope, MemberConstraints c, ConventionKind kind, Func<string> produce)
    {
        var min = Math.Max(c.MinLength ?? 0, c.Required ? 1 : 0);
        var max = c.MaxLength ?? int.MaxValue;
        var value = produce();
        for (var attempt = 0; attempt < 10 && (value.Length < min || value.Length > max); attempt++)
            value = produce();

        if (kind == ConventionKind.Email && max >= 3 && value.IndexOf('@') is > 0 and var at && at < value.Length - 1)
            return FitEmail(scope, value, at, min, max);

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

    /// <summary>
    /// Fits an email into the length limits and keeps it an email: a long one loses letters from the end of its name (and, when the domain alone is too long,
    /// from the end of the domain), a short one gains letters at the end of its name.
    /// </summary>
    private static string FitEmail(FakeScope scope, string email, int at, int min, int max)
    {
        var name = email[..at];
        var domain = email[(at + 1)..];
        if (email.Length > max)
        {
            if (domain.Length > max - 2)
                domain = domain[..(max - 2)].TrimEnd('.', '-');
            name = name[..Math.Min(name.Length, max - 1 - domain.Length)].TrimEnd('.', '-', '_');
            if (name.Length == 0)
                name = email[..1];
        }

        if (name.Length + 1 + domain.Length < min)
        {
            var sb = new StringBuilder(name);
            while (sb.Length + 1 + domain.Length < min)
                sb.Append(scope.Faker.Random.Letter());
            name = sb.ToString();
        }

        return name + "@" + domain;
    }

    /// <summary>Byte count whose base64 text fits the length limits (base64 uses 4 characters per 3 bytes).</summary>
    private static int FitByteCount(MemberConstraints c)
    {
        var maxBytes = c.MaxLength is { } max ? Math.Max(1, max / 4 * 3) : 32;
        var minBytes = c.MinLength is { } min ? (min + 3) / 4 * 3 : 1;
        return Math.Clamp(24, minBytes, Math.Max(minBytes, maxBytes));
    }

    /// <summary>
    /// Generates a string for a <c>[RegularExpression]</c> pattern (implicitly anchored, as the attribute is). A pattern with lookarounds, which generate no text,
    /// is checked with the attribute itself and generated again until it matches.
    /// </summary>
    private static string FromPattern(FakeScope scope, string pattern)
    {
        PatternGenerator generator;
        try
        {
            generator = PatternGenerator.Get(pattern);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Phoney cannot generate values for [RegularExpression(\"{pattern}\")]: {ex.Message} Add a rule with .With(...) for this member.", ex);
        }

        if (generator.IsExact)
            return generator.Generate(scope.Faker.Random);

        var validator = PatternValidators.GetOrAdd(pattern, static p => new RegularExpressionAttribute(p));
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = generator.Generate(scope.Faker.Random);
            if (validator.IsValid(value))
                return value;
        }

        throw new InvalidOperationException(
            $"Phoney cannot generate values for [RegularExpression(\"{pattern}\")]: 100 texts in a row did not satisfy its lookarounds. Add a rule with .With(...) for this member.");
    }

    /// <summary>Validators of the patterns with lookarounds, made once each.</summary>
    private static readonly ConcurrentDictionary<string, RegularExpressionAttribute> PatternValidators = new(StringComparer.Ordinal);

    /// <summary>Parses a numeric <c>[Range]</c>.</summary>
    private static bool TryRange(MemberConstraints c, out NumericRange range)
    {
        range = default;
        if (c.RangeMin is not { } a || c.RangeMax is not { } b
            || !double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            || !double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
            return false;
        range = new NumericRange(min, max, c);
        return true;
    }

    /// <summary>A numeric <c>[Range]</c> read from <paramref name="Source"/>; an exclusive limit is itself outside it.</summary>
    private readonly record struct NumericRange(double Min, double Max, MemberConstraints Source)
    {
        public bool MinExclusive => Source.RangeMinExclusive;

        public bool MaxExclusive => Source.RangeMaxExclusive;

        /// <summary>Whether <paramref name="value"/> lies outside the range.</summary>
        public bool Excludes(double value) =>
            (MinExclusive ? value <= Min : value < Min) || (MaxExclusive ? value >= Max : value > Max);

        /// <summary>The limits as decimals, digit for digit as written (a double keeps about 15), within what a decimal holds.</summary>
        public (decimal Low, decimal High) Decimals() => (Exact(Source.RangeMin, Min), Exact(Source.RangeMax, Max));

        private static decimal Exact(string? text, double parsed) =>
            decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) ? exact : (decimal)Math.Clamp(parsed, -7.9e28, 7.9e28);

        /// <summary>The error for a range in which <paramref name="type"/> has no value.</summary>
        public InvalidOperationException Empty(Type type) => NoValueInRange(Source, type);
    }

    /// <summary>The error for a <c>[Range]</c> with no value of <paramref name="type"/> inside, e.g. <c>[Range(1, 2)]</c> with both limits excluded for an <see cref="int"/>.</summary>
    private static InvalidOperationException NoValueInRange(MemberConstraints c, Type type)
    {
        var excluded = (c.RangeMinExclusive, c.RangeMaxExclusive) switch
        {
            (true, true) => ", MinimumIsExclusive = true, MaximumIsExclusive = true",
            (true, false) => ", MinimumIsExclusive = true",
            (false, true) => ", MaximumIsExclusive = true",
            _ => "",
        };
        return new InvalidOperationException(
            $"Phoney cannot generate values for [Range({c.RangeMin}, {c.RangeMax}{excluded})]: no {type.Name} lies inside it. Widen the range, include a limit, or add a rule with .With(...) for this member.");
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

    /// <summary>
    /// Like <see cref="TryDateRange"/> with the excluded limits moved one tick inside, which <c>DateModule.Between</c>
    /// rounds to the nearest millisecond inside; throws when nothing is left.
    /// </summary>
    private static bool TryOpenDateRange(MemberConstraints c, out DateTimeOffset from, out DateTimeOffset to)
    {
        if (!TryDateRange(c, out from, out to))
            return false;
        if (c.RangeMinExclusive)
            from = from.AddTicks(1);
        if (c.RangeMaxExclusive)
            to = to.AddTicks(-1);
        return from <= to ? true : throw NoValueInRange(c, typeof(DateTimeOffset));
    }

    /// <summary>A uniform value of <typeparamref name="T"/> in <paramref name="range"/>, clamped to the type's range.</summary>
    private static T InRange<T>(FakeScope scope, NumericRange range)
        where T : INumber<T>
    {
        if (typeof(T) == typeof(decimal))
            return T.CreateSaturating(DecimalInRange(scope, range));

        if (typeof(T) == typeof(float))
            return T.CreateSaturating(Narrow<float>(FractionalInRange(scope, range), range));
        if (typeof(T) == typeof(Half))
            return T.CreateSaturating(Narrow<Half>(FractionalInRange(scope, range), range));
        if (typeof(T) == typeof(double))
            return T.CreateSaturating(FractionalInRange(scope, range));

        // Integers: whole numbers inside the range and inside the type, from the limits as written (a double can't tell long.MinValue from the next one).
        var (low, high) = range.Decimals();
        var unbounded = typeof(T) == typeof(System.Numerics.BigInteger); // has no limits to saturate to
        var typeMin = unbounded ? decimal.MinValue : decimal.CreateSaturating(T.CreateSaturating(decimal.MinValue));
        var typeMax = unbounded ? decimal.MaxValue : decimal.CreateSaturating(T.CreateSaturating(decimal.MaxValue));
        var lowest = decimal.Ceiling(low);
        var highest = decimal.Floor(high);
        if (range.MinExclusive && lowest <= low) // Ceiling(low) is never below low: this is a whole-number limit that is excluded
            lowest++;
        if (range.MaxExclusive && highest >= high)
            highest--;
        var from = Math.Max(lowest, typeMin);
        var to = Math.Min(highest, typeMax);
        if (to < from)
            throw range.Empty(typeof(T));
        // A range wholly beyond long (a ulong near its top) is drawn as a BigInteger; one that reaches into long keeps its long draw, so seeds don't move.
        if (from > long.MaxValue || to < long.MinValue)
            return T.CreateSaturating(scope.Faker.Number.BigInteger(new System.Numerics.BigInteger(from), new System.Numerics.BigInteger(to)));
        return T.CreateSaturating(scope.Faker.Random.Long(ToLong(from), ToLong(to)));

        static long ToLong(decimal d) => (long)Math.Clamp(d, long.MinValue, long.MaxValue);
    }

    /// <summary>A double in <paramref name="range"/>, rounded to two decimals unless rounding lands on an excluded limit.</summary>
    private static double FractionalInRange(FakeScope scope, NumericRange range)
    {
        var (min, max) = (Math.Max(range.Min, double.MinValue), Math.Min(range.Max, double.MaxValue)); // [Range] may say ±∞ for no limit
        var drawn = scope.Faker.Random.Double(min, max);
        var value = Math.Clamp(Math.Round(drawn, 2), min, max);
        if (!range.Excludes(value))
            return value;
        // Rounding landed on an excluded limit: the drawn value is in [min, max), so only an excluded minimum needs a step inside.
        value = range.MinExclusive && drawn <= min ? Math.BitIncrement(min) : drawn;
        return range.Excludes(value) ? throw range.Empty(typeof(double)) : value;
    }

    /// <summary>
    /// <paramref name="value"/> (inside <paramref name="range"/>) as a <typeparamref name="TFloat"/>, moved one representable step inside when the conversion rounds it onto or
    /// past a limit, compared as a <see cref="double"/> and as a <typeparamref name="TFloat"/> (a <c>[Range]</c> with a float operand converts its limits too). Draws nothing.
    /// </summary>
    private static TFloat Narrow<TFloat>(double value, NumericRange range)
        where TFloat : IFloatingPointIeee754<TFloat>
    {
        var narrowed = TFloat.CreateSaturating(value);
        // Conversion is monotonic, so the value is at most at the converted limit, and one step is enough when the range holds a TFloat at all.
        if (Below(narrowed))
            narrowed = TFloat.BitIncrement(narrowed);
        else if (Above(narrowed))
            narrowed = TFloat.BitDecrement(narrowed);
        return Below(narrowed) || Above(narrowed) ? throw range.Empty(typeof(TFloat)) : narrowed;

        bool Below(TFloat v) => range.MinExclusive
            ? v <= TFloat.CreateSaturating(range.Min) || double.CreateSaturating(v) <= range.Min
            : v < TFloat.CreateSaturating(range.Min) || double.CreateSaturating(v) < range.Min;

        bool Above(TFloat v) => range.MaxExclusive
            ? v >= TFloat.CreateSaturating(range.Max) || double.CreateSaturating(v) >= range.Max
            : v > TFloat.CreateSaturating(range.Max) || double.CreateSaturating(v) > range.Max;
    }

    /// <summary>
    /// A decimal in <paramref name="range"/>, with two decimals (fewer when the limits are too large to hold them), or with as many more as it takes to have a value inside
    /// (<c>[Range(typeof(decimal), "0.001", "0.009")]</c> has none with two). The limits are read as decimals, not doubles, so no digit is lost.
    /// </summary>
    private static decimal DecimalInRange(FakeScope scope, NumericRange range)
    {
        var (low, high) = range.Decimals();
        var magnitude = Math.Max(Math.Abs(low), Math.Abs(high));
        // Two places, or as many as a decimal this large still holds (Number.Decimal keeps the same number, so the draw is unchanged).
        var places = 2;
        var scale = 100m;
        while (places > 0 && magnitude > decimal.MaxValue / scale)
        {
            places--;
            scale /= 10;
        }

        var (first, last) = Inside(scale);
        // More places while there is no value with these, as long as the limits scaled by ten more still fit a decimal.
        while (!HasStep(first, last, scale) && places < Modules.NumberModule.MaxDecimalPlaces && magnitude <= decimal.MaxValue / (scale * 10))
        {
            places++;
            scale *= 10;
            (first, last) = Inside(scale);
        }

        return HasStep(first, last, scale) ? scope.Faker.Number.Decimal(first, last, places) : throw range.Empty(typeof(decimal));

        // The first and last values inside with steps of 1/step: an excluded limit moves to the next step inside.
        // An excluded decimal.MaxValue (or MinValue) has no step beyond it, which leaves the range empty.
        (decimal First, decimal Last) Inside(decimal step) =>
            (range.MinExclusive ? Beside(decimal.Floor(low * step), 1) / step : low, range.MaxExclusive ? Beside(decimal.Ceiling(high * step), -1) / step : high);

        decimal Beside(decimal value, int direction)
        {
            var room = direction > 0 ? value < decimal.MaxValue : value > decimal.MinValue;
            return room ? value + direction : throw range.Empty(typeof(decimal));
        }

        static bool HasStep(decimal first, decimal last, decimal step) => decimal.Ceiling(first * step) <= decimal.Floor(last * step);
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
        scope.Settings.WithNameConventions ? kind : ConventionKind.None;

    /// <summary>Whether an enum's name suggests it describes sex or gender.</summary>
    private static bool IsSexEnumName(string name) =>
        name.Contains("Gender", StringComparison.OrdinalIgnoreCase) || name.Contains("Sex", StringComparison.OrdinalIgnoreCase);

    /// <summary>Per-enum cache so enum generation allocates nothing.</summary>
    private static class EnumCache<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly TEnum[] Values = EnumValues<TEnum>.All;
        public static readonly TEnum? Female = Find("Female");
        public static readonly TEnum? Male = Find("Male");
        public static readonly bool IsSexEnum = IsSexEnumName(typeof(TEnum).Name);

        /// <summary>The defined value named <paramref name="name"/>, ignoring case.</summary>
        private static TEnum? Find(string name) =>
            global::System.Enum.TryParse<TEnum>(name, ignoreCase: true, out var value) && global::System.Enum.IsDefined(value) ? value : null;
    }
}
