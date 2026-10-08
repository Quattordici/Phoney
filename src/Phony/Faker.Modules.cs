using Phony.Modules;

namespace Phony;

// Module accessors. Modules are created on first use so a Faker that only needs names never allocates the rest.
public sealed partial class Faker
{
    private HelpersModule? _helpers;
    private NumberModule? _number;
    private StringModule? _string;
    private PersonModule? _person;
    private LocationModule? _location;
    private InternetModule? _internet;
    private PhoneModule? _phone;
    private CompanyModule? _company;
    private CommerceModule? _commerce;
    private FinanceModule? _finance;
    private DateModule? _date;
    private LoremModule? _lorem;
    private WordModule? _word;
    private ColorModule? _color;
    private AnimalModule? _animal;
    private BookModule? _book;
    private MusicModule? _music;
    private FoodModule? _food;
    private HackerModule? _hacker;
    private VehicleModule? _vehicle;
    private AirlineModule? _airline;
    private ScienceModule? _science;
    private DatabaseModule? _database;
    private SystemModule? _system;
    private GitModule? _git;
    private ImageModule? _image;

    /// <summary>Patterns, symbols, checksums and collection helpers.</summary>
    public HelpersModule Helpers => _helpers ??= new(this);

    /// <summary>Random numbers.</summary>
    public NumberModule Number => _number ??= new(this);

    /// <summary>Random strings and identifiers (UUID, ULID, nanoid…).</summary>
    public StringModule String => _string ??= new(this);

    /// <summary>Names, job titles and other personal details.</summary>
    public PersonModule Person => _person ??= new(this);

    /// <summary>Addresses, places and coordinates.</summary>
    public LocationModule Location => _location ??= new(this);

    /// <summary>Emails, usernames, URLs, IP addresses and passwords.</summary>
    public InternetModule Internet => _internet ??= new(this);

    /// <summary>Phone numbers and IMEIs.</summary>
    public PhoneModule Phone => _phone ??= new(this);

    /// <summary>Company names and corporate jargon.</summary>
    public CompanyModule Company => _company ??= new(this);

    /// <summary>Products, prices, ISBNs and UPCs.</summary>
    public CommerceModule Commerce => _commerce ??= new(this);

    /// <summary>Accounts, amounts, currencies, credit cards, IBAN/BIC and crypto addresses.</summary>
    public FinanceModule Finance => _finance ??= new(this);

    /// <summary>Past, future, recent and birth dates relative to ReferenceDate.</summary>
    public DateModule Date => _date ??= new(this);

    /// <summary>Placeholder text.</summary>
    public LoremModule Lorem => _lorem ??= new(this);

    /// <summary>Real words of the locale's language by part of speech.</summary>
    public WordModule Word => _word ??= new(this);

    /// <summary>Color names and color values.</summary>
    public ColorModule Color => _color ??= new(this);

    /// <summary>Animal breeds and species.</summary>
    public AnimalModule Animal => _animal ??= new(this);

    /// <summary>Book titles, authors and publishers.</summary>
    public BookModule Book => _book ??= new(this);

    /// <summary>Albums, artists, genres and songs.</summary>
    public MusicModule Music => _music ??= new(this);

    /// <summary>Dishes and ingredients.</summary>
    public FoodModule Food => _food ??= new(this);

    /// <summary>Tech jargon.</summary>
    public HackerModule Hacker => _hacker ??= new(this);

    /// <summary>Vehicles, VINs and registration marks.</summary>
    public VehicleModule Vehicle => _vehicle ??= new(this);

    /// <summary>Airlines, airports, flights and seats.</summary>
    public AirlineModule Airline => _airline ??= new(this);

    /// <summary>Chemical elements and units.</summary>
    public ScienceModule Science => _science ??= new(this);

    /// <summary>Database columns, types and ids.</summary>
    public DatabaseModule Database => _database ??= new(this);

    /// <summary>File names, MIME types, paths, versions and cron expressions.</summary>
    public SystemModule System => _system ??= new(this);

    /// <summary>Git branches, commits and SHAs.</summary>
    public GitModule Git => _git ??= new(this);

    /// <summary>Image URLs and data URIs.</summary>
    public ImageModule Image => _image ??= new(this);
}
