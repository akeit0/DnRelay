using DnRelay.Tool.Models;
using DnRelay.Tool.Options;
using DnRelay.Tool.Utilities;

namespace DnRelay.Tool.Commands;

static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintUsage();
            return 0;
        }

        if (string.Equals(args[0], "tool-refresh", StringComparison.OrdinalIgnoreCase))
        {
            return await RunToolRefreshAsync(args[1..]);
        }

        Console.Error.WriteLine($"Unsupported command: {args[0]}");
        PrintUsage();
        return 1;
    }

    private static async Task<int> RunToolRefreshAsync(string[] args)
    {
        if (HasHelpToken(args))
        {
            PrintToolRefreshHelp();
            return 0;
        }

        var parseResult = ToolRefreshCommandOptions.Parse(args, Directory.GetCurrentDirectory());
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(options.ProjectPath ?? Directory.GetCurrentDirectory());
        var projectPath = options.ProjectPath is null
            ? ToolProjectLocator.FindSingleToolProject(repoRoot)
            : Path.GetFullPath(options.ProjectPath, Directory.GetCurrentDirectory());

        if (projectPath is null)
        {
            Console.Error.WriteLine("Could not find a single PackAsTool project. Pass the project path explicitly.");
            return 1;
        }

        var metadata = ToolProjectFile.Load(projectPath);
        if (metadata is null)
        {
            Console.Error.WriteLine($"Failed to load tool project metadata from: {projectPath}");
            return 1;
        }

        Console.WriteLine("TOOL REFRESH STARTED");
        Console.WriteLine($"target: {Path.GetRelativePath(repoRoot, projectPath)}");
        Console.WriteLine($"artifacts: {Path.GetRelativePath(repoRoot, Path.Combine(repoRoot, "artifacts"))}");

        var bumpKind = options.BumpKind ?? metadata.DefaultBumpKind;
        var nextVersion = options.Version ?? SemVerBumper.Bump(metadata.Version, bumpKind);
        ToolProjectFile.UpdateVersion(projectPath, metadata, nextVersion);

        try
        {
            var artifactsDirectory = Path.Combine(repoRoot, "artifacts");
            Directory.CreateDirectory(artifactsDirectory);

            var packResult = await SimpleProcess.RunAsync("dotnet", ["pack", projectPath, "-c", options.Configuration, "-o", artifactsDirectory], repoRoot);
            if (packResult.ExitCode != 0)
            {
                ToolProjectFile.UpdateVersion(projectPath, metadata, metadata.Version);
                Console.Error.WriteLine(packResult.Output);
                return packResult.ExitCode;
            }

            var updateResult = await SimpleProcess.RunAsync("dotnet", ["tool", "update", "--local", metadata.PackageId, "--version", nextVersion, "--add-source", artifactsDirectory], repoRoot);
            string action;
            if (updateResult.ExitCode == 0)
            {
                action = "updated";
            }
            else
            {
                var installResult = await SimpleProcess.RunAsync("dotnet", ["tool", "install", "--local", metadata.PackageId, "--version", nextVersion, "--add-source", artifactsDirectory], repoRoot);
                if (installResult.ExitCode != 0)
                {
                    ToolProjectFile.UpdateVersion(projectPath, metadata, metadata.Version);
                    Console.Error.WriteLine(updateResult.Output);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(installResult.Output);
                    return installResult.ExitCode;
                }

                action = "installed";
            }

            Console.WriteLine("TOOL REFRESH SUCCEEDED");
            Console.WriteLine($"project: {Path.GetRelativePath(repoRoot, projectPath)}");
            Console.WriteLine($"package: {metadata.PackageId}");
            Console.WriteLine($"action: {action}");
            Console.WriteLine($"bump: {(options.Version is null ? bumpKind : "explicit")}");
            Console.WriteLine($"version: {metadata.Version} -> {nextVersion}");
            Console.WriteLine($"artifacts: {Path.GetRelativePath(repoRoot, artifactsDirectory)}");
            return 0;
        }
        catch
        {
            ToolProjectFile.UpdateVersion(projectPath, metadata, metadata.Version);
            throw;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("dnrelay-tool");
        Console.WriteLine("Companion development tool for DnRelay.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay-tool <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  tool-refresh  Bump or set tool version, pack the local tool, and update or install it in the local tool manifest");
        Console.WriteLine();
        Console.WriteLine("Help:");
        Console.WriteLine("  dnrelay-tool --help");
        Console.WriteLine("  dnrelay-tool <command> --help");
    }

    private static void PrintToolRefreshHelp()
    {
        Console.WriteLine("dnrelay-tool tool-refresh");
        Console.WriteLine("Bump or set tool version, pack the local tool, and update or install it in the local tool manifest.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay-tool tool-refresh [project] [--configuration <Debug|Release>] [--bump <patch|minor|major>] [--version <semver>]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  [project]      Tool project path. Defaults to the single PackAsTool project in the repo");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --configuration  dotnet pack configuration. Default is Release");
        Console.WriteLine("  --bump           Version part to increment. Default comes from <ToolRefreshBump> or patch");
        Console.WriteLine("  --version        Set an explicit target version instead of bumping");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Prefer running this as the installed local tool: dotnet dnrelay-tool tool-refresh");
        Console.WriteLine("  Running tool-refresh through dotnet run can self-lock the apphost during pack.");
        Console.WriteLine("  If the tool is not yet installed in the local manifest, tool-refresh installs it after packing.");
    }

    private static bool HasHelpToken(string[] args)
        => args.Any(IsHelpToken);

    private static bool IsHelpToken(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);
}
