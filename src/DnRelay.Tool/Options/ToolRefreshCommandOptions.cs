namespace DnRelay.Tool.Options;

sealed class ToolRefreshCommandOptions
{
    public required string? ProjectPath { get; init; }
    public required string Configuration { get; init; }
    public required string? BumpKind { get; init; }
    public required string? Version { get; init; }

    public static ParseOutcome<ToolRefreshCommandOptions> Parse(string[] args, string currentDirectory)
    {
        string? projectPath = null;
        var configuration = "Release";
        string? bumpKind = null;
        string? version = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--configuration", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<ToolRefreshCommandOptions>.Fail("Missing value for --configuration.");
                }

                configuration = args[++index];
                continue;
            }

            if (string.Equals(arg, "--bump", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<ToolRefreshCommandOptions>.Fail("Missing value for --bump.");
                }

                bumpKind = args[++index];
                continue;
            }

            if (string.Equals(arg, "--version", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<ToolRefreshCommandOptions>.Fail("Missing value for --version.");
                }

                version = args[++index];
                continue;
            }

            if (projectPath is null && !arg.StartsWith("-", StringComparison.Ordinal))
            {
                projectPath = Path.GetFullPath(arg, currentDirectory);
                continue;
            }

            return ParseOutcome<ToolRefreshCommandOptions>.Fail($"Unknown argument for tool-refresh: {arg}");
        }

        return ParseOutcome<ToolRefreshCommandOptions>.Ok(new ToolRefreshCommandOptions
        {
            ProjectPath = projectPath,
            Configuration = configuration,
            BumpKind = bumpKind,
            Version = version
        });
    }
}
