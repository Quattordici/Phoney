using System.Diagnostics.CodeAnalysis;
using Phoney.Templates;

namespace Phoney.Generation;

/// <summary>
/// The data settings that the test attributes filling parameters share (<see cref="FakeDataOptions"/> for <c>[FakeData]</c>, <see cref="FakeCheckOptions"/>
/// for <c>[FakeCheck]</c>): the locale, the chance of <see langword="null"/> and the reference date of the generated values.
/// </summary>
public abstract class FakeArgumentOptions
{
    private double _nullProbability;
    private string? _referenceDateText;
    private DateTimeOffset? _referenceDate;

    private protected FakeArgumentOptions()
    {
    }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale { get; set; }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => _nullProbability;
        set => _nullProbability = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(NullProbability), value, "Must be between 0 and 1.");
    }

    /// <summary>The point in time relative dates are computed from, in ISO 8601 (<c>2030-01-01</c>, UTC unless it has an offset). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.</summary>
    /// <exception cref="ArgumentException">The text is not a date, so the test would otherwise fail later with a bare <see cref="FormatException"/>.</exception>
    public string? ReferenceDate
    {
        get => _referenceDateText;
        set
        {
            if (value is null)
                _referenceDate = null;
            else if (DateText.TryParse(value, out var date))
                _referenceDate = date;
            else
                throw new ArgumentException($"ReferenceDate '{value}' is not a date with a day; use ISO 8601, such as 2030-01-01 or 2030-01-01T12:00:00Z.", nameof(ReferenceDate));
            _referenceDateText = value;
        }
    }

    /// <summary>Applies the locale, the null probability and the reference date to <paramref name="generator"/>.</summary>
    [RequiresUnreferencedCode("Populates arguments with reflection.")]
    [RequiresDynamicCode("Populates arguments with reflection.")]
    private protected ArgumentGenerator Configure(ArgumentGenerator generator)
    {
        generator = generator.NullProbability(NullProbability);
        if (Locale is { } locale)
            generator = generator.Locale(locale);
        if (_referenceDate is { } date)
            generator = generator.ReferenceDate(date);
        return generator;
    }
}
