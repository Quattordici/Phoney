using System.Globalization;
using Phonery.Data;

namespace Phonery.Modules;

/// <summary>Products, prices and product codes (faker.js <c>commerce</c>).</summary>
public sealed class CommerceModule : FakerModule
{
    /// <summary>
    /// ISBN registrant lengths per registration group ("0" and "1" are the English-language groups faker.js uses),
    /// as (range maximum, registrant length) pairs from the ISBN range message.
    /// </summary>
    private static readonly (string Group, (int Max, int Length)[] Rules)[] IsbnRules =
    [
        ("0",
        [
            (1999999, 2), (2279999, 3), (2289999, 4), (3689999, 3), (3699999, 4), (6389999, 3), (6397999, 4),
            (6399999, 7), (6449999, 3), (6459999, 7), (6479999, 3), (6489999, 7), (6549999, 3), (6559999, 4),
            (6999999, 3), (8499999, 4), (8999999, 5), (9499999, 6), (9999999, 7),
        ]),
        ("1",
        [
            (99999, 3), (299999, 2), (349999, 3), (399999, 4), (499999, 3), (699999, 2), (999999, 4),
            (3979999, 3), (5499999, 4), (6499999, 5), (6799999, 4), (6859999, 5), (7139999, 4), (7169999, 3),
            (7319999, 4), (7399999, 7), (7749999, 5), (7753999, 7), (7763999, 5), (7764999, 7), (7769999, 5),
            (7782999, 7), (7899999, 5), (7999999, 4), (8004999, 5), (8049999, 5), (8379999, 5), (8384999, 7),
            (8671999, 5), (8675999, 4), (8697999, 5), (9159999, 6), (9165059, 7), (9168699, 6), (9169079, 7),
            (9195999, 6), (9196549, 7), (9729999, 6), (9877999, 4), (9911499, 6), (9911999, 7), (9989899, 6),
            (9999999, 7),
        ]),
    ];

    internal CommerceModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a department, e.g. <c>Garden</c>.</summary>
    public string Department() => Faker.Pick(DataKeys.CommerceDepartment);

    /// <summary>Returns a product name, e.g. <c>Incredible Soft Gloves</c>.</summary>
    public string ProductName() => Faker.Pick(DataKeys.CommerceProductNamePattern);

    /// <summary>Returns a product adjective, e.g. <c>Handcrafted</c>.</summary>
    public string ProductAdjective() => Faker.Pick(DataKeys.CommerceProductNameAdjective);

    /// <summary>Returns a product material, e.g. <c>Rubber</c>.</summary>
    public string ProductMaterial() => Faker.Pick(DataKeys.CommerceProductNameMaterial);

    /// <summary>Returns a product, e.g. <c>Computer</c>.</summary>
    public string Product() => Faker.Pick(DataKeys.CommerceProductNameProduct);

    /// <summary>Returns a product description.</summary>
    public string ProductDescription() => Faker.Pick(DataKeys.CommerceProductDescription);

    /// <summary>
    /// Returns a price between <paramref name="min"/> and <paramref name="max"/>. Like real prices (and faker.js),
    /// the last digit favours 9, then 5 and 0, e.g. <c>39.99</c>.
    /// </summary>
    public decimal Price(decimal min = 1, decimal max = 1000, int decimals = 2)
    {
        if (min < 0 || max < 0)
            return 0;
        if (min == max)
            return decimal.Round(min, decimals);

        var generated = Faker.Number.Decimal(min, max, decimals);
        if (decimals == 0)
            return generated;

        var unit = 1m;
        for (var i = 0; i < decimals; i++)
            unit /= 10;
        var lastDigit = decimal.Truncate(generated / unit) % 10;
        var newLastDigit = Random.Index(10) switch
        {
            < 5 => 9,  // weight 5
            < 8 => 5,  // weight 3
            8 => 0,    // weight 1
            _ => Random.Index(10), // weight 1: any digit
        };
        var combined = generated - (lastDigit * unit) + (newLastDigit * unit);
        return combined >= min && combined <= max ? combined : generated;
    }

    /// <summary>Returns a price formatted with <paramref name="symbol"/>, e.g. <c>$39.99</c>.</summary>
    public string PriceText(string symbol = "", decimal min = 1, decimal max = 1000, int decimals = 2) =>
        symbol + Price(min, max, decimals).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Returns a valid ISBN-13 (or ISBN-10) with registration group, registrant and check digit, e.g. <c>978-0-7354-8564-4</c>.</summary>
    public string Isbn(IsbnVariant variant = IsbnVariant.Isbn13, string separator = "-")
    {
        var (group, rules) = Random.Element<(string, (int, int)[])>(IsbnRules);
        var element = Faker.String.Numeric(8);
        var elementValue = int.Parse(element.AsSpan(0, 7), CultureInfo.InvariantCulture);
        var registrantLength = rules.First(r => elementValue <= r.Item1).Item2;
        var registrant = element[..registrantLength];
        var publication = element[registrantLength..];

        var isbn10 = variant == IsbnVariant.Isbn10;
        var digits = (isbn10 ? "" : "978") + group + registrant + publication;
        var length = isbn10 ? 10 : 13;
        var checksum = 0;
        for (var i = 0; i < length - 1; i++)
        {
            var weight = isbn10 ? i + 1 : i % 2 == 1 ? 3 : 1;
            checksum += weight * (digits[i] - '0');
        }

        checksum = isbn10 ? checksum % 11 : (10 - (checksum % 10)) % 10;
        var check = checksum == 10 ? "X" : checksum.ToString(CultureInfo.InvariantCulture);
        return isbn10
            ? string.Join(separator, group, registrant, publication, check)
            : string.Join(separator, "978", group, registrant, publication, check);
    }

    /// <summary>Returns a 12-digit UPC-A code with a valid check digit, optionally starting with <paramref name="prefix"/>.</summary>
    public string Upc(string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length > 11 || !prefix.All(char.IsAsciiDigit))
            throw new ArgumentException("The prefix must be at most 11 digits.", nameof(prefix));
        var body = prefix + Faker.String.Numeric(11 - prefix.Length);
        var sum = 0;
        for (var i = 0; i < 11; i++)
            sum += (body[i] - '0') * (i % 2 == 0 ? 3 : 1);
        return body + ((10 - (sum % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>ISBN formats.</summary>
public enum IsbnVariant
{
    /// <summary>13 digits, prefixed with 978.</summary>
    Isbn13,

    /// <summary>10 characters; the check character may be <c>X</c>.</summary>
    Isbn10,
}
