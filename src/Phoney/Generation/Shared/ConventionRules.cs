// Shared between the Phoney runtime (net8.0+) and Phoney.SourceGenerator (netstandard2.0), so the reflection
// and source-generated paths pick identical conventions. Keep this file free of APIs newer than netstandard2.0.
// The generator compiles it with PHONEY_GENERATOR so these types stay internal there.
#nullable enable

using System;
using System.Collections.Generic;

namespace Phoney.Generation;

/// <summary>Type categories that conventions are defined for.</summary>
#if PHONEY_GENERATOR
internal
#else
public
#endif
enum TypeCategory
{
    /// <summary>Anything without a dedicated category (complex types, collections…).</summary>
    Other,
#pragma warning disable CS1591 // self-explanatory
    String, Char, Bool, Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double, Decimal,
    Guid, DateTime, DateTimeOffset, DateOnly, TimeOnly, TimeSpan, Uri, Enum,
#pragma warning restore CS1591
}

/// <summary>What a member's value is generated as, decided from its name and type.</summary>
#if PHONEY_GENERATOR
internal
#else
public
#endif
enum ConventionKind
{
    /// <summary>No name convention: the value is generated from the type alone.</summary>
    None,
#pragma warning disable CS1591 // each kind maps to the module method of the same name
    FirstName, LastName, MiddleName, FullName, Prefix, Suffix, Gender, Sex, JobTitle, Bio,
    Username, DisplayName, Email, Password, Phone,
    StreetAddress, SecondaryAddress, City, ZipCode, State, County, Country, CountryCode, Latitude, Longitude, TimeZone, Language,
    CompanyName, Department, ProductName, ProductDescription, Title, Description, Paragraph, Word, Slug,
    Url, DomainName, Ip, Ipv4, Ipv6, Mac, UserAgent, Avatar, ImageUrl, Color, HexColor, Emoji,
    CurrencyCode, Iban, Bic, CreditCardNumber, Cvv, AccountNumber, RoutingNumber, Pin, Isbn, Upc, Sku, Vin, LicensePlate,
    Uuid, Token, Version, FileName, FilePath, MimeType, FileExtension,
    Id, ReferenceId, Age, Price, Quantity, Rating, Year, Percentage, Port,
    BirthDate, PastDate, RecentDate, FutureDate,
#pragma warning restore CS1591
}

/// <summary>
/// Maps member names (and the declaring type's name, for ambiguous names like <c>Name</c>) to conventions.
/// Names are normalized (case, <c>_</c> and <c>-</c> ignored); an exact match wins, otherwise the longest
/// trailing run of whole words that matches (so <c>BillingCity</c> → <see cref="ConventionKind.City"/>, but
/// <c>Monkey</c> does not match <c>key</c>).
/// </summary>
#if PHONEY_GENERATOR
internal
#else
public
#endif
static class ConventionRules
{
    private static readonly Dictionary<string, (ConventionKind Kind, Accepts Accepts)> Names = Build();

    /// <summary>Which type categories a name convention applies to.</summary>
    private enum Accepts
    {
        Text,
        Integer,
        Number,
        Date,
        IdLike,
    }

    /// <summary>Returns the convention for a member, or <see cref="ConventionKind.None"/>.</summary>
    /// <param name="memberName">Property, field or constructor parameter name.</param>
    /// <param name="category">The member's type category (nullable value types use their underlying type).</param>
    /// <param name="declaringTypeName">Simple name of the type that declares the member, e.g. <c>Customer</c>.</param>
    public static ConventionKind Match(string memberName, TypeCategory category, string declaringTypeName)
    {
        var name = Normalize(memberName);
        if (name.Length == 0)
            return ConventionKind.None;

        if (name == "name" || name == "title")
            return Fits(Accepts.Text, category) ? ContextualName(name, Normalize(declaringTypeName)) : ConventionKind.None;

        if (Names.TryGetValue(name, out var exact))
            return Fits(exact.Accepts, category) ? exact.Kind : ConventionKind.None;

        // Try trailing word runs, longest first: "HomeEmailAddress" → "emailaddress" before "address".
        var words = Words(memberName);
        for (var start = 1; start < words.Count; start++)
        {
            var suffix = string.Concat(words.GetRange(start, words.Count - start));
            if (Names.TryGetValue(suffix, out var match) && Fits(match.Accepts, category))
                return match.Kind == ConventionKind.Id ? ConventionKind.ReferenceId : match.Kind; // CustomerId is a reference, not this object's id
        }

        return ConventionKind.None;
    }

    /// <summary>Splits <c>billingAddress_Line2</c> into lower-case words: billing, address, line2.</summary>
    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_' || c == '-' || c == ' ' || c == '@')
            {
                Flush();
                continue;
            }

            // A new word starts at an upper-case letter after a lower-case letter or digit, or before "Xy" in "XMLParser".
            if (char.IsUpper(c) && current.Length > 0 &&
                (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                Flush();
            current.Append(char.ToLowerInvariant(c));
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
                words.Add(current.ToString());
            current.Clear();
        }
    }

    /// <summary>Lower-cases and removes separators: <c>First_Name</c> → <c>firstname</c>.</summary>
    public static string Normalize(string name)
    {
        var chars = new char[name.Length];
        var length = 0;
        foreach (var c in name)
        {
            if (c != '_' && c != '-' && c != ' ' && c != '@')
                chars[length++] = char.ToLowerInvariant(c);
        }

        return new string(chars, 0, length);
    }

    /// <summary>Maps a <see cref="Type"/>-independent category from a type's full name (used by both paths).</summary>
    public static TypeCategory CategoryOf(string fullTypeName, bool isEnum) => isEnum ? TypeCategory.Enum : fullTypeName switch
    {
        "System.String" => TypeCategory.String,
        "System.Char" => TypeCategory.Char,
        "System.Boolean" => TypeCategory.Bool,
        "System.Byte" => TypeCategory.Byte,
        "System.SByte" => TypeCategory.SByte,
        "System.Int16" => TypeCategory.Int16,
        "System.UInt16" => TypeCategory.UInt16,
        "System.Int32" => TypeCategory.Int32,
        "System.UInt32" => TypeCategory.UInt32,
        "System.Int64" => TypeCategory.Int64,
        "System.UInt64" => TypeCategory.UInt64,
        "System.Single" => TypeCategory.Single,
        "System.Double" => TypeCategory.Double,
        "System.Decimal" => TypeCategory.Decimal,
        "System.Guid" => TypeCategory.Guid,
        "System.DateTime" => TypeCategory.DateTime,
        "System.DateTimeOffset" => TypeCategory.DateTimeOffset,
        "System.DateOnly" => TypeCategory.DateOnly,
        "System.TimeOnly" => TypeCategory.TimeOnly,
        "System.TimeSpan" => TypeCategory.TimeSpan,
        "System.Uri" => TypeCategory.Uri,
        _ => TypeCategory.Other,
    };

    /// <summary>Whether a category is numeric.</summary>
    public static bool IsNumber(TypeCategory c) => c >= TypeCategory.Byte && c <= TypeCategory.Decimal;

    /// <summary>Whether a category is an integer type.</summary>
    private static bool IsInteger(TypeCategory c) => c >= TypeCategory.Byte && c <= TypeCategory.UInt64;

    /// <summary>Whether a category represents a date.</summary>
    private static bool IsDate(TypeCategory c) => c == TypeCategory.DateTime || c == TypeCategory.DateTimeOffset || c == TypeCategory.DateOnly;

    /// <summary>Whether a name convention applies to a member of category <paramref name="c"/>.</summary>
    private static bool Fits(Accepts accepts, TypeCategory c) => accepts switch
    {
        Accepts.Text => c == TypeCategory.String,
        Accepts.Integer => IsInteger(c),
        Accepts.Number => IsNumber(c),
        Accepts.Date => IsDate(c),
        Accepts.IdLike => IsInteger(c) || c == TypeCategory.Guid || c == TypeCategory.String,
        _ => false,
    };

    /// <summary><c>Name</c> and <c>Title</c> depend on what the type represents.</summary>
    private static ConventionKind ContextualName(string name, string typeName)
    {
        bool Is(params string[] words)
        {
            foreach (var w in words)
            {
                if (typeName.EndsWith(w, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        if (name == "title")
        {
            if (Is("book", "album", "song", "movie", "film", "article", "post", "blog", "task", "todo", "issue", "ticket", "event"))
                return ConventionKind.Title;
            return Is("person", "employee", "user", "member", "staff", "contact", "author") ? ConventionKind.JobTitle : ConventionKind.Title;
        }

        if (Is("company", "organization", "organisation", "business", "employer", "vendor", "supplier", "manufacturer", "brand", "tenant", "customerorganization"))
            return ConventionKind.CompanyName;
        if (Is("product", "item", "article", "sku", "good", "offer"))
            return ConventionKind.ProductName;
        if (Is("category", "department", "section"))
            return ConventionKind.Department;
        if (Is("city", "town"))
            return ConventionKind.City;
        if (Is("country"))
            return ConventionKind.Country;
        if (Is("file", "document", "attachment"))
            return ConventionKind.FileName;
        if (Is("tag", "label", "keyword", "topic"))
            return ConventionKind.Word;
        return ConventionKind.FullName;
    }

    /// <summary>The name table: normalized member name → convention and accepted types.</summary>
    private static Dictionary<string, (ConventionKind, Accepts)> Build()
    {
        var map = new Dictionary<string, (ConventionKind, Accepts)>(StringComparer.Ordinal);
        void Text(ConventionKind kind, params string[] names) => Add(map, kind, Accepts.Text, names);

        Text(ConventionKind.FirstName, "firstname", "givenname", "forename", "fname");
        Text(ConventionKind.LastName, "lastname", "surname", "familyname", "lname");
        Text(ConventionKind.MiddleName, "middlename");
        Text(ConventionKind.FullName, "fullname", "contactname", "customername", "personname", "authorname", "ownername", "employeename", "recipientname");
        Text(ConventionKind.Prefix, "prefix", "salutation", "honorific", "nameprefix");
        Text(ConventionKind.Suffix, "suffix", "namesuffix");
        Text(ConventionKind.Gender, "gender");
        Text(ConventionKind.Sex, "sex");
        Text(ConventionKind.JobTitle, "jobtitle", "position", "occupation", "role", "jobrole");
        Text(ConventionKind.Bio, "bio", "biography", "about", "aboutme");
        Text(ConventionKind.Username, "username", "login", "loginname", "handle", "nickname", "screenname");
        Text(ConventionKind.DisplayName, "displayname");
        Text(ConventionKind.Email, "email", "emailaddress", "mail", "mailaddress");
        Text(ConventionKind.Password, "password", "pwd", "passphrase");
        Text(ConventionKind.Phone, "phone", "phonenumber", "telephone", "tel", "mobile", "mobilephone", "mobilenumber", "cellphone", "cell", "fax", "faxnumber");
        Text(ConventionKind.StreetAddress, "street", "streetaddress", "address", "address1", "addressline1", "line1", "streetname");
        Text(ConventionKind.SecondaryAddress, "address2", "addressline2", "line2", "apartment", "suite");
        Text(ConventionKind.City, "city", "town", "cityname");
        Text(ConventionKind.ZipCode, "zip", "zipcode", "postcode", "postalcode", "postnumber");
        Text(ConventionKind.State, "state", "province", "region", "statename");
        Text(ConventionKind.County, "county");
        Text(ConventionKind.Country, "country", "countryname");
        Text(ConventionKind.CountryCode, "countrycode", "countryiso", "isocountry");
        Text(ConventionKind.TimeZone, "timezone", "tz", "timezoneid");
        Text(ConventionKind.Language, "language", "languagecode", "locale", "culture", "lang");
        Text(ConventionKind.CompanyName, "company", "companyname", "organization", "organisation", "employer", "business", "businessname", "vendor", "supplier");
        Text(ConventionKind.Department, "department", "category", "division");
        Text(ConventionKind.ProductName, "product", "productname", "itemname");
        Text(ConventionKind.ProductDescription, "productdescription");
        Text(ConventionKind.Title, "headline", "subject", "caption", "heading");
        Text(ConventionKind.Description, "description", "summary", "details", "excerpt", "abstract", "remark", "remarks");
        Text(ConventionKind.Paragraph, "notes", "note", "comment", "comments", "text", "body", "content", "message", "review", "feedback");
        Text(ConventionKind.Word, "tag", "keyword", "label", "word", "code");
        Text(ConventionKind.Slug, "slug", "permalink");
        Text(ConventionKind.Url, "url", "uri", "website", "homepage", "link", "weburl", "webpage", "site");
        Text(ConventionKind.DomainName, "domain", "domainname", "hostname", "host");
        Text(ConventionKind.Ip, "ip", "ipaddress");
        Text(ConventionKind.Ipv4, "ipv4", "ipv4address");
        Text(ConventionKind.Ipv6, "ipv6", "ipv6address");
        Text(ConventionKind.Mac, "mac", "macaddress");
        Text(ConventionKind.UserAgent, "useragent");
        Text(ConventionKind.Avatar, "avatar", "avatarurl", "profilepicture", "profileimage", "profilephoto");
        Text(ConventionKind.ImageUrl, "image", "imageurl", "photo", "photourl", "picture", "pictureurl", "thumbnail", "thumbnailurl", "logo", "logourl");
        Text(ConventionKind.Color, "color", "colour");
        Text(ConventionKind.HexColor, "hexcolor", "colorhex", "colorcode");
        Text(ConventionKind.Emoji, "emoji");
        Text(ConventionKind.CurrencyCode, "currency", "currencycode", "currencyiso");
        Text(ConventionKind.Iban, "iban");
        Text(ConventionKind.Bic, "bic", "swift", "swiftcode", "biccode");
        Text(ConventionKind.CreditCardNumber, "creditcard", "creditcardnumber", "cardnumber", "ccnumber", "pan");
        Text(ConventionKind.Cvv, "cvv", "cvc", "cvv2", "securitycode");
        Text(ConventionKind.AccountNumber, "accountnumber", "bankaccount", "bankaccountnumber");
        Text(ConventionKind.RoutingNumber, "routingnumber", "aba");
        Text(ConventionKind.Pin, "pin", "pincode");
        Text(ConventionKind.Isbn, "isbn");
        Text(ConventionKind.Upc, "upc", "ean", "gtin", "barcode");
        Text(ConventionKind.Sku, "sku", "partnumber", "productcode", "articlenumber");
        Text(ConventionKind.Vin, "vin");
        Text(ConventionKind.LicensePlate, "licenseplate", "licenceplate", "registrationnumber", "numberplate", "plate");
        Text(ConventionKind.Uuid, "uuid", "guid", "correlationid", "externalid", "referenceid", "traceid");
        Text(ConventionKind.Token, "token", "apikey", "secret", "accesstoken", "refreshtoken", "key", "hash", "passwordhash", "salt");
        Text(ConventionKind.Version, "version", "semver");
        Text(ConventionKind.FileName, "filename", "file");
        Text(ConventionKind.FilePath, "filepath", "path", "directory", "folder");
        Text(ConventionKind.MimeType, "mimetype", "contenttype", "mediatype");
        Text(ConventionKind.FileExtension, "extension", "fileextension", "ext");

        Add(map, ConventionKind.Latitude, Accepts.Number, "latitude", "lat");
        Add(map, ConventionKind.Longitude, Accepts.Number, "longitude", "lng", "lon", "long");
        Add(map, ConventionKind.Id, Accepts.IdLike, "id");
        Add(map, ConventionKind.Age, Accepts.Integer, "age");
        Add(map, ConventionKind.Price, Accepts.Number, "price", "amount", "cost", "total", "subtotal", "balance", "salary", "fee", "tax", "discount", "unitprice", "totalprice", "income", "revenue", "budget");
        Add(map, ConventionKind.Quantity, Accepts.Integer, "quantity", "qty", "count", "stock", "units", "itemcount");
        Add(map, ConventionKind.Rating, Accepts.Number, "rating", "stars", "score");
        Add(map, ConventionKind.Year, Accepts.Integer, "year", "modelyear", "releaseyear");
        Add(map, ConventionKind.Percentage, Accepts.Number, "percent", "percentage", "progress");
        Add(map, ConventionKind.Port, Accepts.Integer, "port");

        Add(map, ConventionKind.BirthDate, Accepts.Date, "birthdate", "dateofbirth", "dob", "birthday", "born", "bornon");
        Add(map, ConventionKind.PastDate, Accepts.Date, "created", "createdat", "createdon", "createddate", "registeredat", "registered", "joinedat", "joined", "startdate", "startedat", "since", "publishedat", "published", "issuedat", "orderdate", "date");
        Add(map, ConventionKind.RecentDate, Accepts.Date, "updated", "updatedat", "updatedon", "modified", "modifiedat", "modifiedon", "lastlogin", "lastloginat", "lastseen", "lastmodified", "lastupdated", "lastactive", "timestamp");
        Add(map, ConventionKind.FutureDate, Accepts.Date, "expires", "expiresat", "expiry", "expirydate", "expirationdate", "duedate", "due", "dueat", "enddate", "endsat", "deadline", "validuntil", "scheduledat", "deliverydate");
        return map;
    }

    /// <summary>Adds <paramref name="names"/> for one convention.</summary>
    private static void Add(Dictionary<string, (ConventionKind, Accepts)> map, ConventionKind kind, Accepts accepts, params string[] names)
    {
        foreach (var name in names)
            map[name] = (kind, accepts);
    }
}
