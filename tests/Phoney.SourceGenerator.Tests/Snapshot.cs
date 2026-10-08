using System.Reflection;

namespace Phoney.SourceGenerator.Tests;

/// <summary>
/// Minimal snapshot assertions: compares text with <c>Snapshots/&lt;name&gt;.verified.cs</c>. On a mismatch (or the
/// first run) it writes <c>&lt;name&gt;.received.cs</c> next to it and fails; review it and rename it to accept.
/// </summary>
internal static class Snapshot
{
    // The project directory comes from build metadata: [CallerFilePath] is path-mapped (/_/...) in CI builds.
    private static readonly string Directory = Path.Combine(
        typeof(Snapshot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ProjectDirectory").Value!,
        "Snapshots");

    public static void Match(string actual, string name)
    {
        var directory = Directory;
        System.IO.Directory.CreateDirectory(directory);
        var verified = Path.Combine(directory, name + ".verified.cs");
        var received = Path.Combine(directory, name + ".received.cs");
        actual = actual.ReplaceLineEndings("\n");

        if (File.Exists(verified) && File.ReadAllText(verified).ReplaceLineEndings("\n") == actual)
        {
            File.Delete(received);
            return;
        }

        File.WriteAllText(received, actual);
        Assert.Fail($"Snapshot '{name}' does not match. Review {received} and rename it to {Path.GetFileName(verified)} to accept.");
    }
}
