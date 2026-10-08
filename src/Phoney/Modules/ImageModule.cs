using System.Globalization;
using System.Text;

namespace Phoney.Modules;

/// <summary>Image URLs and self-contained placeholder images.</summary>
/// <remarks>URLs are built from the templates in <see cref="ImageSources"/>, so they can point at any host.</remarks>
public sealed class ImageModule : FakerModule
{
    private static readonly int[] AvatarSizes = [32, 64, 128, 256, 512];

    internal ImageModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns the URL of a photo, built from <see cref="ImageSources.Photo"/>.</summary>
    /// <param name="width">Width in pixels; 100–2000 when not given.</param>
    /// <param name="height">Height in pixels; 100–2000 when not given.</param>
    public string Url(int? width = null, int? height = null) => ImageSources.Format(
        ImageSources.Photo,
        ("seed", Faker.String.AlphaNumeric(Random.Int(5, 10))),
        ("width", (width ?? Random.Int(100, 2000)).ToString(CultureInfo.InvariantCulture)),
        ("height", (height ?? Random.Int(100, 2000)).ToString(CultureInfo.InvariantCulture)));

    /// <summary>Returns the URL of a portrait, built from <see cref="ImageSources.Avatar"/>.</summary>
    /// <param name="sex">Sex of the person shown; random when not given (pass it to match a generated person).</param>
    /// <param name="size">Size in pixels: 32, 64, 128, 256 or 512.</param>
    public string Avatar(Sex? sex = null, int size = 512)
    {
        if (Array.IndexOf(AvatarSizes, size) < 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be 32, 64, 128, 256 or 512.");
        return ImageSources.Format(
            ImageSources.Avatar,
            ("sex", (sex ?? Faker.Person.Sex()) == Sex.Female ? "female" : "male"),
            ("size", size.ToString(CultureInfo.InvariantCulture)),
            ("index", Random.Int(0, 99).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Returns an SVG placeholder image as a <c>data:</c> URI (a solid color showing its size). Needs no network, so it
    /// suits tests and offline demos.
    /// </summary>
    /// <param name="width">Width in pixels; 100–2000 when not given.</param>
    /// <param name="height">Height in pixels; 100–2000 when not given.</param>
    /// <param name="color">Fill color; random when not given.</param>
    /// <param name="base64">Base64-encode the SVG instead of URL-encoding it.</param>
    public string DataUri(int? width = null, int? height = null, string? color = null, bool base64 = false)
    {
        var w = width ?? Random.Int(100, 2000);
        var h = height ?? Random.Int(100, 2000);
        color ??= Faker.Color.Hex();
        var svg = string.Create(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" baseProfile=\"full\" width=\"{w}\" height=\"{h}\"><rect width=\"100%\" height=\"100%\" fill=\"{color}\"/><text x=\"{w / 2.0}\" y=\"{h / 2.0}\" font-size=\"20\" alignment-baseline=\"middle\" text-anchor=\"middle\" fill=\"white\">{w}x{h}</text></svg>");
        return base64
            ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))
            : "data:image/svg+xml;charset=UTF-8," + Uri.EscapeDataString(svg);
    }
}

/// <summary>
/// URL templates for generated image URLs. The defaults use public placeholder services (picsum.photos and the
/// faker.js portrait set); point them at your own host to keep generated data independent of third parties.
/// </summary>
/// <example>
/// <code>
/// ImageSources.UseHost("https://images.test");   // https://images.test/photos/…, https://images.test/avatars/…
/// ImageSources.Photo = "https://cdn.example.com/img/{width}x{height}?s={seed}";
/// </code>
/// </example>
public static class ImageSources
{
    /// <summary>Default for <see cref="Photo"/>.</summary>
    public const string DefaultPhoto = "https://picsum.photos/seed/{seed}/{width}/{height}";

    /// <summary>Default for <see cref="Avatar"/>.</summary>
    public const string DefaultAvatar = "https://cdn.jsdelivr.net/gh/faker-js/assets-person-portrait/{sex}/{size}/{index}.jpg";

    /// <summary>Template for <see cref="ImageModule.Url"/>; placeholders <c>{seed}</c>, <c>{width}</c> and <c>{height}</c>.</summary>
    public static string Photo { get; set; } = DefaultPhoto;

    /// <summary>Template for <see cref="ImageModule.Avatar"/>; placeholders <c>{sex}</c> (<c>female</c>/<c>male</c>), <c>{size}</c> and <c>{index}</c> (0–99).</summary>
    public static string Avatar { get; set; } = DefaultAvatar;

    /// <summary>Points both templates at <paramref name="baseUrl"/>: <c>{baseUrl}/photos/{seed}/{width}x{height}</c> and <c>{baseUrl}/avatars/{sex}/{size}/{index}.jpg</c>.</summary>
    public static void UseHost(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var root = baseUrl.TrimEnd('/');
        Photo = root + "/photos/{seed}/{width}x{height}";
        Avatar = root + "/avatars/{sex}/{size}/{index}.jpg";
    }

    /// <summary>Restores the default templates.</summary>
    public static void Reset()
    {
        Photo = DefaultPhoto;
        Avatar = DefaultAvatar;
    }

    /// <summary>Substitutes <c>{name}</c> placeholders in <paramref name="template"/>.</summary>
    internal static string Format(string template, params (string Name, string Value)[] values)
    {
        var sb = new StringBuilder(template);
        foreach (var (name, value) in values)
            sb.Replace("{" + name + "}", value);
        return sb.ToString();
    }
}
