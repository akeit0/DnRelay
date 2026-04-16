using System.Xml.Linq;
using DnRelay.Models;

namespace DnRelay.Utilities;

static class BuildGraphLockScopeResolver
{
    public static BuildLockScope Resolve(string repoRoot, string targetPath)
    {
        var resolvedTarget = Path.GetFullPath(targetPath);
        var projects = ResolveProjects(resolvedTarget);
        if (projects.Count == 0)
        {
            return CreateRepoScope(repoRoot, resolvedTarget, "target could not be resolved to project graph");
        }

        var expandedProjects = ExpandProjectReferences(projects);
        if (expandedProjects.Count == 0)
        {
            return CreateRepoScope(repoRoot, resolvedTarget, "project graph expansion failed");
        }

        var locksDirectory = Path.Combine(DnRelayDirectory.Ensure(repoRoot), "locks");
        Directory.CreateDirectory(locksDirectory);

        var normalizedProjects = expandedProjects
            .Select(static path => Path.GetFullPath(path).ToUpperInvariant())
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();

        var lockPaths = normalizedProjects
            .Select(projectPath => Path.Combine(locksDirectory, $"{ComputeHash(projectPath)}.lock"))
            .ToList();

        var summary = expandedProjects.Count == 1
            ? Path.GetRelativePath(repoRoot, expandedProjects.First())
            : $"{expandedProjects.Count} projects";

        return new BuildLockScope(lockPaths, "build graph lock", summary);
    }

    private static HashSet<string> ResolveProjects(string targetPath)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(targetPath))
        {
            var solutionFiles = Directory.GetFiles(targetPath, "*.sln", SearchOption.TopDirectoryOnly);
            var slnxFiles = Directory.GetFiles(targetPath, "*.slnx", SearchOption.TopDirectoryOnly);
            var projectFiles = Directory.GetFiles(targetPath, "*.csproj", SearchOption.TopDirectoryOnly);

            var candidateCount = solutionFiles.Length + slnxFiles.Length + projectFiles.Length;
            if (candidateCount != 1)
            {
                return projects;
            }

            if (solutionFiles.Length == 1) AddSolutionProjects(solutionFiles[0], projects);
            if (slnxFiles.Length == 1) AddSlnxProjects(slnxFiles[0], projects);
            if (projectFiles.Length == 1) projects.Add(Path.GetFullPath(projectFiles[0]));
            return projects;
        }

        if (!File.Exists(targetPath))
        {
            return projects;
        }

        var extension = Path.GetExtension(targetPath);
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projects.Add(Path.GetFullPath(targetPath));
        }
        else if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            AddSolutionProjects(targetPath, projects);
        }
        else if (extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            AddSlnxProjects(targetPath, projects);
        }

        return projects;
    }

    private static HashSet<string> ExpandProjectReferences(HashSet<string> rootProjects)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>(rootProjects.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase));
        while (stack.Count > 0)
        {
            var projectPath = stack.Pop();
            if (!visited.Add(projectPath))
            {
                continue;
            }

            foreach (var reference in ReadProjectReferences(projectPath))
            {
                stack.Push(reference);
            }
        }

        return visited;
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            yield break;
        }

        XDocument? document;
        try
        {
            document = XDocument.Load(projectPath, LoadOptions.None);
        }
        catch
        {
            yield break;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        foreach (var element in document.Descendants().Where(static element => element.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            yield return Path.GetFullPath(include, projectDirectory);
        }
    }

    private static void AddSlnxProjects(string slnxPath, HashSet<string> projects)
    {
        XDocument? document;
        try
        {
            document = XDocument.Load(slnxPath, LoadOptions.None);
        }
        catch
        {
            return;
        }

        var baseDirectory = Path.GetDirectoryName(slnxPath) ?? Directory.GetCurrentDirectory();
        foreach (var project in document.Descendants().Where(static element => element.Name.LocalName == "Project"))
        {
            var path = project.Attribute("Path")?.Value;
            if (!string.IsNullOrWhiteSpace(path))
            {
                projects.Add(Path.GetFullPath(path, baseDirectory));
            }
        }
    }

    private static void AddSolutionProjects(string solutionPath, HashSet<string> projects)
    {
        var baseDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        foreach (var line in File.ReadLines(solutionPath))
        {
            if (!line.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 2)
            {
                continue;
            }

            var relativePath = parts[1].Trim().Trim('"');
            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projects.Add(Path.GetFullPath(relativePath, baseDirectory));
        }
    }

    private static BuildLockScope CreateRepoScope(string repoRoot, string targetPath, string reason)
    {
        var locksDirectory = Path.Combine(DnRelayDirectory.Ensure(repoRoot), "locks");
        Directory.CreateDirectory(locksDirectory);
        return new BuildLockScope(
            [Path.Combine(locksDirectory, "repo-build.lock")],
            "build lock",
            $"repo fallback ({reason})");
    }

    private static string ComputeHash(string input)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)))[..12].ToLowerInvariant();
}
