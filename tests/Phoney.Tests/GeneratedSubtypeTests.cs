using System.Text.Json.Serialization;
using Phoney.Generation;

namespace Phoney.Tests;

public interface IExhibit;

public sealed class Painting : IExhibit
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
}

public sealed record Sculpture(string Material, double Weight) : IExhibit;

[JsonDerivedType(typeof(Guide))]
[JsonDerivedType(typeof(Curator))]
public abstract class Staff
{
    public string FirstName { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class Guide : Staff
{
    public string Language { get; set; } = "";
}

public sealed class Curator : Staff
{
    public int YearsOfExperience { get; set; }
}

public interface IDecoration;

public sealed class Plant : IDecoration
{
    public string Name { get; set; } = "";
}

public sealed class Gallery
{
    public string Name { get; set; } = "";
    public IExhibit Highlight { get; set; } = null!;
    public List<IExhibit> Exhibits { get; set; } = [];
    public Staff? Host { get; set; }
    public IDecoration Decoration { get; set; } = null!;
}

[FakeFor<Gallery>, FakeFor<Painting>, FakeFor<Sculpture>]
[FakeSubtype<IDecoration, Plant>]
public static partial class GalleryFakes;

/// <summary>Subtypes through source-generated models, and their parity with the reflection model.</summary>
public sealed class GeneratedSubtypeTests
{
    private static readonly DateTimeOffset Reference = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void JsonDerivedType_and_FakeSubtype_types_get_generated_models()
    {
        ModelRegistry.IsRegistered<Guide>().ShouldBeTrue();
        ModelRegistry.IsRegistered<Curator>().ShouldBeTrue();
        ModelRegistry.IsRegistered<Plant>().ShouldBeTrue();
        ModelRegistry.For<Plant>().GetType().Name.ShouldStartWith("__PhoneyModel_");
    }

    [Test]
    public void Generated_models_create_subtypes()
    {
        var galleries = GalleryFakes.Gallery.Seed(1).Subtype<IExhibit, Painting, Sculpture>().Generate(30);

        galleries.ShouldAllBe(g => g.Decoration is Plant); // [FakeSubtype] registers app-wide
        galleries.ShouldAllBe(g => g.Host is Guide || g.Host is Curator);
        galleries.Select(g => g.Highlight).OfType<Painting>().ShouldNotBeEmpty();
        galleries.Select(g => g.Highlight).OfType<Sculpture>().ShouldNotBeEmpty();
        galleries.SelectMany(g => g.Exhibits).ShouldAllBe(e => e is Painting || e is Sculpture);
    }

    [Test]
    public void Generated_and_reflection_models_produce_identical_subtypes()
    {
        var generated = GalleryFakes.Gallery.Subtype<IExhibit, Painting, Sculpture>().Seed(7).ReferenceDate(Reference).Generate(25);
        var reflected = new ObjectGenerator<Gallery>(new ReflectionModel<Gallery>()).Subtype<IExhibit, Painting, Sculpture>().Seed(7).ReferenceDate(Reference).Generate(25);

        reflected.Select(Describe).ShouldBe(generated.Select(Describe));

        // Without subtypes for IExhibit the members stay null, in both models alike.
        var plainGenerated = GalleryFakes.Gallery.Seed(8).Generate(10);
        var plainReflected = new ObjectGenerator<Gallery>(new ReflectionModel<Gallery>()).Seed(8).Generate(10);
        plainGenerated.ShouldAllBe(g => g.Highlight == null);
        plainReflected.Select(Describe).ShouldBe(plainGenerated.Select(Describe));
    }

    [Test]
    public void Generated_and_reflection_models_populate_subtypes_identically()
    {
        Gallery Existing() => new() { Highlight = new Painting { Title = "Kept" }, Host = new Curator() };

        var generated = GalleryFakes.Gallery.Seed(5).Populate(Existing());
        var reflected = new ObjectGenerator<Gallery>(new ReflectionModel<Gallery>()).Seed(5).Populate(Existing());

        generated.Highlight.ShouldBeOfType<Painting>().Title.ShouldBe("Kept");
        ((Painting)generated.Highlight).Price.ShouldNotBe(0);
        generated.Host.ShouldBeOfType<Curator>().Email.ShouldContain("@");
        Describe(reflected).ShouldBe(Describe(generated));
    }

    private static string Describe(Gallery g) =>
        $"{g.Name}|{Exhibit(g.Highlight)}|{string.Join(",", g.Exhibits.Select(Exhibit))}|{Host(g.Host)}|{(g.Decoration as Plant)?.Name}";

    private static string Exhibit(IExhibit? e) => e switch
    {
        Painting p => $"P:{p.Title}:{p.Price}",
        Sculpture s => $"S:{s.Material}:{s.Weight}",
        null => "null",
        _ => "?",
    };

    private static string Host(Staff? s) => s switch
    {
        Guide g => $"G:{g.FirstName}:{g.Email}:{g.Language}",
        Curator c => $"C:{c.FirstName}:{c.Email}:{c.YearsOfExperience}",
        _ => "null",
    };
}
