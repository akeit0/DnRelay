namespace DnRelay.Utilities;

static class DnRelayDirectory
{
    public static string Ensure(string repoRoot)
    {
        var dnRelayDirectory = Path.Combine(repoRoot, ".dnrelay");
        Directory.CreateDirectory(dnRelayDirectory);
        EnsureGitIgnore(dnRelayDirectory);
        return dnRelayDirectory;
    }

    private static void EnsureGitIgnore(string dnRelayDirectory)
    {
        var gitIgnorePath = Path.Combine(dnRelayDirectory, ".gitignore");
        const string content = "logs/\nlocks/\npids/\n";

        if (File.Exists(gitIgnorePath))
        {
            return;
        }

        File.WriteAllText(gitIgnorePath, content);
    }
}
