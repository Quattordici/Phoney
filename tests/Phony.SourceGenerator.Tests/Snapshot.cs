using System.Runtime.CompilerServices;

namespace Phony.SourceGenerator.Tests;

/// <summary>
/// Minimal snapshot assertions: compares text with <c>Snapshots/&lt;name&gt;.verified.cs</c>. On a mismatch (or the
/// first run) it writes <c>&lt;name&gt;.received.cs</c> next to it and fails; review it and rename it to accept.
/// </summary>
internal static class Snapshot
{
    public static void Match(string actual, string name, [CallerFilePath] string callerFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        Directory.CreateDirectory(directory);
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
