using Phonery.Data;

namespace Phonery.Modules;

/// <summary>Animal breeds and species (faker.js <c>animal</c>).</summary>
public sealed class AnimalModule : FakerModule
{
    internal AnimalModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a dog breed.</summary>
    public string Dog() => Faker.Pick(DataKeys.AnimalDog);

    /// <summary>Returns a cat breed.</summary>
    public string Cat() => Faker.Pick(DataKeys.AnimalCat);

    /// <summary>Returns a snake species.</summary>
    public string Snake() => Faker.Pick(DataKeys.AnimalSnake);

    /// <summary>Returns a bear species.</summary>
    public string Bear() => Faker.Pick(DataKeys.AnimalBear);

    /// <summary>Returns a lion species.</summary>
    public string Lion() => Faker.Pick(DataKeys.AnimalLion);

    /// <summary>Returns a cetacean (whale, dolphin…) species.</summary>
    public string Cetacean() => Faker.Pick(DataKeys.AnimalCetacean);

    /// <summary>Returns a horse breed.</summary>
    public string Horse() => Faker.Pick(DataKeys.AnimalHorse);

    /// <summary>Returns a bird species.</summary>
    public string Bird() => Faker.Pick(DataKeys.AnimalBird);

    /// <summary>Returns a cow breed.</summary>
    public string Cow() => Faker.Pick(DataKeys.AnimalCow);

    /// <summary>Returns a fish species.</summary>
    public string Fish() => Faker.Pick(DataKeys.AnimalFish);

    /// <summary>Returns a crocodilian species.</summary>
    public string Crocodilia() => Faker.Pick(DataKeys.AnimalCrocodilia);

    /// <summary>Returns an insect species.</summary>
    public string Insect() => Faker.Pick(DataKeys.AnimalInsect);

    /// <summary>Returns a rabbit breed.</summary>
    public string Rabbit() => Faker.Pick(DataKeys.AnimalRabbit);

    /// <summary>Returns a rodent species.</summary>
    public string Rodent() => Faker.Pick(DataKeys.AnimalRodent);

    /// <summary>Returns a kind of animal, e.g. <c>crocodile</c>.</summary>
    public string Type() => Faker.Pick(DataKeys.AnimalType);

    /// <summary>Returns a pet name, e.g. <c>Buddy</c>.</summary>
    public string PetName() => Faker.Pick(DataKeys.AnimalPetName);
}
