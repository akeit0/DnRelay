namespace DnRelay.Tool.Utilities;

static class RepositoryRootLocator
{
    public static string Find(string targetPath)
    {
        var path = Path.GetFullPath(targetPath);
        var directory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (directory is null)
        {
            throw new InvalidOperationException($"Unable to determine directory for: {targetPath}");
        }

        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            var hasDotnetTools = File.Exists(Path.Combine(current.FullName, "dotnet-tools.json"));
            var hasSolution = Directory.EnumerateFiles(current.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any() ||
                              Directory.EnumerateFiles(current.FullName, "*.slnx", SearchOption.TopDirectoryOnly).Any();

            if (hasDotnetTools || hasSolution)
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return File.Exists(path)
            ? Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory()
            : path;
    }
}
