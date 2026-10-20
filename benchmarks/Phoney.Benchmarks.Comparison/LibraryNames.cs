namespace Phoney.Benchmarks.Comparison;

/// <summary>The names of the compared libraries, as the benchmark descriptions, the cold start commands and the README matrix spell them.</summary>
internal static class LibraryNames
{
    public const string Phoney = "Phoney";
    public const string PhoneyReflection = "Phoney (reflection)";
    public const string PhoneyGenerated = "Phoney (source generated)";
    public const string Bogus = "Bogus";
    public const string AutoBogus = "AutoBogus";
    public const string AutoFixture = "AutoFixture";
    public const string NBuilder = "NBuilder";
    public const string NBuilderByHand = "NBuilder (composed by hand)";
    public const string FakerNet = "Faker.Net";
}
