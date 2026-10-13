using System.ComponentModel;
using Phoney.Generation;
using NodaDuration = NodaTime.Duration;
using NodaInstant = NodaTime.Instant;
using NodaLocalDate = NodaTime.LocalDate;
using NodaLocalDateTime = NodaTime.LocalDateTime;
using NodaLocalTime = NodaTime.LocalTime;
using NodaOffsetDateTime = NodaTime.OffsetDateTime;
using NodaZonedDateTime = NodaTime.ZonedDateTime;

namespace Phoney.NodaTime;

/// <summary>
/// Produces NodaTime members of generated objects from the value Phoney generates for the matching built-in type
/// (an <c>Instant CreatedAt</c> gets the <c>DateTimeOffset</c> a <c>CreatedAt</c> would get), so name conventions,
/// timelines and identities apply. Called by source-generated models and, through <see cref="ScalarAdapters"/>, by the reflection model.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NodaTimeValues
{
    static NodaTimeValues()
    {
        ScalarAdapters.Register(Instant);
        ScalarAdapters.Register(OffsetDateTime);
        ScalarAdapters.Register(ZonedDateTime);
        ScalarAdapters.Register(LocalDateTime);
        ScalarAdapters.Register(LocalDate);
        ScalarAdapters.Register(LocalTime);
        ScalarAdapters.Register(Duration);
    }

    /// <summary>An <c>Instant</c>, generated like a <see cref="DateTimeOffset"/> member.</summary>
    public static NodaInstant Instant(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaInstant.FromDateTimeOffset(ConventionValues.DateTimeOffset(scope, kind, constraints));

    /// <summary>An <c>OffsetDateTime</c> (UTC), generated like a <see cref="DateTimeOffset"/> member.</summary>
    public static NodaOffsetDateTime OffsetDateTime(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaOffsetDateTime.FromDateTimeOffset(ConventionValues.DateTimeOffset(scope, kind, constraints));

    /// <summary>A <c>ZonedDateTime</c> (UTC), generated like a <see cref="DateTimeOffset"/> member.</summary>
    public static NodaZonedDateTime ZonedDateTime(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaZonedDateTime.FromDateTimeOffset(ConventionValues.DateTimeOffset(scope, kind, constraints));

    /// <summary>A <c>LocalDateTime</c>, generated like a <see cref="DateTime"/> member.</summary>
    public static NodaLocalDateTime LocalDateTime(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaLocalDateTime.FromDateTime(ConventionValues.DateTime(scope, kind, constraints));

    /// <summary>A <c>LocalDate</c>, generated like a <see cref="DateOnly"/> member.</summary>
    public static NodaLocalDate LocalDate(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaLocalDate.FromDateOnly(ConventionValues.DateOnly(scope, kind, constraints));

    /// <summary>A <c>LocalTime</c>, generated like a <see cref="TimeOnly"/> member.</summary>
    public static NodaLocalTime LocalTime(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaLocalTime.FromTimeOnly(ConventionValues.TimeOnly(scope, kind, constraints));

    /// <summary>A <c>Duration</c>, generated like a <see cref="TimeSpan"/> member.</summary>
    public static NodaDuration Duration(FakeScope scope, ConventionKind kind, MemberConstraints? constraints = null) =>
        NodaDuration.FromTimeSpan(ConventionValues.TimeSpan(scope, kind, constraints));
}
