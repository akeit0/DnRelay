namespace DnRelay.Utilities;

static class RepositoryRootLocator
{
    public static string Find(string targetPath)
    {
        var startDirectory = Directory.Exists(targetPath)
            ? new DirectoryInfo(targetPath)
            : new FileInfo(targetPath).Directory;

        if (startDirectory is null)
        {
            return Directory.GetCurrentDirectory();
        }

        for (var current = startDirectory; current is not null; current = current.Parent)
        {
            if (current.GetDirectories(".git").Length > 0)
            {
                return current.FullName;
            }

            if (current.GetFiles("*.sln").Length > 0 || current.GetFiles("*.slnx").Length > 0)
            {
                return current.FullName;
            }
        }

        return startDirectory.FullName;
    }
}
