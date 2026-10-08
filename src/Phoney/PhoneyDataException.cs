namespace Phoney;

/// <summary>Thrown when locale data needed to generate a value is missing, unavailable or malformed.</summary>
public class PhoneyDataException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PhoneyDataException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public PhoneyDataException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when data is explicitly marked as not applicable to a locale (faker.js <c>null</c>), e.g. states in
/// Azerbaijani. Inside templates such parts render as empty text instead of failing the whole value.
/// </summary>
public sealed class PhoneyDataUnavailableException : PhoneyDataException
{
    /// <summary>Creates the exception.</summary>
    public PhoneyDataUnavailableException(string message) : base(message)
    {
    }
}
