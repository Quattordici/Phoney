using System.Globalization;
using Phony.Data;

namespace Phony.Modules;

/// <summary>
/// Colors (faker.js <c>color</c>). Numeric color models return typed values with a <c>ToCss()</c> method
/// instead of faker.js' format options.
/// </summary>
public sealed class ColorModule : FakerModule
{
    internal ColorModule(Faker faker) : base(faker)
    {
    }

    /// <summary>Returns a human-readable color name in the locale's language, e.g. <c>red</c>.</summary>
    public string Human() => Faker.Pick(DataKeys.ColorHuman);

    /// <summary>Returns a color space name, e.g. <c>sRGB</c>.</summary>
    public string Space() => Faker.Pick(DataKeys.ColorSpace);

    /// <summary>Returns a CSS color function.</summary>
    public CssFunction CssSupportedFunction() => Random.Enum<CssFunction>();

    /// <summary>Returns a CSS color space.</summary>
    public CssSpace CssSupportedSpace() => Random.Enum<CssSpace>();

    /// <summary>Returns a hex color such as <c>#ffa500</c>.</summary>
    /// <param name="prefix">Text before the hex digits.</param>
    /// <param name="casing">Casing of the hex digits.</param>
    /// <param name="includeAlpha">Append two alpha digits.</param>
    public string Rgb(string prefix = "#", Casing casing = Casing.Lower, bool includeAlpha = false) =>
        Faker.String.Hexadecimal(includeAlpha ? 8 : 6, casing, prefix);

    /// <summary>Returns RGB channel values, optionally with an alpha channel (0–1).</summary>
    public RgbColor RgbValues(bool includeAlpha = false) =>
        new((byte)Random.Int(0, 255), (byte)Random.Int(0, 255), (byte)Random.Int(0, 255), includeAlpha ? Fraction(0.01) : null);

    /// <summary>Returns a CMYK color with components between 0 and 1.</summary>
    public CmykColor Cmyk() => new(Fraction(0.01), Fraction(0.01), Fraction(0.01), Fraction(0.01));

    /// <summary>Returns an HSL color (hue in degrees, saturation and lightness 0–1), optionally with alpha.</summary>
    public HslColor Hsl(bool includeAlpha = false) =>
        new(Random.Int(0, 360), Fraction(0.01), Fraction(0.01), includeAlpha ? Fraction(0.01) : null);

    /// <summary>Returns an HWB color (hue in degrees, whiteness and blackness 0–1).</summary>
    public HwbColor Hwb() => new(Random.Int(0, 360), Fraction(0.01), Fraction(0.01));

    /// <summary>Returns a CIE LAB color (lightness 0–1, a and b −100..100).</summary>
    public LabColor Lab() => new(Fraction(0.000001), Step(-100, 100, 0.0001), Step(-100, 100, 0.0001));

    /// <summary>Returns a CIE LCH color (lightness 0–1, chroma 0–230, hue 0–360).</summary>
    public LchColor Lch() => new(Fraction(0.000001), Step(0, 230, 0.1), Step(0, 360, 0.1));

    /// <summary>Returns a CSS <c>color()</c> value in <paramref name="space"/>, e.g. <c>color(display-p3 0.12 1 0.23)</c>.</summary>
    public string ColorByCssColorSpace(CssSpace space = CssSpace.Srgb)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Create(inv, $"color({CssSpaceName(space)} {Fraction(0.0001)} {Fraction(0.0001)} {Fraction(0.0001)})");
    }

    /// <summary>CSS spelling of a color space.</summary>
    internal static string CssSpaceName(CssSpace space) => space switch
    {
        CssSpace.DisplayP3 => "display-p3",
        CssSpace.Rec2020 => "rec2020",
        CssSpace.A98Rgb => "a98-rgb",
        CssSpace.ProphotoRgb => "prophoto-rgb",
        _ => "sRGB",
    };

    /// <summary>A value in [0, 1] that is a multiple of <paramref name="step"/> (faker.js <c>number.float({ multipleOf })</c>).</summary>
    private double Fraction(double step) => Step(0, 1, step);

    /// <summary>A value between <paramref name="min"/> and <paramref name="max"/> that is a multiple of <paramref name="step"/>.</summary>
    private double Step(double min, double max, double step)
    {
        var factor = 1 / step;
        var steps = Random.Long((long)Math.Ceiling(min * factor), (long)Math.Floor(max * factor));
        return Math.Round(steps / factor, (int)Math.Ceiling(-Math.Log10(step)));
    }
}

/// <summary>CSS color functions.</summary>
public enum CssFunction
{
    /// <summary><c>rgb()</c></summary>
    Rgb,

    /// <summary><c>rgba()</c></summary>
    Rgba,

    /// <summary><c>hsl()</c></summary>
    Hsl,

    /// <summary><c>hsla()</c></summary>
    Hsla,

    /// <summary><c>hwb()</c></summary>
    Hwb,

    /// <summary><c>cmyk()</c></summary>
    Cmyk,

    /// <summary><c>lab()</c></summary>
    Lab,

    /// <summary><c>lch()</c></summary>
    Lch,

    /// <summary><c>color()</c></summary>
    Color,
}

/// <summary>CSS color spaces usable with <c>color()</c>.</summary>
public enum CssSpace
{
    /// <summary>sRGB.</summary>
    Srgb,

    /// <summary>Display P3.</summary>
    DisplayP3,

    /// <summary>Rec. 2020.</summary>
    Rec2020,

    /// <summary>Adobe RGB (1998).</summary>
    A98Rgb,

    /// <summary>ProPhoto RGB.</summary>
    ProphotoRgb,
}

/// <summary>An RGB color with optional alpha (0–1).</summary>
public readonly record struct RgbColor(byte R, byte G, byte B, double? Alpha = null)
{
    /// <summary>Formats as <c>rgb(r, g, b)</c>, or <c>rgba(r, g, b, a)</c> with alpha.</summary>
    public string ToCss() => Alpha is { } a
        ? string.Create(CultureInfo.InvariantCulture, $"rgba({R}, {G}, {B}, {a})")
        : string.Create(CultureInfo.InvariantCulture, $"rgb({R}, {G}, {B})");

    /// <summary>Formats as <c>#rrggbb</c> (or <c>#rrggbbaa</c>).</summary>
    public string ToHex() => Alpha is { } a
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}{(byte)Math.Round(a * 255):x2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");
}

/// <summary>A CMYK color with components 0–1.</summary>
public readonly record struct CmykColor(double Cyan, double Magenta, double Yellow, double Key)
{
    /// <summary>Formats as <c>cmyk(c%, m%, y%, k%)</c>.</summary>
    public string ToCss() => string.Create(CultureInfo.InvariantCulture,
        $"cmyk({Math.Round(Cyan * 100)}%, {Math.Round(Magenta * 100)}%, {Math.Round(Yellow * 100)}%, {Math.Round(Key * 100)}%)");
}

/// <summary>An HSL color: hue in degrees, saturation and lightness 0–1, optional alpha.</summary>
public readonly record struct HslColor(int Hue, double Saturation, double Lightness, double? Alpha = null)
{
    /// <summary>Formats as <c>hsl(hdeg s% l%)</c> (with <c>/ a</c> when there is alpha).</summary>
    public string ToCss() => Alpha is { } a
        ? string.Create(CultureInfo.InvariantCulture, $"hsl({Hue}deg {Math.Round(Saturation * 100)}% {Math.Round(Lightness * 100)}% / {a})")
        : string.Create(CultureInfo.InvariantCulture, $"hsl({Hue}deg {Math.Round(Saturation * 100)}% {Math.Round(Lightness * 100)}%)");
}

/// <summary>An HWB color: hue in degrees, whiteness and blackness 0–1.</summary>
public readonly record struct HwbColor(int Hue, double Whiteness, double Blackness)
{
    /// <summary>Formats as <c>hwb(h w% b%)</c>.</summary>
    public string ToCss() => string.Create(CultureInfo.InvariantCulture, $"hwb({Hue} {Math.Round(Whiteness * 100)}% {Math.Round(Blackness * 100)}%)");
}

/// <summary>A CIE LAB color: lightness 0–1, a and b −100..100.</summary>
public readonly record struct LabColor(double Lightness, double A, double B)
{
    /// <summary>Formats as <c>lab(l% a b)</c>.</summary>
    public string ToCss() => string.Create(CultureInfo.InvariantCulture, $"lab({Math.Round(Lightness * 100)}% {A} {B})");
}

/// <summary>A CIE LCH color: lightness 0–1, chroma 0–230, hue 0–360.</summary>
public readonly record struct LchColor(double Lightness, double Chroma, double Hue)
{
    /// <summary>Formats as <c>lch(l% c h)</c>.</summary>
    public string ToCss() => string.Create(CultureInfo.InvariantCulture, $"lch({Math.Round(Lightness * 100)}% {Chroma} {Hue})");
}
