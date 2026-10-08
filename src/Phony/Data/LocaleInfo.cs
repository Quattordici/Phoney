namespace Phony;

/// <summary>Describes a locale available to Phony.</summary>
/// <param name="Code">faker.js locale code, e.g. <c>de_AT</c>.</param>
/// <param name="Title">English name, e.g. <c>German (Austria)</c>.</param>
/// <param name="Endonym">Name in the locale's own language, e.g. <c>Deutsch (Österreich)</c>.</param>
/// <param name="Language">ISO 639 language code.</param>
/// <param name="Country">ISO 3166 country code, when the locale is country specific.</param>
/// <param name="Script">ISO 15924 script code, e.g. <c>Latn</c>.</param>
/// <param name="Direction">Text direction: <c>ltr</c> or <c>rtl</c>.</param>
/// <param name="Fallback">Locales consulted, in order, for data this locale does not define.</param>
public sealed record LocaleInfo(
    string Code,
    string? Title,
    string? Endonym,
    string? Language,
    string? Country,
    string? Script,
    string? Direction,
    IReadOnlyList<string> Fallback)
{
    /// <inheritdoc />
    public override string ToString() => Title is null ? Code : $"{Code} ({Title})";
}
