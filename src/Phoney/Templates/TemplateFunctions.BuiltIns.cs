using System.Collections.Concurrent;
using System.Globalization;
using Phoney.Data;
using Phoney.Modules;

namespace Phoney.Templates;

// The built-in template functions, one registration method per module. A module is registered the first time one of its functions is
// asked for: registering all ~240 at once is a 10 KB method that the JIT compiles in the first call of every process, to evaluate a
// template that uses two or three of them.
public static partial class TemplateFunctions
{
    private delegate void ModuleRegistration(ConcurrentDictionary<string, TemplateFunction> f);

    private static readonly object ModuleGate = new();
    private static readonly HashSet<string> LoadedModules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The registration of each module of the built-in functions, by module name (the part of the function name before the first dot).</summary>
    private static readonly Dictionary<string, ModuleRegistration> BuiltInModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["helpers"] = RegisterHelpers,
        ["number"] = RegisterNumber,
        ["string"] = RegisterString,
        ["person"] = RegisterPerson,
        ["location"] = RegisterLocation,
        ["internet"] = RegisterInternet,
        ["nationalId"] = RegisterNationalId,
        ["phone"] = RegisterPhone,
        ["company"] = RegisterCompany,
        ["commerce"] = RegisterCommerce,
        ["finance"] = RegisterFinance,
        ["date"] = RegisterDate,
        ["lorem"] = RegisterLorem,
        ["word"] = RegisterWord,
        ["color"] = RegisterColor,
        ["animal"] = RegisterAnimal,
        ["book"] = RegisterBook,
        ["music"] = RegisterMusic,
        ["food"] = RegisterFood,
        ["hacker"] = RegisterHacker,
        ["vehicle"] = RegisterVehicle,
        ["airline"] = RegisterAirline,
        ["science"] = RegisterScience,
        ["datatype"] = RegisterDatatype,
        ["database"] = RegisterDatabase,
        ["system"] = RegisterSystem,
        ["git"] = RegisterGit,
        ["image"] = RegisterImage,
    };

    /// <summary>Registers the built-in functions of the module that <paramref name="name"/> belongs to, once.</summary>
    private static void EnsureModuleOf(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
            EnsureModule(name[..dot]);
    }

    private static void EnsureModule(string module)
    {
        lock (ModuleGate)
        {
            if (BuiltInModules.TryGetValue(module, out var register) && LoadedModules.Add(module))
                register(Functions);
        }
    }

    private static void EnsureAllModules()
    {
        lock (ModuleGate)
        {
#pragma warning disable PHONEYDEV001 // Each module registers independently of the others, so the order of the modules doesn't matter.
            foreach (var (module, register) in BuiltInModules)
            {
                if (LoadedModules.Add(module))
                    register(Functions);
            }
#pragma warning restore PHONEYDEV001
        }
    }

    /// <summary>The most characters one template function call produces; a template can come from anywhere, so a typo or a hostile value can't ask for gigabytes.</summary>
    private const int MaxTemplateSize = 100_000;

    /// <summary>The most words a call produces (a word is up to about 13 characters, so this stays near <see cref="MaxTemplateSize"/> times a few).</summary>
    private const int MaxTemplateWords = 10_000;

    /// <summary>The most sentences a call produces (up to about 130 characters each).</summary>
    private const int MaxTemplateSentences = 1_000;

    /// <summary>The most paragraphs a call produces (three sentences each, up to about 400 characters).</summary>
    private const int MaxTemplateParagraphs = 300;

    /// <summary>A length or count argument (<paramref name="name"/> in an options object, or the plain number), <paramref name="fallback"/> when absent, kept to <paramref name="limit"/>.</summary>
    /// <exception cref="FormatException">The value is larger than <paramref name="limit"/>.</exception>
    private static int SizeOf(TemplateArgs a, int fallback, string name = "length", int limit = MaxTemplateSize) => Limited(a.Int(0, name) ?? fallback, name, limit);

    /// <summary>Like <see cref="SizeOf"/> for an argument that has no default (the module chooses one).</summary>
    /// <exception cref="FormatException">The value is larger than <paramref name="limit"/>.</exception>
    private static int? OptionalSizeOf(TemplateArgs a, string name, int limit) => a.Int(0, name) is { } size ? Limited(size, name, limit) : null;

    /// <summary>Returns the requested size unchanged when at most <paramref name="limit"/>; negative values are left for the called module to handle.</summary>
    /// <exception cref="FormatException">The size exceeds <paramref name="limit"/>; <paramref name="name"/> identifies the argument in the error.</exception>
    private static int Limited(int size, string name, int limit) =>
        size <= limit ? size : throw new FormatException($"The {name} {size} is too large for a template: at most {limit} (use the API for more).");

    /// <summary>The <c>sex</c> option (<c>"female"</c>/<c>"male"</c>, also as the plain argument unless <paramref name="plainAllowed"/> is off), or <see langword="null"/> when it is absent.</summary>
    /// <remarks>Values are case-insensitive, <c>f</c>/<c>m</c> are accepted, and an empty value returns <see langword="null"/>.</remarks>
    /// <exception cref="FormatException">The value is not empty or one of the accepted labels.</exception>
    private static Sex? SexOf(TemplateArgs a, bool plainAllowed = true) => (plainAllowed ? a.String(0, "sex", null) : a.OptionString(0, "sex", null))?.ToUpperInvariant() switch
    {
        null or "" => null,
        "FEMALE" or "F" => Sex.Female,
        "MALE" or "M" => Sex.Male,
        var other => throw new FormatException($"The sex must be 'female' or 'male', but was '{other}'."),
    };

    /// <summary>The <c>casing</c> option (<c>"upper"</c>, <c>"lower"</c> or <c>"mixed"</c>).</summary>
    /// <remarks>Only an options object gives the casing; a plain argument is the main parameter of the function (its length). Values are case-insensitive; a missing or empty value returns <paramref name="fallback"/>.</remarks>
    /// <exception cref="FormatException">The value is not empty or one of the accepted labels.</exception>
    private static Casing CasingOf(TemplateArgs a, Casing fallback = Casing.Mixed) => a.OptionString(0, "casing", null)?.ToUpperInvariant() switch
    {
        null or "" => fallback,
        "UPPER" => Casing.Upper,
        "LOWER" => Casing.Lower,
        "MIXED" => Casing.Mixed,
        var other => throw new FormatException($"The casing must be 'upper', 'lower' or 'mixed', but was '{other}'."),
    };

    private static void RegisterHelpers(ConcurrentDictionary<string, TemplateFunction> f)
    {
        f["helpers.arrayElement"] = (faker, a) => faker.Random.Element(a.SharedStrings(0));
        f["helpers.fromRegExp"] = (faker, a) => faker.Helpers.FromRegExp(a.String(0) ?? "");
        f["helpers.replaceSymbols"] = (faker, a) => faker.Random.ReplaceSymbols(a.String(0) ?? "");
        f["helpers.replaceSymbolWithNumber"] = (faker, a) => faker.Helpers.ReplaceSymbolWithNumber(a.String(0) ?? "");
        f["helpers.replaceCreditCardSymbols"] = (faker, a) => a.Count == 0 ? faker.Helpers.ReplaceCreditCardSymbols() : faker.Helpers.ReplaceCreditCardSymbols(a.String(0)!);
        f["helpers.slugify"] = (_, a) => HelpersModule.Slugify(a.String(0) ?? "");
    }

    private static void RegisterNumber(ConcurrentDictionary<string, TemplateFunction> f)
    {
        var inv = CultureInfo.InvariantCulture;
        f["number.int"] = WritingFunction.Of(static (Faker faker, TemplateArgs a, ref TextBuilder t) =>
        {
            Span<char> digits = stackalloc char[20];
            Number(faker, a, 0, int.MaxValue).TryFormat(digits, out var written, provider: CultureInfo.InvariantCulture);
            t.Append(digits[..written]);
        });
        f["number.float"] = (faker, a) =>
        {
            var min = a.Double(0, "min") ?? 0;
            var max = a.Double(0, "max") ?? (a.Count > 0 && a.Double(0) is { } single ? single : 1);
            return faker.Number.Double(min, max, a.Int(0, "fractionDigits")).ToString(CultureInfo.InvariantCulture);
        };
        f["number.binary"] = (faker, a) => faker.Number.Binary(a.Int(0, "min") ?? 0, a.Int(0, "max") ?? a.Int(0) ?? 1);
        f["number.octal"] = (faker, a) => faker.Number.Octal(a.Int(0, "min") ?? 0, a.Int(0, "max") ?? a.Int(0) ?? 7);
        f["number.hex"] = (faker, a) => faker.Number.Hex(a.Int(0, "min") ?? 0, a.Int(0, "max") ?? a.Int(0) ?? 15);
        f["number.romanNumeral"] = (faker, a) => faker.Number.RomanNumeral(a.Int(0, "min") ?? 1, a.Int(0, "max") ?? a.Int(0) ?? 3999);
        f["number.long"] = (faker, a) => faker.Number.Long(a.Int(0, "min") ?? 0, a.Int(1) ?? a.Int(0, "max") ?? long.MaxValue).ToString(inv);
        f["number.double"] = f["number.float"];
        f["number.normal"] = (faker, a) => faker.Number.Normal(a.Double(0, "mean") ?? 0, a.Double(1, "standardDeviation") ?? 1, a.Double(2, "min"), a.Double(3, "max")).ToString(CultureInfo.InvariantCulture);
        f["number.decimal"] = (faker, a) => faker.Number.Decimal((decimal)(a.Double(0) ?? 0), (decimal)(a.Double(1) ?? 1), a.Int(2) ?? 2).ToString(inv);
    }

    /// <summary>Registers string template functions, reading length, casing, exclusion and prefix options where supported.</summary>
    private static void RegisterString(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        f["string.numeric"] = (faker, a) => faker.String.Numeric(SizeOf(a, 1), a.Bool(0, "allowLeadingZeros") ?? a.Bool(1, "allowLeadingZeros") ?? true, a.OptionCharacters(0, "exclude"));
        f["string.alpha"] = (faker, a) => faker.String.Alpha(SizeOf(a, 1), CasingOf(a), a.OptionCharacters(0, "exclude"));
        f["string.alphanumeric"] = (faker, a) => faker.String.AlphaNumeric(SizeOf(a, 1), CasingOf(a), a.OptionCharacters(0, "exclude"));
        f["string.hexadecimal"] = (faker, a) => faker.String.Hexadecimal(SizeOf(a, 1), CasingOf(a), a.OptionString(0, "prefix", "0x")!);
        f["string.binary"] = (faker, a) => faker.String.Binary(SizeOf(a, 1), a.OptionString(0, "prefix", "0b")!);
        f["string.octal"] = (faker, a) => faker.String.Octal(SizeOf(a, 1), a.OptionString(0, "prefix", "0o")!);
        f["string.sample"] = (faker, a) => faker.String.Sample(SizeOf(a, 10));
        f["string.symbol"] = (faker, a) => faker.String.Symbol(SizeOf(a, 1));
        f["string.nanoid"] = (faker, a) => faker.String.NanoId(SizeOf(a, 21));
        Add("string.uuid", x => x.String.Uuid());
        Add("string.ulid", x => x.String.Ulid());
        Add("string.uuidV7", x => x.String.UuidV7());
    }

    /// <summary>Registers person template functions, including sex selection and supplied first and last names for full names.</summary>
    private static void RegisterPerson(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        f["person.firstName"] = (faker, a) => faker.Person.FirstName(SexOf(a));
        f["person.lastName"] = (faker, a) => faker.Person.LastName(SexOf(a));
        f["person.middleName"] = (faker, a) => faker.Person.MiddleName(SexOf(a));
        f["person.fullName"] = (faker, a) => faker.Person.FullName(a.OptionString(0, "firstName", null), a.OptionString(0, "lastName", null), SexOf(a, plainAllowed: false));
        Add("person.gender", x => x.Person.Gender());
        Add("person.sex", x => x.Person.SexLabel());
        Add("person.sexLabel", x => x.Person.SexLabel());
        Add("person.bio", x => x.Person.Bio());
        f["person.prefix"] = (faker, a) => faker.Person.Prefix(SexOf(a));
        Add("person.suffix", x => x.Person.Suffix());
        Add("person.jobTitle", x => x.Person.JobTitle());
        Add("person.jobDescriptor", x => x.Person.JobDescriptor());
        Add("person.jobArea", x => x.Person.JobArea());
        Add("person.jobType", x => x.Person.JobType());
        Add("person.zodiacSign", x => x.Person.ZodiacSign());
    }

    /// <summary>Registers location template functions, including postal-code formatting and language values as text.</summary>
    private static void RegisterLocation(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        // The address parts locale templates nest most write straight into the template's buffer; each draws what its LocationModule method draws.
        f["location.zipCode"] = WritingFunction.Of(static (Faker x, TemplateArgs a, ref TextBuilder t) =>
            x.Random.AppendReplacedSymbols(a.String(0, "format", null) ?? x.Pick(DataKeys.LocationPostcode), ref t));
        f["location.city"] = WritingFunction.Of(static (Faker x, TemplateArgs _, ref TextBuilder t) => x.AppendPick(DataKeys.LocationCityPattern, ref t));
        f["location.buildingNumber"] = WritingFunction.Of(static (Faker x, TemplateArgs _, ref TextBuilder t) =>
            x.Helpers.AppendHashRunsWithoutLeadingZero(x.Pick(DataKeys.LocationBuildingNumber), ref t));
        f["location.street"] = WritingFunction.Of(static (Faker x, TemplateArgs _, ref TextBuilder t) => x.AppendPick(DataKeys.LocationStreetPattern, ref t));
        f["location.streetAddress"] = WritingFunction.Of(static (Faker x, TemplateArgs a, ref TextBuilder t) =>
            x.AppendPick(a.Bool(0, "useFullAddress") ?? false ? DataKeys.LocationStreetAddressFull : DataKeys.LocationStreetAddressNormal, ref t));
        f["location.postalAddress"] = WritingFunction.Of(static (Faker x, TemplateArgs _, ref TextBuilder t) => x.AppendPick(DataKeys.LocationPostalAddress, ref t));
        f["location.secondaryAddress"] = WritingFunction.Of(static (Faker x, TemplateArgs _, ref TextBuilder t) =>
            x.Helpers.AppendHashRunsWithoutLeadingZero(x.Pick(DataKeys.LocationSecondaryAddress), ref t));
        Add("location.county", x => x.Location.County());
        Add("location.country", x => x.Location.Country());
        Add("location.continent", x => x.Location.Continent());
        Add("location.countryCode", x => x.Location.CountryCode());
        f["location.state"] = (faker, a) => faker.Location.State(a.Bool(0, "abbreviated") ?? false);
        Add("location.direction", x => x.Location.Direction());
        Add("location.cardinalDirection", x => x.Location.CardinalDirection());
        Add("location.ordinalDirection", x => x.Location.OrdinalDirection());
        Add("location.timeZone", x => x.Location.TimeZone());
        Add("location.latitude", x => x.Location.Latitude().ToString(CultureInfo.InvariantCulture));
        Add("location.longitude", x => x.Location.Longitude().ToString(CultureInfo.InvariantCulture));
        Add("location.address", x => x.Location.Address().ToString());
        Add("location.language", x => x.Location.Language().ToString());
        f["location.zipCodeByState"] = (faker, a) => faker.Location.ZipCodeByState(a.String(0) ?? "");
    }

    /// <summary>The <c>pattern</c> option of <c>internet.password</c> as a regular expression, or <see langword="null"/>; a regular expression is only built when one is given.</summary>
    /// <remarks>A missing or empty pattern returns <see langword="null"/>; matching has a five-second timeout.</remarks>
    /// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
    private static System.Text.RegularExpressions.Regex? PasswordPattern(TemplateArgs a) =>
        a.OptionString(0, "pattern", null) is { Length: > 0 } pattern
            ? new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5))
            : null;

    /// <summary>Registers internet template functions and their email, username, URL, MAC, password and color options.</summary>
    private static void RegisterInternet(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        var inv = CultureInfo.InvariantCulture;
        f["internet.email"] = (faker, a) => faker.Internet.Email(a.OptionString(0, "firstName", null), a.OptionString(0, "lastName", null), a.OptionString(0, "provider", null), a.Bool(0, "allowSpecialCharacters") ?? false);
        Add("internet.exampleEmail", x => x.Internet.ExampleEmail());
        f["internet.username"] = (faker, a) => faker.Internet.Username(a.OptionString(0, "firstName", null), a.OptionString(0, "lastName", null));
        f["internet.userName"] = f["internet.username"];
        Add("internet.displayName", x => x.Internet.DisplayName());
        Add("internet.protocol", x => x.Internet.Protocol());
        Add("internet.httpMethod", x => x.Internet.HttpMethod());
        Add("internet.httpStatusCode", x => x.Internet.HttpStatusCode().ToString(inv));
        f["internet.url"] = (faker, a) => faker.Internet.Url(a.Bool(0, "appendSlash"), a.OptionString(0, "protocol", "https")!);
        Add("internet.domainName", x => x.Internet.DomainName());
        Add("internet.domainSuffix", x => x.Internet.DomainSuffix());
        Add("internet.domainWord", x => x.Internet.DomainWord());
        Add("internet.ip", x => x.Internet.Ip());
        Add("internet.ipv4", x => x.Internet.Ipv4());
        Add("internet.ipv6", x => x.Internet.Ipv6());
        Add("internet.port", x => x.Internet.Port().ToString(inv));
        Add("internet.userAgent", x => x.Internet.UserAgent());
        f["internet.mac"] = (faker, a) => faker.Internet.Mac(a.String(0, "separator", ":")!);
        f["internet.password"] = (faker, a) => faker.Internet.Password(SizeOf(a, 15), a.Bool(0, "memorable") ?? false, PasswordPattern(a), a.OptionString(0, "prefix", "")!);
        f["internet.color"] = (faker, a) => faker.Internet.Color(a.Int(0, "redBase") ?? 0, a.Int(0, "greenBase") ?? 0, a.Int(0, "blueBase") ?? 0);
        Add("internet.emoji", x => x.Internet.Emoji());
        Add("internet.jwtAlgorithm", x => x.Internet.JwtAlgorithm());
        Add("internet.jwt", x => x.Internet.Jwt());
    }

    private static void RegisterNationalId(ConcurrentDictionary<string, TemplateFunction> f)
    {
        f["nationalId.person"] = (faker, a) => faker.NationalId.Person(a.String(0), formatted: a.Bool(1, "formatted") ?? true);
        f["nationalId.company"] = (faker, a) => faker.NationalId.Company(a.String(0), a.Bool(1, "formatted") ?? true);
        f["nationalId.vat"] = (faker, a) => faker.NationalId.Vat(a.String(0), a.Bool(1, "formatted") ?? true);
    }

    private static void RegisterPhone(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("phone.number", x => x.Phone.Number());
        Add("phone.imei", x => x.Phone.Imei());
    }

    private static void RegisterCompany(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("company.name", x => x.Company.Name());
        Add("company.catchPhrase", x => x.Company.CatchPhrase());
        Add("company.buzzPhrase", x => x.Company.BuzzPhrase());
        Add("company.catchPhraseAdjective", x => x.Company.CatchPhraseAdjective());
        Add("company.catchPhraseDescriptor", x => x.Company.CatchPhraseDescriptor());
        Add("company.catchPhraseNoun", x => x.Company.CatchPhraseNoun());
        Add("company.buzzAdjective", x => x.Company.BuzzAdjective());
        Add("company.buzzVerb", x => x.Company.BuzzVerb());
        Add("company.buzzNoun", x => x.Company.BuzzNoun());
    }

    /// <summary>Registers commerce template functions; price and priceText share the symbol, range and decimal-place options.</summary>
    private static void RegisterCommerce(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("commerce.department", x => x.Commerce.Department());
        Add("commerce.productName", x => x.Commerce.ProductName());
        Add("commerce.productAdjective", x => x.Commerce.ProductAdjective());
        Add("commerce.productMaterial", x => x.Commerce.ProductMaterial());
        Add("commerce.product", x => x.Commerce.Product());
        Add("commerce.productDescription", x => x.Commerce.ProductDescription());
        Add("commerce.isbn", x => x.Commerce.Isbn());
        Add("commerce.upc", x => x.Commerce.Upc());
        Add("commerce.ean13", x => x.Commerce.Ean13());
        Add("commerce.ean8", x => x.Commerce.Ean8());
        f["commerce.price"] = (faker, a) => faker.Commerce.PriceText(
            a.OptionString(0, "symbol", "")!, (decimal)(a.Double(0, "min") ?? 1), (decimal)(a.Double(0, "max") ?? 1000), a.Int(0, "dec") ?? 2);
        f["commerce.priceText"] = f["commerce.price"];
    }

    /// <summary>Registers finance template functions, including formatted amounts, optional card issuers or masks, and currency values as text.</summary>
    private static void RegisterFinance(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        f["finance.accountNumber"] = (faker, a) => faker.Finance.AccountNumber(SizeOf(a, 8));
        Add("finance.accountName", x => x.Finance.AccountName());
        Add("finance.routingNumber", x => x.Finance.RoutingNumber());
        f["finance.amount"] = (faker, a) => faker.Finance.AmountText(
            (decimal)(a.Double(0, "min") ?? 0), (decimal)(a.Double(0, "max") ?? 1000), a.Int(0, "dec") ?? 2, a.OptionString(0, "symbol", "")!);
        Add("finance.transactionType", x => x.Finance.TransactionType());
        Add("finance.transactionDescription", x => x.Finance.TransactionDescription());
        Add("finance.currencyCode", x => x.Finance.CurrencyCode());
        Add("finance.currencyName", x => x.Finance.CurrencyName());
        Add("finance.currencySymbol", x => x.Finance.CurrencySymbol());
        Add("finance.currencyNumericCode", x => x.Finance.CurrencyNumericCode());
        Add("finance.creditCardIssuer", x => x.Finance.CreditCardIssuer().ToString());
        f["finance.creditCardNumber"] = (faker, a) => a.String(0, "issuer", null) is { Length: > 0 } issuer
            ? issuer.Contains('#') ? faker.Finance.CreditCardNumber(issuer) : faker.Finance.CreditCardNumber(ParseIssuer(issuer))
            : faker.Finance.CreditCardNumber();
        Add("finance.creditCardCVV", x => x.Finance.CreditCardCvv());
        f["finance.pin"] = (faker, a) => faker.Finance.Pin(SizeOf(a, 4));
        Add("finance.ethereumAddress", x => x.Finance.EthereumAddress());
        Add("finance.bitcoinAddress", x => x.Finance.BitcoinAddress());
        Add("finance.litecoinAddress", x => x.Finance.LitecoinAddress());
        Add("finance.currency", x => x.Finance.Currency().ToString());
        Add("finance.iban", x => x.Finance.Iban());
        Add("finance.bic", x => x.Finance.Bic());
        f["finance.amountText"] = f["finance.amount"];
    }

    /// <summary>Registers relative dates, month and weekday names, time zones, times of day and birthdates with age or year bounds.</summary>
    private static void RegisterDate(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        var inv = CultureInfo.InvariantCulture;
        Add("date.anytime", x => x.Date.Anytime().ToString("O", inv));
        f["date.past"] = (faker, a) => faker.Date.Past(a.Int(0, "years") ?? 1).ToString("O", inv);
        f["date.future"] = (faker, a) => faker.Date.Future(a.Int(0, "years") ?? 1).ToString("O", inv);
        f["date.recent"] = (faker, a) => faker.Date.Recent(a.Int(0, "days") ?? 1).ToString("O", inv);
        f["date.soon"] = (faker, a) => faker.Date.Soon(a.Int(0, "days") ?? 1).ToString("O", inv);
        f["date.birthdate"] = (faker, a) => (a.OptionString(0, "mode", "age")?.ToUpperInvariant() switch
        {
            "AGE" => faker.Date.Birthdate(a.Int(0, "min") ?? 18, a.Int(0, "max") ?? 80),
            "YEAR" => faker.Date.BirthdateInYears(a.Int(0, "min") ?? 1920, a.Int(0, "max") ?? 2000),
            var other => throw new FormatException($"The mode must be 'age' or 'year', but was '{other}'."),
        }).ToString("O", inv);
        f["date.month"] = (faker, a) => faker.Date.Month(a.Bool(0, "abbreviated") ?? false, a.Bool(0, "context") ?? false);
        f["date.weekday"] = (faker, a) => faker.Date.Weekday(a.Bool(0, "abbreviated") ?? false, a.Bool(0, "context") ?? false);
        Add("date.timeZone", x => x.Date.TimeZone());
        Add("date.timeOfDay", x => x.Date.TimeOfDay().ToString("HH:mm:ss", inv));
    }

    /// <summary>Registers lorem template functions with bounded counts; omitted optional counts use the module's defaults.</summary>
    private static void RegisterLorem(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        f["lorem.word"] = (faker, a) => faker.Lorem.Word(a.Int(0, "length"), a.Int(0, "length"));
        f["lorem.words"] = (faker, a) => faker.Lorem.Words(SizeOf(a, 3, "count", MaxTemplateWords));
        f["lorem.sentence"] = (faker, a) => faker.Lorem.Sentence(OptionalSizeOf(a, "count", MaxTemplateWords));
        f["lorem.sentences"] = (faker, a) => faker.Lorem.Sentences(OptionalSizeOf(a, "count", MaxTemplateSentences));
        f["lorem.paragraph"] = (faker, a) => faker.Lorem.Paragraph(SizeOf(a, 3, "count", MaxTemplateSentences));
        f["lorem.paragraphs"] = (faker, a) => faker.Lorem.Paragraphs(SizeOf(a, 3, "count", MaxTemplateParagraphs));
        f["lorem.lines"] = (faker, a) => faker.Lorem.Lines(OptionalSizeOf(a, "count", MaxTemplateSentences));
        f["lorem.slug"] = (faker, a) => faker.Lorem.Slug(SizeOf(a, 3, "count", MaxTemplateWords));
        Add("lorem.text", x => x.Lorem.Text());
    }

    /// <summary>Registers word template functions, including length filters and an optional bounded word count.</summary>
    private static void RegisterWord(ConcurrentDictionary<string, TemplateFunction> f)
    {
        f["word.adjective"] = (faker, a) => faker.Word.Adjective(a.Int(0, "length"), a.Int(0, "length"));
        f["word.adverb"] = (faker, a) => faker.Word.Adverb(a.Int(0, "length"), a.Int(0, "length"));
        f["word.conjunction"] = (faker, a) => faker.Word.Conjunction(a.Int(0, "length"), a.Int(0, "length"));
        f["word.interjection"] = (faker, a) => faker.Word.Interjection(a.Int(0, "length"), a.Int(0, "length"));
        f["word.noun"] = (faker, a) => faker.Word.Noun(a.Int(0, "length"), a.Int(0, "length"));
        f["word.preposition"] = (faker, a) => faker.Word.Preposition(a.Int(0, "length"), a.Int(0, "length"));
        f["word.verb"] = (faker, a) => faker.Word.Verb(a.Int(0, "length"), a.Int(0, "length"));
        f["word.sample"] = (faker, a) => faker.Word.Sample(a.Int(0, "length"), a.Int(0, "length"));
        f["word.words"] = (faker, a) => faker.Word.Words(OptionalSizeOf(a, "count", MaxTemplateWords));
    }

    /// <summary>Registers color template functions, including hex options and CSS representations of color values.</summary>
    private static void RegisterColor(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("color.human", x => x.Color.Human());
        Add("color.space", x => x.Color.Space());
        Add("color.rgb", x => x.Color.Hex());
        f["color.hex"] = (faker, a) => faker.Color.Hex(a.OptionString(0, "prefix", "#")!, CasingOf(a, Casing.Lower), a.Bool(0, "includeAlpha") ?? false);
        Add("color.cmyk", x => x.Color.Cmyk().ToCss());
        Add("color.hsl", x => x.Color.Hsl().ToCss());
        Add("color.hwb", x => x.Color.Hwb().ToCss());
        Add("color.lab", x => x.Color.Lab().ToCss());
        Add("color.lch", x => x.Color.Lch().ToCss());
        Add("color.cssSupportedFunction", x => x.Color.CssSupportedFunction().ToString().ToLowerInvariant());
        Add("color.cssSupportedSpace", x => ColorModule.CssSpaceName(x.Color.CssSupportedSpace()));
        Add("color.colorByCssColorSpace", x => x.Color.ColorByCssColorSpace());
    }

    private static void RegisterAnimal(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("animal.dog", x => x.Animal.Dog());
        Add("animal.cat", x => x.Animal.Cat());
        Add("animal.snake", x => x.Animal.Snake());
        Add("animal.bear", x => x.Animal.Bear());
        Add("animal.lion", x => x.Animal.Lion());
        Add("animal.cetacean", x => x.Animal.Cetacean());
        Add("animal.horse", x => x.Animal.Horse());
        Add("animal.bird", x => x.Animal.Bird());
        Add("animal.cow", x => x.Animal.Cow());
        Add("animal.fish", x => x.Animal.Fish());
        Add("animal.crocodilia", x => x.Animal.Crocodilia());
        Add("animal.insect", x => x.Animal.Insect());
        Add("animal.rabbit", x => x.Animal.Rabbit());
        Add("animal.rodent", x => x.Animal.Rodent());
        Add("animal.type", x => x.Animal.Type());
        Add("animal.petName", x => x.Animal.PetName());
    }

    private static void RegisterBook(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("book.author", x => x.Book.Author());
        Add("book.format", x => x.Book.Format());
        Add("book.genre", x => x.Book.Genre());
        Add("book.publisher", x => x.Book.Publisher());
        Add("book.series", x => x.Book.Series());
        Add("book.title", x => x.Book.Title());
    }

    private static void RegisterMusic(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("music.album", x => x.Music.Album());
        Add("music.artist", x => x.Music.Artist());
        Add("music.genre", x => x.Music.Genre());
        Add("music.songName", x => x.Music.SongName());
    }

    private static void RegisterFood(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("food.adjective", x => x.Food.Adjective());
        Add("food.description", x => x.Food.Description());
        Add("food.dish", x => x.Food.Dish());
        Add("food.ethnicCategory", x => x.Food.EthnicCategory());
        Add("food.fruit", x => x.Food.Fruit());
        Add("food.ingredient", x => x.Food.Ingredient());
        Add("food.meat", x => x.Food.Meat());
        Add("food.spice", x => x.Food.Spice());
        Add("food.vegetable", x => x.Food.Vegetable());
    }

    private static void RegisterHacker(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("hacker.abbreviation", x => x.Hacker.Abbreviation());
        Add("hacker.adjective", x => x.Hacker.Adjective());
        Add("hacker.noun", x => x.Hacker.Noun());
        Add("hacker.verb", x => x.Hacker.Verb());
        Add("hacker.ingverb", x => x.Hacker.IngVerb());
        Add("hacker.phrase", x => x.Hacker.Phrase());
    }

    private static void RegisterVehicle(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("vehicle.vehicle", x => x.Vehicle.Vehicle());
        Add("vehicle.manufacturer", x => x.Vehicle.Manufacturer());
        Add("vehicle.model", x => x.Vehicle.Model());
        Add("vehicle.type", x => x.Vehicle.Type());
        Add("vehicle.fuel", x => x.Vehicle.Fuel());
        Add("vehicle.vin", x => x.Vehicle.Vin());
        Add("vehicle.color", x => x.Vehicle.Color());
        Add("vehicle.vrm", x => x.Vehicle.Vrm());
        Add("vehicle.bicycle", x => x.Vehicle.Bicycle());
    }

    /// <summary>Registers airline template functions, formatting typed values as text and aircraft types in lower case.</summary>
    private static void RegisterAirline(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("airline.recordLocator", x => x.Airline.RecordLocator());
        Add("airline.seat", x => x.Airline.Seat());
        Add("airline.flightNumber", x => x.Airline.FlightNumber());
        Add("airline.airline", x => x.Airline.Airline().ToString());
        Add("airline.airplane", x => x.Airline.Airplane().ToString());
        Add("airline.airport", x => x.Airline.Airport().ToString());
        Add("airline.aircraftType", x => x.Airline.AircraftType().ToString().ToLowerInvariant());
    }

    /// <summary>Registers chemical-element and scientific-unit template functions using the values' text representations.</summary>
    private static void RegisterScience(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("science.chemicalElement", x => x.Science.ChemicalElement().ToString());
        Add("science.unit", x => x.Science.Unit().ToString());
    }

    /// <summary>Registers datatype.boolean as lower-case JSON boolean text, with a probability option or plain probability (default 0.5).</summary>
    private static void RegisterDatatype(ConcurrentDictionary<string, TemplateFunction> f) =>
        f["datatype.boolean"] = (faker, a) => faker.Random.Bool(a.Double(0, "probability") ?? 0.5) ? "true" : "false";

    private static void RegisterDatabase(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("database.column", x => x.Database.Column());
        Add("database.type", x => x.Database.Type());
        Add("database.collation", x => x.Database.Collation());
        Add("database.engine", x => x.Database.Engine());
        Add("database.mongodbObjectId", x => x.Database.MongoDbObjectId());
    }

    private static void RegisterSystem(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        f["system.fileName"] = (faker, a) => faker.System.FileName(a.Int(0, "extensionCount") ?? 1);
        Add("system.commonFileName", x => x.System.CommonFileName());
        Add("system.mimeType", x => x.System.MimeType());
        Add("system.commonFileType", x => x.System.CommonFileType());
        Add("system.commonFileExt", x => x.System.CommonFileExt());
        Add("system.fileType", x => x.System.FileType());
        f["system.fileExt"] = (faker, a) => faker.System.FileExt(a.String(0));
        Add("system.directoryPath", x => x.System.DirectoryPath());
        Add("system.filePath", x => x.System.FilePath());
        Add("system.semver", x => x.System.Semver());
        Add("system.networkInterface", x => x.System.NetworkInterface());
        Add("system.cron", x => x.System.Cron());
    }

    /// <summary>Registers Git template functions, including commit dates, log entries and a bounded SHA length.</summary>
    private static void RegisterGit(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("git.branch", x => x.Git.Branch());
        Add("git.commitMessage", x => x.Git.CommitMessage());
        Add("git.commitDate", x => x.Git.CommitDate());
        Add("git.commitEntry", x => x.Git.CommitEntry());
        f["git.commitSha"] = (faker, a) => faker.Git.CommitSha(SizeOf(a, 40));
    }

    /// <summary>Registers image template functions for avatar URLs, photo URLs and placeholder data URIs.</summary>
    private static void RegisterImage(ConcurrentDictionary<string, TemplateFunction> f)
    {
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);
        Add("image.avatar", x => x.Image.Avatar());
        Add("image.personPortrait", x => x.Image.Avatar()); // faker.js name
        Add("image.avatarGitHub", x => x.Image.AvatarGitHub());
        Add("image.url", x => x.Image.Url());
        Add("image.dataUri", x => x.Image.DataUri());
    }
}
