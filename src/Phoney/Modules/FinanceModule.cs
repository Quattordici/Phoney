using System.Globalization;
using System.Text;
using Phoney.Data;

namespace Phoney.Modules;

/// <summary>Accounts, amounts, currencies, cards, IBAN/BIC and crypto addresses (faker.js <c>finance</c>).</summary>
public sealed class FinanceModule : FakerModule
{
    private const string UpperAlpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// Locale data key of each issuer's number formats, and the issuer's standard IIN range used when the locale
    /// has no formats for it.
    /// </summary>
    private static readonly (CardIssuer Issuer, int Key, string Default)[] CardIssuers =
    [
        (CardIssuer.AmericanExpress, DataKeys.FinanceCreditCardAmericanExpress, "3[4-7]##-######-####L"),
        (CardIssuer.DinersClub, DataKeys.FinanceCreditCardDinersClub, "30[0-5]#-######-###L"),
        (CardIssuer.Discover, DataKeys.FinanceCreditCardDiscover, "6011-####-####-###L"),
        (CardIssuer.Jcb, DataKeys.FinanceCreditCardJcb, "35[28-89]-####-####-###L"),
        (CardIssuer.Mastercard, DataKeys.FinanceCreditCardMastercard, "5[1-5]##-####-####-###L"),
        (CardIssuer.UnionPay, DataKeys.FinanceCreditCardUnionpay, "62##-####-####-###L"),
        (CardIssuer.Visa, DataKeys.FinanceCreditCardVisa, "4###-####-####-###L"),
    ];

    internal FinanceModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an account number of <paramref name="length"/> digits.</summary>
    public string AccountNumber(int length = 8) => Faker.String.Numeric(length);

    /// <summary>Returns an account name, e.g. <c>Savings Account</c>.</summary>
    public string AccountName() => Faker.Pick(DataKeys.FinanceAccountType) + " Account";

    /// <summary>Returns a 9-digit ABA routing number with a valid check digit.</summary>
    public string RoutingNumber()
    {
        var routing = Faker.Pick(DataKeys.FinanceFederalReserveRoutingSymbol) + Faker.String.Numeric(4);
        // ABA checksum: weights 3, 7, 1 repeating; the check digit rounds the sum up to a multiple of 10.
        var sum = 0;
        for (var i = 0; i < routing.Length; i++)
            sum += (routing[i] - '0') * (i % 3) switch { 0 => 3, 1 => 7, _ => 1 };
        return routing + (((sum + 9) / 10 * 10) - sum).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns an amount between <paramref name="min"/> and <paramref name="max"/> with <paramref name="decimals"/> decimals.</summary>
    public decimal Amount(decimal min = 0, decimal max = 1000, int decimals = 2) => Faker.Number.Decimal(min, max, decimals);

    /// <summary>Returns an amount as text, e.g. <c>$123.45</c>, formatted with <paramref name="culture"/> (invariant by default).</summary>
    public string AmountText(decimal min = 0, decimal max = 1000, int decimals = 2, string symbol = "", CultureInfo? culture = null)
    {
        var format = culture is null ? "F" : "N";
        return symbol + Amount(min, max, decimals).ToString(format + decimals.ToString(CultureInfo.InvariantCulture), culture ?? CultureInfo.InvariantCulture);
    }

    /// <summary>Returns a transaction type, e.g. <c>deposit</c>.</summary>
    public string TransactionType() => Faker.Pick(DataKeys.FinanceTransactionType);

    /// <summary>Returns a transaction description.</summary>
    public string TransactionDescription() => Faker.Pick(DataKeys.FinanceTransactionDescriptionPattern);

    /// <summary>Returns a currency with its ISO 4217 codes and symbol.</summary>
    public Currency Currency()
    {
        var (table, row) = Faker.PickRecord(DataKeys.FinanceCurrency);
        return new Currency(
            table.Get(row, "name") ?? "",
            table.Get(row, "code") ?? "",
            table.Get(row, "symbol") ?? "",
            table.Get(row, "numericCode") ?? "");
    }

    /// <summary>Returns an ISO 4217 currency code, e.g. <c>SEK</c>.</summary>
    public string CurrencyCode() => Currency().Code;

    /// <summary>Returns a currency name, e.g. <c>Swedish Krona</c>.</summary>
    public string CurrencyName() => Currency().Name;

    /// <summary>Returns a currency symbol, e.g. <c>€</c> (currencies without a symbol are skipped).</summary>
    public string CurrencySymbol()
    {
        string symbol;
        do
            symbol = Currency().Symbol;
        while (symbol.Length == 0);
        return symbol;
    }

    /// <summary>Returns an ISO 4217 numeric currency code, e.g. <c>752</c>.</summary>
    public string CurrencyNumericCode() => Currency().NumericCode;

    /// <summary>Returns a card issuer the locale has number formats for.</summary>
    public CardIssuer CreditCardIssuer()
    {
        var available = CardIssuers.Where(c => Faker.Data.Has(c.Key)).ToArray();
        return Random.Element(available).Issuer;
    }

    /// <summary>Returns a credit card number with a valid Luhn check digit, from a random issuer unless one is given.</summary>
    public string CreditCardNumber(CardIssuer? issuer = null)
    {
        var chosen = issuer ?? CreditCardIssuer();
        var (_, key, fallback) = CardIssuers.First(c => c.Issuer == chosen);
        return FromCardFormat(Faker.TryPick(key) ?? fallback);
    }

    /// <summary>
    /// Returns a card number for a custom format with a valid Luhn check digit: <c>#</c> is a digit, <c>[a-b]</c> a number
    /// in that range and <c>L</c> the check digit, e.g. <c>4###-####-####-###L</c>.
    /// </summary>
    public string CreditCardNumber(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return FromCardFormat(format);
    }

    /// <summary>Returns a 3-digit card verification value.</summary>
    public string CreditCardCvv() => Faker.String.Numeric(3);

    /// <summary>Returns a PIN of <paramref name="length"/> digits.</summary>
    public string Pin(int length = 4)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        return Faker.String.Numeric(length);
    }

    /// <summary>Returns an Ethereum address: <c>0x</c> and 40 hexadecimal characters.</summary>
    public string EthereumAddress() => Faker.String.Hexadecimal(40, Casing.Lower);

    /// <summary>Returns a Bitcoin address shaped like the given family and network.</summary>
    public string BitcoinAddress(BitcoinAddressFamily? type = null, BitcoinNetwork network = BitcoinNetwork.Mainnet)
    {
        var family = type ?? Random.Enum<BitcoinAddressFamily>();
        var mainnet = network == BitcoinNetwork.Mainnet;
        var (prefix, min, max, casing, exclude) = family switch
        {
            BitcoinAddressFamily.Segwit => (mainnet ? "3" : "2", 26, 34, Casing.Mixed, "0OIl"),
            BitcoinAddressFamily.Bech32 => (mainnet ? "bc1" : "tb1", 42, 42, Casing.Lower, "1bBiIoO"),
            BitcoinAddressFamily.Taproot => (mainnet ? "bc1p" : "tb1p", 62, 62, Casing.Lower, "1bBiIoO"),
            _ => (mainnet ? "1" : "m", 26, 34, Casing.Mixed, "0OIl"),
        };
        return prefix + Faker.String.AlphaNumeric(Random.Int(min, max) - prefix.Length, casing, exclude);
    }

    /// <summary>Returns a Litecoin address shape (26–33 base58 characters starting with L, M or 3).</summary>
    public string LitecoinAddress() =>
        Faker.String.FromCharacters("LM3") +
        Faker.String.FromCharacters("123456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ", Random.Int(26, 33) - 1);

    /// <summary>Returns a valid IBAN (correct structure and check digits), e.g. <c>SE4550000000058398257466</c>.</summary>
    /// <param name="countryCode">ISO 3166 country (e.g. <c>SE</c>); random among the 69 supported countries when not given.</param>
    /// <param name="formatted">Group the IBAN in blocks of four characters.</param>
    /// <exception cref="ArgumentException">The country has no IBAN format.</exception>
    public string Iban(string? countryCode = null, bool formatted = false)
    {
        var format = countryCode is null
            ? Random.Element(IbanData.Formats)
            : Array.Find(IbanData.Formats, f => string.Equals(f.Country, countryCode, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Country code '{countryCode}' is not supported for IBANs.", nameof(countryCode));

        var bban = new StringBuilder(format.Total);
        foreach (var segment in format.Bban)
        {
            for (var c = segment.Count; c > 0; c--)
            {
                bban.Append(segment.Type switch
                {
                    'a' => UpperAlpha[Random.Index(26)],
                    'c' => Random.Bool(0.8) ? Random.Digit() : UpperAlpha[Random.Index(26)],
                    _ => Random.Digit(),
                });
            }
        }

        var bbanText = bban.ToString();
        var checksum = 98 - Mod97(bbanText + format.Country + "00");
        var iban = $"{format.Country}{checksum.ToString("00", CultureInfo.InvariantCulture)}{bbanText}";
        return formatted ? PrettyPrintIban(iban) : iban;
    }

    /// <summary>Returns a BIC/SWIFT code, e.g. <c>DEUTDEFF500</c>; the branch code is random when <paramref name="includeBranchCode"/> is null.</summary>
    public string Bic(bool? includeBranchCode = null)
    {
        var bank = Faker.String.Alpha(4, Casing.Upper);
        var country = Random.Element(IbanData.Iso3166);
        var location = Faker.String.AlphaNumeric(2, Casing.Upper);
        var branch = (includeBranchCode ?? Random.Bool())
            ? Random.Bool() ? Faker.String.AlphaNumeric(3, Casing.Upper) : "XXX"
            : "";
        return bank + country + location + branch;
    }

    /// <summary>Groups an IBAN into blocks of four characters.</summary>
    public static string PrettyPrintIban(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        var sb = new StringBuilder(iban.Length + (iban.Length / 4));
        for (var i = 0; i < iban.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append(' ');
            sb.Append(iban[i]);
        }

        return sb.ToString();
    }

    /// <summary>Fills a card format; some locales write formats as <c>/regex/</c>, and the slashes are not part of the number.</summary>
    private string FromCardFormat(string format) =>
        Faker.Helpers.ReplaceCreditCardSymbols(format.Replace("/", "", StringComparison.Ordinal));

    /// <summary>ISO 7064 MOD 97-10 over the IBAN's digit form (letters become 10..35).</summary>
    private static int Mod97(string text)
    {
        var m = 0;
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                m = ((m * 10) + (c - '0')) % 97;
            }
            else
            {
                var value = char.ToUpperInvariant(c) - 'A' + 10;
                m = ((m * 100) + value) % 97;
            }
        }

        return m;
    }
}

/// <summary>A currency.</summary>
/// <param name="Name">Name, e.g. <c>Euro</c>.</param>
/// <param name="Code">ISO 4217 code, e.g. <c>EUR</c>.</param>
/// <param name="Symbol">Symbol, e.g. <c>€</c>; may be empty.</param>
/// <param name="NumericCode">ISO 4217 numeric code, e.g. <c>978</c>.</param>
public readonly record struct Currency(string Name, string Code, string Symbol, string NumericCode);

/// <summary>Payment card issuers.</summary>
public enum CardIssuer
{
    /// <summary>American Express.</summary>
    AmericanExpress,

    /// <summary>Diners Club.</summary>
    DinersClub,

    /// <summary>Discover.</summary>
    Discover,

    /// <summary>JCB.</summary>
    Jcb,

    /// <summary>Mastercard.</summary>
    Mastercard,

    /// <summary>UnionPay.</summary>
    UnionPay,

    /// <summary>Visa.</summary>
    Visa,
}

/// <summary>Bitcoin address families.</summary>
public enum BitcoinAddressFamily
{
    /// <summary>P2PKH, starting with 1 (m on testnet).</summary>
    Legacy,

    /// <summary>P2SH, starting with 3 (2 on testnet).</summary>
    Segwit,

    /// <summary>Native SegWit, starting with bc1 (tb1 on testnet).</summary>
    Bech32,

    /// <summary>Taproot, starting with bc1p (tb1p on testnet).</summary>
    Taproot,
}

/// <summary>Bitcoin networks.</summary>
public enum BitcoinNetwork
{
    /// <summary>The main network.</summary>
    Mainnet,

    /// <summary>The test network.</summary>
    Testnet,
}
