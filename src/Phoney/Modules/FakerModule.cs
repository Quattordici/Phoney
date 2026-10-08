namespace Phoney.Modules;

/// <summary>Base class of the modules exposed by <see cref="Phoney.Faker"/> (<c>faker.Person</c>, <c>faker.Internet</c>, …).</summary>
public abstract class FakerModule
{
    /// <summary>Modules are created by <see cref="Phoney.Faker"/>.</summary>
    private protected FakerModule(Faker faker) => Faker = faker;

    /// <summary>The faker this module belongs to; gives access to locale data and other modules.</summary>
    private protected Faker Faker { get; }

    /// <summary>Shortcut for <c>Faker.Random</c>.</summary>
    private protected Randomizer Random => Faker.Random;
}

/// <summary>Biological sex used to pick gendered names and prefixes.</summary>
public enum Sex
{
    /// <summary>Female.</summary>
    Female,

    /// <summary>Male.</summary>
    Male,
}

/// <summary>Letter casing for generated strings.</summary>
public enum Casing
{
    /// <summary>Upper- and lower-case letters.</summary>
    Mixed,

    /// <summary>Upper-case letters only.</summary>
    Upper,

    /// <summary>Lower-case letters only.</summary>
    Lower,
}
