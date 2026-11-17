using System.Text.RegularExpressions;

namespace Phoney.Tests.Repository;

/// <summary>
/// The supported .NET versions are named once, as <c>PhoneyTargetFrameworks</c> in Directory.Build.props (the scheduled support check compares it
/// with Microsoft's release index). Projects, the SDK, CI and the README follow it, so supporting a new release or dropping one is a change there
/// that these tests then point through.
/// </summary>
public sealed partial class TargetFrameworkTests
{
    private static readonly string[] Targets = SupportedTargets();

    private static IEnumerable<int> Majors => Targets.Select(t => int.Parse(t["net".Length..t.IndexOf('.', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture));

    [Test]
    public void The_supported_targets_are_named_in_Directory_Build_props()
    {
        Targets.ShouldNotBeEmpty();
        Targets.ShouldAllBe(t => NetVersion().IsMatch(t));
    }

    [Test]
    public void No_project_names_a_dotnet_version_of_its_own()
    {
        var projects = new[] { "src", "tests", "tools", "benchmarks", "samples" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, dir), "*.csproj", SearchOption.AllDirectories));

        var offenders = projects
            .Where(p => NetVersion().IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(RepositoryFiles.Root, p))
            .Order(StringComparer.Ordinal);

        offenders.ShouldBeEmpty("Use $(PhoneyTargetFrameworks), $(PhoneyTestTargetFrameworks) or $(PhoneyAppTargetFramework) from Directory.Build.props.");
    }

    [Test]
    public void The_sdk_is_the_one_of_the_newest_target()
    {
        var sdk = SdkVersion().Match(RepositoryFiles.Read("global.json")).Groups[1].Value;

        sdk.ShouldStartWith($"{Majors.Max()}.0.", customMessage: "global.json must name the SDK of the newest target framework.");
    }

    [Test]
    public void Workflows_install_every_target_or_the_newest()
    {
        var all = Majors.Select(m => $"{m}.0.x").ToArray();
        var newest = $"{Majors.Max()}.0.x";
        var workflows = Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, ".github", "workflows"), "*.yml").Order(StringComparer.Ordinal).ToList();
        workflows.ShouldNotBeEmpty();

        foreach (var (workflow, installed) in workflows.SelectMany(w => DotNetVersions(File.ReadAllText(w)).Select(v => (Path.GetFileName(w), v))))
        {
            if (installed.Length == 1)
                installed[0].ShouldBe(newest, $"{workflow}: a job that installs one .NET installs the newest.");
            else
                installed.ShouldBe(all, $"{workflow}: a job that installs several .NET versions installs every target, oldest first.");
        }
    }

    [Test]
    public void The_readme_names_the_targets()
    {
        var names = string.Join(" and ", Majors.Select(m => $".NET {m}"));

        RepositoryFiles.Read("README.md").ShouldContain($"Targets {names}:");
    }

    private static string[] SupportedTargets()
    {
        var match = TargetsProperty().Match(RepositoryFiles.Read("Directory.Build.props"));
        match.Success.ShouldBeTrue("Directory.Build.props has no <PhoneyTargetFrameworks>.");
        return match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>The versions each <c>actions/setup-dotnet</c> step installs, one entry per <c>dotnet-version</c> (a value or a block list).</summary>
    private static IEnumerable<string[]> DotNetVersions(string yaml)
    {
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("dotnet-version:", StringComparison.Ordinal))
                continue;

            var value = line["dotnet-version:".Length..].Trim();
            if (value != "|")
            {
                yield return [value];
                continue;
            }

            var versions = new List<string>();
            while (i + 1 < lines.Length && DotNetChannel().IsMatch(lines[i + 1].Trim()))
                versions.Add(lines[++i].Trim());
            yield return [.. versions];
        }
    }

    [GeneratedRegex(@"<PhoneyTargetFrameworks>([^<]+)</PhoneyTargetFrameworks>")]
    private static partial Regex TargetsProperty();

    [GeneratedRegex(@"\bnet\d+\.\d+\b")]
    private static partial Regex NetVersion();

    [GeneratedRegex("\"version\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex SdkVersion();

    [GeneratedRegex(@"^\d+\.\d+\.x$")]
    private static partial Regex DotNetChannel();
}
