namespace Phoney.Tests.Repository;

/// <summary>The checked-out repository, for tests that check its docs, projects and workflows.</summary>
internal static class RepositoryFiles
{
    public static readonly string Root = FindRoot();

    public static string Read(params string[] path) => File.ReadAllText(Path.Combine([Root, .. path]));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Phoney.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException($"No Phoney.slnx above {AppContext.BaseDirectory}; these tests read the repository's files.");
    }
}
