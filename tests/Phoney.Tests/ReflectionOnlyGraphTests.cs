using System.Text;
using Phoney.Generation;

namespace Phoney.Tests;

// Reflection-only types: no [FakeFor] reaches them, so nested objects go through the untyped reflection model.
public sealed class Playlist
{
    public string Title { get; set; } = "";
    public List<Track> Tracks { get; set; } = [];
}

public sealed class Track
{
    public string Name { get; set; } = "";
    public Playlist? Playlist { get; set; }
}

public sealed class Crate
{
    public string Label { get; set; } = "";
    public List<string> Records { get; } = [];
}

public sealed class Depot
{
    public string Name { get; set; } = "";
    public Crate Main { get; set; } = null!;
}

public enum PatientGender
{
    Unknown,
    Female,
    Male,
}

public sealed class Clinician
{
    public string FirstName { get; set; } = "";
    public PatientGender Category { get; set; }
}

public sealed class Seat
{
    public Seat(int row, int number)
    {
        Row = row;
        Number = number;
    }

    public int Row { get; }
    public int Number { get; }
    public string Holder { get; set; } = "";
}

public sealed class Notebook
{
    public string Title { get; set; } = "";
    public StringBuilder? Draft { get; set; }
}

[FakeFor<Seat>]
[FakeFor<Notebook>]
public static partial class ReflectionOnlyGraphFakes;

/// <summary>Graph features that reach objects through the untyped reflection model, and the generated model's twins of them.</summary>
public sealed class ReflectionOnlyGraphTests
{
    [Test]
    public void Back_references_reach_nested_objects_that_only_have_a_reflection_model()
    {
        var playlists = Fake.For<Playlist>().Seed(3).WithBackReferences().Generate(5);

        playlists.SelectMany(p => p.Tracks).ShouldNotBeEmpty();
        foreach (var playlist in playlists)
            playlist.Tracks.ShouldAllBe(t => ReferenceEquals(t.Playlist, playlist));
    }

    [Test]
    public void Populate_fills_the_read_only_collection_of_an_existing_nested_object()
    {
        var crate = new Crate { Label = "kept" };

        var warehouse = Fake.For<Depot>().Seed(2).Populate(new Depot { Main = crate });

        warehouse.Main.ShouldBeSameAs(crate);
        crate.Label.ShouldBe("kept");
        crate.Records.ShouldNotBeEmpty();
        warehouse.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void A_kept_enum_named_like_a_sex_decides_the_sex_of_the_generated_name()
    {
        var female = ((Phoney.Data.StringsEntry)Locales.Data("sv").Get("person.first_name.female")!).Values;
        var male = ((Phoney.Data.StringsEntry)Locales.Data("sv").Get("person.first_name.male")!).Values;
        var generator = Fake.For<Clinician>().Locale("sv").Seed(4);

        for (var i = 0; i < 30; i++)
        {
            female.ShouldContain(generator.Seed(i).Populate(new Clinician { Category = PatientGender.Female }).FirstName);
            male.ShouldContain(generator.Seed(i).Populate(new Clinician { Category = PatientGender.Male }).FirstName);
        }
    }

    [Test]
    public void A_unique_combination_of_constructor_parameters_is_unique_in_both_models()
    {
        static ObjectGenerator<Seat> Seats(ObjectGenerator<Seat> generator) => generator
            .With(s => s.Row, f => f.Number.Int(1, 6))
            .With(s => s.Number, f => f.Number.Int(1, 10))
            .Unique(s => new { s.Row, s.Number })
            .Seed(8);

        var generated = Seats(ReflectionOnlyGraphFakes.Seat).Generate(60);
        var reflected = Seats(new ObjectGenerator<Seat>(new ReflectionModel<Seat>())).Generate(60);

        generated.Select(s => (s.Row, s.Number)).Distinct().Count().ShouldBe(60);
        generated.Take(3).Select(s => (s.Row, s.Number, s.Holder)).ShouldBe([(4, 5, "textilis"), (3, 1, "eaque"), (5, 4, "cavus")]); // what seed 8 has always given
        reflected.Select(s => (s.Row, s.Number, s.Holder)).ShouldBe(generated.Select(s => (s.Row, s.Number, s.Holder)));
    }

    [Test]
    public void A_framework_class_without_a_producer_is_null_in_both_models()
    {
        var generated = ReflectionOnlyGraphFakes.Notebook.Seed(1).Generate(5);
        var reflected = new ObjectGenerator<Notebook>(new ReflectionModel<Notebook>()).Seed(1).Generate(5);

        generated.ShouldAllBe(n => n.Draft == null && n.Title.Length > 0);
        reflected.Select(n => (n.Title, n.Draft)).ShouldBe(generated.Select(n => (n.Title, n.Draft)));
    }

    [Test]
    public void A_model_names_its_type_and_refuses_to_fill_a_member_it_does_not_fill()
    {
        var model = ModelRegistry.Generated<Notebook>();

        model.Type.ShouldBe(typeof(Notebook));
        Should.Throw<InvalidOperationException>(() => model.Fill(new Notebook(), 0, new List<string>(), replace: false))
            .Message.ShouldContain("Title is not a collection that is filled in place");
    }
}
