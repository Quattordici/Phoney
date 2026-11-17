using System.Text.RegularExpressions;
using Phoney.DotNetSupport;

// Compares <PhoneyTargetFrameworks> in Directory.Build.props with the support rule applied to Microsoft's release index (SupportedReleases).
// Usage: dotnet run --project tools/Phoney.DotNetSupport [-- <repository root> [<releases-index.json>]]; exits with 1 when they differ.
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var index = args.Length > 1
    ? await File.ReadAllTextAsync(args[1])
    : await new HttpClient().GetStringAsync(new Uri(SupportedReleases.IndexUrl));

var expected = SupportedReleases.TargetFrameworks(index);
var props = await File.ReadAllTextAsync(Path.Combine(root, "Directory.Build.props"));
var match = Regex.Match(props, "<PhoneyTargetFrameworks>([^<]+)</PhoneyTargetFrameworks>");
if (!match.Success)
{
    Console.WriteLine("::error::Directory.Build.props has no <PhoneyTargetFrameworks>.");
    return 1;
}

var actual = match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (actual.SequenceEqual(expected, StringComparer.Ordinal))
{
    Console.WriteLine($"Phoney targets the supported .NET releases: {string.Join(", ", actual)}.");
    return 0;
}

var add = expected.Except(actual, StringComparer.Ordinal).ToArray();
var drop = actual.Except(expected, StringComparer.Ordinal).ToArray();
Console.WriteLine(
    $"::error::Phoney should target {string.Join(";", expected)} but targets {string.Join(";", actual)}."
    + (add.Length > 0 ? $" Add {string.Join(", ", add)}." : "")
    + (drop.Length > 0 ? $" Drop {string.Join(", ", drop)}, which Microsoft no longer supports." : "")
    + " Change <PhoneyTargetFrameworks> in Directory.Build.props, then follow the failing TargetFrameworkTests (global.json, workflows, README).");
return 1;
