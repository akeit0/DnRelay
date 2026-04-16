namespace DnRelay.Tool.Utilities;

static class ToolProjectLocator
{
    public static string? FindSingleToolProject(string repoRoot)
    {
        var candidates = Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => ToolProjectFile.Load(path)?.PackAsTool == true)
            .Take(2)
            .ToArray();

        return candidates.Length == 1 ? candidates[0] : null;
    }
}
