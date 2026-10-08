using System.Collections.Concurrent;
using System.Globalization;
using Phonery.Modules;

namespace Phonery.Templates;

/// <summary>A function callable from templates, e.g. <c>{{person.firstName}}</c> or <c>{{string.numeric(4)}}</c>.</summary>
/// <param name="faker">The faker evaluating the template.</param>
/// <param name="args">Arguments given in parentheses; <see cref="TemplateArgs.Count"/> is 0 without parentheses.</param>
public delegate string TemplateFunction(Faker faker, TemplateArgs args);

/// <summary>
/// Registry of the functions templates can call. Names use faker.js spelling (<c>module.camelCaseMethod</c>) so
/// faker.js locale data and templates written for faker.js work unchanged. Register your own to extend templates.
/// </summary>
public static partial class TemplateFunctions
{
    private static readonly ConcurrentDictionary<string, TemplateFunction> Functions = new(CreateBuiltIns(), StringComparer.Ordinal);

    /// <summary>Names of all registered functions.</summary>
    public static IReadOnlyCollection<string> Names => [.. Functions.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Registers (or replaces) a template function, e.g. <c>Register("my.sku", (f, _) => f.Random.Replace("SKU-#####"))</c>.</summary>
    /// <remarks>Register functions before templates that use them are first evaluated; parsed templates are cached.</remarks>
    public static void Register(string name, TemplateFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        if (name.Contains('(') || name.Contains('{'))
            throw new ArgumentException($"'{name}' is not a valid function name.", nameof(name));
        Functions[name] = function;
    }

    /// <summary>Whether a function with this name is registered.</summary>
    public static bool Contains(string name) => Functions.ContainsKey(name);

    /// <summary>The function registered as <paramref name="name"/>, or <see langword="null"/>.</summary>
    internal static TemplateFunction? Get(string name) => Functions.TryGetValue(name, out var function) ? function : null;

    /// <summary>Built-in functions mirroring faker.js methods. Arguments follow faker.js (positional or an options object).</summary>
    private static Dictionary<string, TemplateFunction> CreateBuiltIns()
    {
        var f = new Dictionary<string, TemplateFunction>(StringComparer.Ordinal);

        // Methods without arguments: name → module call.
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);

        // helpers
        f["helpers.arrayElement"] = (faker, a) => faker.Random.Element(a.Strings(0));
        f["helpers.fromRegExp"] = (faker, a) => faker.Helpers.FromRegExp(a.String(0) ?? "");
        f["helpers.replaceSymbols"] = (faker, a) => faker.Helpers.ReplaceSymbols(a.String(0) ?? "");
        f["helpers.replaceSymbolWithNumber"] = (faker, a) => faker.Helpers.ReplaceSymbolWithNumber(a.String(0) ?? "");
        f["helpers.replaceCreditCardSymbols"] = (faker, a) => a.Count == 0 ? faker.Helpers.ReplaceCreditCardSymbols() : faker.Helpers.ReplaceCreditCardSymbols(a.String(0)!);
        f["helpers.slugify"] = (_, a) => HelpersModule.Slugify(a.String(0) ?? "");

        // number
        f["number.int"] = (faker, a) => Number(faker, a, 0, int.MaxValue).ToString(CultureInfo.InvariantCulture);
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

        // string
        f["string.numeric"] = (faker, a) => faker.String.Numeric(a.Int(0, "length") ?? 1, a.Bool(0, "allowLeadingZeros") ?? a.Bool(1, "allowLeadingZeros") ?? true);
        f["string.alpha"] = (faker, a) => faker.String.Alpha(a.Int(0, "length") ?? 1);
        f["string.alphanumeric"] = (faker, a) => faker.String.AlphaNumeric(a.Int(0, "length") ?? 1);
        f["string.hexadecimal"] = (faker, a) => faker.String.Hexadecimal(a.Int(0, "length") ?? 1);
        f["string.binary"] = (faker, a) => faker.String.Binary(a.Int(0, "length") ?? 1);
        f["string.octal"] = (faker, a) => faker.String.Octal(a.Int(0, "length") ?? 1);
        f["string.sample"] = (faker, a) => faker.String.Sample(a.Int(0) ?? 10);
        f["string.symbol"] = (faker, a) => faker.String.Symbol(a.Int(0) ?? 1);
        f["string.nanoid"] = (faker, a) => faker.String.NanoId(a.Int(0) ?? 21);
        Add("string.uuid", x => x.String.Uuid());
        Add("string.ulid", x => x.String.Ulid());

        // person
        Add("person.firstName", x => x.Person.FirstName());
        Add("person.lastName", x => x.Person.LastName());
        Add("person.middleName", x => x.Person.MiddleName());
        Add("person.fullName", x => x.Person.FullName());
        Add("person.gender", x => x.Person.Gender());
        Add("person.sex", x => x.Person.Sex());
        Add("person.bio", x => x.Person.Bio());
        Add("person.prefix", x => x.Person.Prefix());
        Add("person.suffix", x => x.Person.Suffix());
        Add("person.jobTitle", x => x.Person.JobTitle());
        Add("person.jobDescriptor", x => x.Person.JobDescriptor());
        Add("person.jobArea", x => x.Person.JobArea());
        Add("person.jobType", x => x.Person.JobType());
        Add("person.zodiacSign", x => x.Person.ZodiacSign());

        // location
        Add("location.zipCode", x => x.Location.ZipCode());
        Add("location.city", x => x.Location.City());
        Add("location.buildingNumber", x => x.Location.BuildingNumber());
        Add("location.street", x => x.Location.Street());
        f["location.streetAddress"] = (faker, a) => faker.Location.StreetAddress(a.Bool(0, "useFullAddress") ?? false);
        Add("location.postalAddress", x => x.Location.PostalAddress());
        Add("location.secondaryAddress", x => x.Location.SecondaryAddress());
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

        RegisterMoreBuiltIns(f);
        return f;
    }

    /// <summary>Hook for the remaining modules, implemented in <c>TemplateFunctions.Modules.cs</c>.</summary>
    static partial void RegisterMoreBuiltIns(Dictionary<string, TemplateFunction> functions);

    /// <summary>faker.js <c>number.int</c> accepts either a max or a <c>{min, max, multipleOf}</c> object.</summary>
    private static long Number(Faker faker, TemplateArgs a, long defaultMin, long defaultMax)
    {
        var min = a.Int(0, "min") ?? defaultMin;
        var max = a.Int(0, "max") ?? a.Int(0) ?? defaultMax;
        return faker.Number.Long(min, max, a.Int(0, "multipleOf") ?? 1);
    }
}
