using System.Globalization;

namespace Phony.Templates;

// Built-in template functions for the modules not registered in TemplateFunctions.cs.
public static partial class TemplateFunctions
{
    static partial void RegisterMoreBuiltIns(Dictionary<string, TemplateFunction> functions)
    {
        var f = functions;
        var inv = CultureInfo.InvariantCulture;
        void Add(string name, Func<Faker, string> call) => f[name] = (faker, _) => call(faker);

        // internet
        Add("internet.email", x => x.Internet.Email());
        Add("internet.exampleEmail", x => x.Internet.ExampleEmail());
        Add("internet.username", x => x.Internet.Username());
        Add("internet.userName", x => x.Internet.Username());
        Add("internet.displayName", x => x.Internet.DisplayName());
        Add("internet.protocol", x => x.Internet.Protocol());
        Add("internet.httpMethod", x => x.Internet.HttpMethod());
        Add("internet.httpStatusCode", x => x.Internet.HttpStatusCode().ToString(inv));
        Add("internet.url", x => x.Internet.Url());
        Add("internet.domainName", x => x.Internet.DomainName());
        Add("internet.domainSuffix", x => x.Internet.DomainSuffix());
        Add("internet.domainWord", x => x.Internet.DomainWord());
        Add("internet.ip", x => x.Internet.Ip());
        Add("internet.ipv4", x => x.Internet.Ipv4());
        Add("internet.ipv6", x => x.Internet.Ipv6());
        Add("internet.port", x => x.Internet.Port().ToString(inv));
        Add("internet.userAgent", x => x.Internet.UserAgent());
        f["internet.mac"] = (faker, a) => faker.Internet.Mac(a.String(0, "separator") ?? ":");
        f["internet.password"] = (faker, a) => faker.Internet.Password(a.Int(0, "length") ?? 15, a.Bool(0, "memorable") ?? false);
        Add("internet.emoji", x => x.Internet.Emoji());
        Add("internet.jwtAlgorithm", x => x.Internet.JwtAlgorithm());
        Add("internet.jwt", x => x.Internet.Jwt());

        // phone
        Add("phone.number", x => x.Phone.Number());
        Add("phone.imei", x => x.Phone.Imei());

        // company
        Add("company.name", x => x.Company.Name());
        Add("company.catchPhrase", x => x.Company.CatchPhrase());
        Add("company.buzzPhrase", x => x.Company.BuzzPhrase());
        Add("company.catchPhraseAdjective", x => x.Company.CatchPhraseAdjective());
        Add("company.catchPhraseDescriptor", x => x.Company.CatchPhraseDescriptor());
        Add("company.catchPhraseNoun", x => x.Company.CatchPhraseNoun());
        Add("company.buzzAdjective", x => x.Company.BuzzAdjective());
        Add("company.buzzVerb", x => x.Company.BuzzVerb());
        Add("company.buzzNoun", x => x.Company.BuzzNoun());

        // commerce
        Add("commerce.department", x => x.Commerce.Department());
        Add("commerce.productName", x => x.Commerce.ProductName());
        Add("commerce.productAdjective", x => x.Commerce.ProductAdjective());
        Add("commerce.productMaterial", x => x.Commerce.ProductMaterial());
        Add("commerce.product", x => x.Commerce.Product());
        Add("commerce.productDescription", x => x.Commerce.ProductDescription());
        Add("commerce.isbn", x => x.Commerce.Isbn());
        Add("commerce.upc", x => x.Commerce.Upc());
        f["commerce.price"] = (faker, a) => faker.Commerce.PriceText(
            a.String(0, "symbol") ?? "", (decimal)(a.Double(0, "min") ?? 1), (decimal)(a.Double(0, "max") ?? 1000), a.Int(0, "dec") ?? 2);

        // finance
        f["finance.accountNumber"] = (faker, a) => faker.Finance.AccountNumber(a.Int(0, "length") ?? 8);
        Add("finance.accountName", x => x.Finance.AccountName());
        Add("finance.routingNumber", x => x.Finance.RoutingNumber());
        f["finance.amount"] = (faker, a) => faker.Finance.AmountText(
            (decimal)(a.Double(0, "min") ?? 0), (decimal)(a.Double(0, "max") ?? 1000), a.Int(0, "dec") ?? 2, a.String(0, "symbol") ?? "");
        Add("finance.transactionType", x => x.Finance.TransactionType());
        Add("finance.transactionDescription", x => x.Finance.TransactionDescription());
        Add("finance.currencyCode", x => x.Finance.CurrencyCode());
        Add("finance.currencyName", x => x.Finance.CurrencyName());
        Add("finance.currencySymbol", x => x.Finance.CurrencySymbol());
        Add("finance.currencyNumericCode", x => x.Finance.CurrencyNumericCode());
        Add("finance.creditCardIssuer", x => x.Finance.CreditCardIssuer());
        f["finance.creditCardNumber"] = (faker, a) => faker.Finance.CreditCardNumber(a.String(0, "issuer"));
        Add("finance.creditCardCVV", x => x.Finance.CreditCardCvv());
        f["finance.pin"] = (faker, a) => faker.Finance.Pin(a.Int(0, "length") ?? 4);
        Add("finance.ethereumAddress", x => x.Finance.EthereumAddress());
        Add("finance.bitcoinAddress", x => x.Finance.BitcoinAddress());
        Add("finance.litecoinAddress", x => x.Finance.LitecoinAddress());
        Add("finance.iban", x => x.Finance.Iban());
        Add("finance.bic", x => x.Finance.Bic());

        // date
        Add("date.anytime", x => x.Date.Anytime().ToString("O", inv));
        f["date.past"] = (faker, a) => faker.Date.Past(a.Int(0, "years") ?? 1).ToString("O", inv);
        f["date.future"] = (faker, a) => faker.Date.Future(a.Int(0, "years") ?? 1).ToString("O", inv);
        f["date.recent"] = (faker, a) => faker.Date.Recent(a.Int(0, "days") ?? 1).ToString("O", inv);
        f["date.soon"] = (faker, a) => faker.Date.Soon(a.Int(0, "days") ?? 1).ToString("O", inv);
        Add("date.birthdate", x => x.Date.Birthdate().ToString("O", inv));
        f["date.month"] = (faker, a) => faker.Date.Month(a.Bool(0, "abbreviated") ?? false, a.Bool(0, "context") ?? false);
        f["date.weekday"] = (faker, a) => faker.Date.Weekday(a.Bool(0, "abbreviated") ?? false, a.Bool(0, "context") ?? false);
        Add("date.timeZone", x => x.Date.TimeZone());

        // lorem
        f["lorem.word"] = (faker, a) => faker.Lorem.Word(a.Int(0, "length"), a.Int(0, "length"));
        f["lorem.words"] = (faker, a) => faker.Lorem.Words(a.Int(0) ?? 3);
        f["lorem.sentence"] = (faker, a) => faker.Lorem.Sentence(a.Int(0));
        f["lorem.sentences"] = (faker, a) => faker.Lorem.Sentences(a.Int(0));
        f["lorem.paragraph"] = (faker, a) => faker.Lorem.Paragraph(a.Int(0) ?? 3);
        f["lorem.paragraphs"] = (faker, a) => faker.Lorem.Paragraphs(a.Int(0) ?? 3);
        f["lorem.lines"] = (faker, a) => faker.Lorem.Lines(a.Int(0));
        f["lorem.slug"] = (faker, a) => faker.Lorem.Slug(a.Int(0) ?? 3);
        Add("lorem.text", x => x.Lorem.Text());

        // word
        f["word.adjective"] = (faker, a) => faker.Word.Adjective(a.Int(0, "length"), a.Int(0, "length"));
        f["word.adverb"] = (faker, a) => faker.Word.Adverb(a.Int(0, "length"), a.Int(0, "length"));
        f["word.conjunction"] = (faker, a) => faker.Word.Conjunction(a.Int(0, "length"), a.Int(0, "length"));
        f["word.interjection"] = (faker, a) => faker.Word.Interjection(a.Int(0, "length"), a.Int(0, "length"));
        f["word.noun"] = (faker, a) => faker.Word.Noun(a.Int(0, "length"), a.Int(0, "length"));
        f["word.preposition"] = (faker, a) => faker.Word.Preposition(a.Int(0, "length"), a.Int(0, "length"));
        f["word.verb"] = (faker, a) => faker.Word.Verb(a.Int(0, "length"), a.Int(0, "length"));
        f["word.sample"] = (faker, a) => faker.Word.Sample(a.Int(0, "length"), a.Int(0, "length"));
        f["word.words"] = (faker, a) => faker.Word.Words(a.Int(0, "count"));

        // color
        Add("color.human", x => x.Color.Human());
        Add("color.space", x => x.Color.Space());
        Add("color.rgb", x => x.Color.Rgb());

        // animal
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

        // book, music
        Add("book.author", x => x.Book.Author());
        Add("book.format", x => x.Book.Format());
        Add("book.genre", x => x.Book.Genre());
        Add("book.publisher", x => x.Book.Publisher());
        Add("book.series", x => x.Book.Series());
        Add("book.title", x => x.Book.Title());
        Add("music.album", x => x.Music.Album());
        Add("music.artist", x => x.Music.Artist());
        Add("music.genre", x => x.Music.Genre());
        Add("music.songName", x => x.Music.SongName());

        // food, hacker
        Add("food.adjective", x => x.Food.Adjective());
        Add("food.description", x => x.Food.Description());
        Add("food.dish", x => x.Food.Dish());
        Add("food.ethnicCategory", x => x.Food.EthnicCategory());
        Add("food.fruit", x => x.Food.Fruit());
        Add("food.ingredient", x => x.Food.Ingredient());
        Add("food.meat", x => x.Food.Meat());
        Add("food.spice", x => x.Food.Spice());
        Add("food.vegetable", x => x.Food.Vegetable());
        Add("hacker.abbreviation", x => x.Hacker.Abbreviation());
        Add("hacker.adjective", x => x.Hacker.Adjective());
        Add("hacker.noun", x => x.Hacker.Noun());
        Add("hacker.verb", x => x.Hacker.Verb());
        Add("hacker.ingverb", x => x.Hacker.IngVerb());
        Add("hacker.phrase", x => x.Hacker.Phrase());

        // vehicle, airline, science, database
        Add("vehicle.vehicle", x => x.Vehicle.Vehicle());
        Add("vehicle.manufacturer", x => x.Vehicle.Manufacturer());
        Add("vehicle.model", x => x.Vehicle.Model());
        Add("vehicle.type", x => x.Vehicle.Type());
        Add("vehicle.fuel", x => x.Vehicle.Fuel());
        Add("vehicle.vin", x => x.Vehicle.Vin());
        Add("vehicle.color", x => x.Vehicle.Color());
        Add("vehicle.vrm", x => x.Vehicle.Vrm());
        Add("vehicle.bicycle", x => x.Vehicle.Bicycle());
        Add("airline.recordLocator", x => x.Airline.RecordLocator());
        Add("airline.seat", x => x.Airline.Seat());
        Add("airline.flightNumber", x => x.Airline.FlightNumber());
        Add("database.column", x => x.Database.Column());
        Add("database.type", x => x.Database.Type());
        Add("database.collation", x => x.Database.Collation());
        Add("database.engine", x => x.Database.Engine());
        Add("database.mongodbObjectId", x => x.Database.MongoDbObjectId());

        // system, git, image
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
        Add("git.branch", x => x.Git.Branch());
        Add("git.commitMessage", x => x.Git.CommitMessage());
        Add("git.commitDate", x => x.Git.CommitDate());
        f["git.commitSha"] = (faker, a) => faker.Git.CommitSha(a.Int(0, "length") ?? 40);
        Add("image.avatar", x => x.Image.Avatar());
        Add("image.avatarGitHub", x => x.Image.AvatarGitHub());
        Add("image.personPortrait", x => x.Image.PersonPortrait());
        Add("image.url", x => x.Image.Url());
        Add("image.urlPicsumPhotos", x => x.Image.UrlPicsumPhotos());
        Add("image.dataUri", x => x.Image.DataUri());
    }
}
