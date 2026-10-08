using Phony.Data;

namespace Phony.Modules;

/// <summary>Phone numbers and IMEIs (faker.js <c>phone</c>).</summary>
public sealed class PhoneModule : FakerModule
{
    internal PhoneModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a phone number in one of the locale's formats for <paramref name="style"/>.</summary>
    public string Number(PhoneStyle style = PhoneStyle.Human)
    {
        var key = style switch
        {
            PhoneStyle.National => DataKeys.PhoneNumberFormatNational,
            PhoneStyle.International => DataKeys.PhoneNumberFormatInternational,
            PhoneStyle.Mobile => DataKeys.PhoneNumberFormatMobile,
            _ => DataKeys.PhoneNumberFormatHuman,
        };
        // Some locales define only the human formats; fall back to those rather than failing.
        if (!Faker.Data.Has(key))
            key = DataKeys.PhoneNumberFormatHuman;
        return Faker.Helpers.ReplaceSymbolWithNumber(Faker.Pick(key));
    }

    /// <summary>Returns a 15-digit IMEI with a valid Luhn check digit, e.g. <c>13-850175-913761-7</c>.</summary>
    public string Imei() => Faker.Helpers.ReplaceCreditCardSymbols("##-######-######-L");
}

/// <summary>Phone number formats.</summary>
public enum PhoneStyle
{
    /// <summary>As people commonly write it, possibly with an extension, e.g. <c>555.770.7727 x1234</c>.</summary>
    Human,

    /// <summary>National dialing format without extensions, e.g. <c>(555) 123-4567</c>.</summary>
    National,

    /// <summary>E.123 international format, e.g. <c>+15551234567</c>.</summary>
    International,

    /// <summary>A mobile number. Like every style, it falls back to <see cref="Human"/> formats when the locale has none.</summary>
    Mobile,
}
