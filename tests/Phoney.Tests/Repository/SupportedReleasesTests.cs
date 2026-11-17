using Phoney.DotNetSupport;

namespace Phoney.Tests.Repository;

/// <summary>
/// The support rule: every .NET LTS Microsoft still supports, plus every stable release after the oldest of them. Fed with the shape of Microsoft's
/// release index (builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json), which the scheduled support check downloads.
/// </summary>
public sealed class SupportedReleasesTests
{
    private static string Index(params (string Version, string Type, string Phase)[] releases) =>
        "{\"releases-index\":[" + string.Join(",", releases.Select(r =>
            $"{{\"channel-version\":\"{r.Version}\",\"release-type\":\"{r.Type}\",\"support-phase\":\"{r.Phase}\"}}")) + "]}";

    [Test]
    public void After_net8_and_net9_reached_end_of_life_the_targets_are_net10_and_net11()
    {
        var index = Index(("12.0", "lts", "preview"), ("11.0", "sts", "active"), ("10.0", "lts", "active"), ("9.0", "sts", "eol"), ("8.0", "lts", "eol"));

        SupportedReleases.TargetFrameworks(index).ShouldBe(["net10.0", "net11.0"]);
    }

    [Test]
    public void A_supported_release_between_two_supported_lts_releases_is_a_target()
    {
        var index = Index(("10.0", "lts", "active"), ("9.0", "sts", "active"), ("8.0", "lts", "maintenance"));

        SupportedReleases.TargetFrameworks(index).ShouldBe(["net8.0", "net9.0", "net10.0"]);
    }

    [Test]
    public void Releases_before_the_oldest_supported_lts_are_not_targets()
    {
        var index = Index(("10.0", "lts", "active"), ("9.0", "sts", "maintenance"), ("8.0", "lts", "eol"));

        SupportedReleases.TargetFrameworks(index).ShouldBe(["net10.0"]);
    }

    [Test]
    [Arguments("preview")]
    [Arguments("go-live")]
    [Arguments("eol")]
    public void A_release_that_is_not_stable_and_supported_is_not_a_target(string phase)
    {
        var index = Index(("11.0", "sts", phase), ("10.0", "lts", "active"));

        SupportedReleases.TargetFrameworks(index).ShouldBe(["net10.0"]);
    }

    [Test]
    public void Versions_are_ordered_as_numbers()
    {
        var index = Index(("10.0", "sts", "active"), ("9.0", "lts", "active"));

        SupportedReleases.TargetFrameworks(index).ShouldBe(["net9.0", "net10.0"]);
    }

    [Test]
    public void An_index_without_a_supported_lts_release_is_refused()
    {
        var index = Index(("11.0", "sts", "active"), ("10.0", "lts", "eol"));

        Should.Throw<InvalidOperationException>(() => SupportedReleases.TargetFrameworks(index)).Message.ShouldContain("no supported LTS");
    }
}
