using System.Globalization;
using System.Text;

namespace Phoney.Modules;

/// <summary>Image URLs and data URIs (faker.js <c>image</c>).</summary>
public sealed class ImageModule : FakerModule
{
    private const string PortraitBaseUrl = "https://cdn.jsdelivr.net/gh/faker-js/assets-person-portrait";

    internal ImageModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns an avatar image URL (a portrait or a GitHub avatar).</summary>
    public string Avatar() => Random.Bool() ? PersonPortrait() : AvatarGitHub();

    /// <summary>Returns a GitHub avatar URL.</summary>
    public string AvatarGitHub() => $"https://avatars.githubusercontent.com/u/{Random.Int(0, 100_000_000).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Returns the URL of an AI-generated portrait (from the faker.js asset repository).</summary>
    /// <param name="sex">Sex of the person shown; random when not given.</param>
    /// <param name="size">Image size in pixels: 32, 64, 128, 256 or 512.</param>
    public string PersonPortrait(Sex? sex = null, int size = 512)
    {
        if (size is not (32 or 64 or 128 or 256 or 512))
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be 32, 64, 128, 256 or 512.");
        var s = (sex ?? Faker.Person.SexType()) == Sex.Female ? "female" : "male";
        return $"{PortraitBaseUrl}/{s}/{size.ToString(CultureInfo.InvariantCulture)}/{Random.Int(0, 99).ToString(CultureInfo.InvariantCulture)}.jpg";
    }

    /// <summary>Returns a URL of a random photo of the given size (random sizes up to 3999 when not given).</summary>
    public string Url(int? width = null, int? height = null) => UrlPicsumPhotos(width, height, grayscale: false, blur: 0);

    /// <summary>Returns a picsum.photos URL.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="grayscale">Whether the image is grayscale; random when not given.</param>
    /// <param name="blur">Blur level 0 (none) to 10; random when not given.</param>
    public string UrlPicsumPhotos(int? width = null, int? height = null, bool? grayscale = null, int? blur = null)
    {
        var w = width ?? Random.Int(1, 3999);
        var h = height ?? Random.Int(1, 3999);
        var gray = grayscale ?? Random.Bool();
        var b = blur ?? Random.Int(0, 10);
        var sb = new StringBuilder("https://picsum.photos/seed/")
            .Append(Faker.String.AlphaNumeric(Random.Int(5, 10)))
            .Append('/').Append(w.ToString(CultureInfo.InvariantCulture))
            .Append('/').Append(h.ToString(CultureInfo.InvariantCulture));
        var hasBlur = b is >= 1 and <= 10;
        if (gray || hasBlur)
        {
            sb.Append('?');
            if (gray)
                sb.Append("grayscale");
            if (gray && hasBlur)
                sb.Append('&');
            if (hasBlur)
                sb.Append("blur=").Append(b.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Returns an SVG placeholder image as a data URI, showing its size on a solid color.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="color">Fill color; random when not given.</param>
    /// <param name="base64">Base64-encode the SVG instead of URL-encoding it; random when not given.</param>
    public string DataUri(int? width = null, int? height = null, string? color = null, bool? base64 = null)
    {
        var w = width ?? Random.Int(1, 3999);
        var h = height ?? Random.Int(1, 3999);
        color ??= Faker.Color.Rgb();
        var useBase64 = base64 ?? Random.Bool();
        var inv = CultureInfo.InvariantCulture;
        var svg = string.Create(inv,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" baseProfile=\"full\" width=\"{w}\" height=\"{h}\"><rect width=\"100%\" height=\"100%\" fill=\"{color}\"/><text x=\"{w / 2.0}\" y=\"{h / 2.0}\" font-size=\"20\" alignment-baseline=\"middle\" text-anchor=\"middle\" fill=\"white\">{w}x{h}</text></svg>");
        return useBase64
            ? "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))
            : "data:image/svg+xml;charset=UTF-8," + Uri.EscapeDataString(svg);
    }
}
