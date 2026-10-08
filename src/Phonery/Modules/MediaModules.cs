using Phonery.Data;

namespace Phonery.Modules;

/// <summary>Books (faker.js <c>book</c>).</summary>
public sealed class BookModule : FakerModule
{
    internal BookModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an author's name, e.g. <c>Jane Austen</c>.</summary>
    public string Author() => Faker.Pick(DataKeys.BookAuthor);

    /// <summary>Returns a book format, e.g. <c>Paperback</c>.</summary>
    public string Format() => Faker.Pick(DataKeys.BookFormat);

    /// <summary>Returns a genre, e.g. <c>Fantasy</c>.</summary>
    public string Genre() => Faker.Pick(DataKeys.BookGenre);

    /// <summary>Returns a publisher, e.g. <c>Penguin Books</c>.</summary>
    public string Publisher() => Faker.Pick(DataKeys.BookPublisher);

    /// <summary>Returns a book series, e.g. <c>Harry Potter</c>.</summary>
    public string Series() => Faker.Pick(DataKeys.BookSeries);

    /// <summary>Returns a book title, e.g. <c>Pride and Prejudice</c>.</summary>
    public string Title() => Faker.Pick(DataKeys.BookTitle);
}

/// <summary>Music (faker.js <c>music</c>).</summary>
public sealed class MusicModule : FakerModule
{
    internal MusicModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an album name.</summary>
    public string Album() => Faker.Pick(DataKeys.MusicAlbum);

    /// <summary>Returns an artist.</summary>
    public string Artist() => Faker.Pick(DataKeys.MusicArtist);

    /// <summary>Returns a music genre, e.g. <c>Jazz</c>.</summary>
    public string Genre() => Faker.Pick(DataKeys.MusicGenre);

    /// <summary>Returns a song name.</summary>
    public string SongName() => Faker.Pick(DataKeys.MusicSongName);
}
