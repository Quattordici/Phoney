using System.Globalization;
using System.Text.Json;

namespace Phoney.DotNetSupport;

/// <summary>
/// Which .NET versions Phoney targets: every LTS release Microsoft still supports, plus every supported stable release after the oldest of them.
/// Previews and release candidates (support phase <c>preview</c>, <c>go-live</c>) are not stable; <c>eol</c> releases are not supported.
/// </summary>
internal static class SupportedReleases
{
    /// <summary>The URL of Microsoft's release index, the source of the support phases.</summary>
    public const string IndexUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json";

    private static readonly string[] SupportedPhases = ["active", "maintenance"];

    /// <summary>The target frameworks (<c>net10.0</c>…), oldest first, for the JSON of Microsoft's release index.</summary>
    public static string[] TargetFrameworks(string releasesIndexJson)
    {
        using var document = JsonDocument.Parse(releasesIndexJson);
        var supported = document.RootElement.GetProperty("releases-index").EnumerateArray()
            .Where(r => SupportedPhases.Contains(r.GetProperty("support-phase").GetString(), StringComparer.Ordinal))
            .Select(r => (Version: Version.Parse(r.GetProperty("channel-version").GetString()!), Lts: r.GetProperty("release-type").GetString() == "lts"))
            .ToList();

        var oldestLts = supported.Where(r => r.Lts).Select(r => r.Version).DefaultIfEmpty().Min()
            ?? throw new InvalidOperationException("The release index lists no supported LTS release, so the support rule has nothing to start from.");

        return [.. supported
            .Where(r => r.Version >= oldestLts)
            .Select(r => r.Version)
            .Order()
            .Select(v => string.Create(CultureInfo.InvariantCulture, $"net{v.Major}.{v.Minor}"))];
    }
}
