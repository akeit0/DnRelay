using DnRelay.Parsing;
using DnRelay.Utilities;

namespace DnRelay.Options;

sealed class RunCommandOptions
{
    public required string WorkingDirectory { get; init; }
    public required string? ProjectPath { get; init; }
    public required List<string> RunArguments { get; init; }
    public required List<string> ApplicationArguments { get; init; }
    public required bool NoBuild { get; init; }
    public required bool RawOutput { get; init; }
    public required TimeSpan? Timeout { get; init; }
    public required bool Json { get; init; }
    public required string? LogsDirectoryOverride { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public static ParseOutcome<RunCommandOptions> Parse(string[] args, string currentDirectory)
    {
        string? projectPath = null;
        var runArguments = new List<string>();
        var applicationArguments = new List<string>();
        var noBuild = false;
        var rawOutput = false;
        TimeSpan? timeout = null;
        var json = false;
        string? logsDirectoryOverride = null;
        var passThroughMode = false;
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (passThroughMode)
            {
                applicationArguments.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                passThroughMode = true;
                continue;
            }

            if (string.Equals(arg, "--json", StringComparison.Ordinal))
            {
                json = true;
                continue;
            }

            if (string.Equals(arg, "--raw", StringComparison.Ordinal))
            {
                rawOutput = true;
                continue;
            }

            if (string.Equals(arg, "--no-build", StringComparison.Ordinal))
            {
                noBuild = true;
                continue;
            }

            if (string.Equals(arg, "--logs-dir", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<RunCommandOptions>.Fail("Missing value for --logs-dir.");
                }

                logsDirectoryOverride = args[++index];
                continue;
            }

            if (string.Equals(arg, "--project", StringComparison.Ordinal) || string.Equals(arg, "--target", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<RunCommandOptions>.Fail($"Missing value for {arg}.");
                }

                projectPath = Path.GetFullPath(args[++index], currentDirectory);
                continue;
            }

            if (string.Equals(arg, "--timeout", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<RunCommandOptions>.Fail("Missing value for --timeout.");
                }

                var timeoutText = args[++index];
                if (!DurationParser.TryParse(timeoutText, out timeout))
                {
                    return ParseOutcome<RunCommandOptions>.Fail($"Invalid timeout value: {timeoutText}");
                }

                continue;
            }

            if (string.Equals(arg, "--env", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<RunCommandOptions>.Fail("Missing value for --env.");
                }

                if (!TryParseEnvironmentAssignment(args[++index], out var name, out var value))
                {
                    return ParseOutcome<RunCommandOptions>.Fail("Invalid --env value. Expected KEY=VALUE.");
                }

                environmentVariables[name] = value;
                continue;
            }

            if (string.Equals(arg, "--env-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<RunCommandOptions>.Fail("Missing value for --env-file.");
                }

                var envFilePath = Path.GetFullPath(args[++index], currentDirectory);
                if (!File.Exists(envFilePath))
                {
                    return ParseOutcome<RunCommandOptions>.Fail($"Environment file not found: {envFilePath}");
                }

                var envFileResult = EnvFileParser.Parse(envFilePath);
                if (!envFileResult.Success)
                {
                    return ParseOutcome<RunCommandOptions>.Fail(envFileResult.ErrorMessage!);
                }

                foreach (var pair in envFileResult.EnvironmentVariables!)
                {
                    environmentVariables[pair.Key] = pair.Value;
                }

                continue;
            }

            if (projectPath is null && !arg.StartsWith("-", StringComparison.Ordinal))
            {
                projectPath = Path.GetFullPath(arg, currentDirectory);
                continue;
            }

            runArguments.Add(arg);
        }

        if (json && rawOutput)
        {
            return ParseOutcome<RunCommandOptions>.Fail("--raw cannot be combined with --json.");
        }

        return ParseOutcome<RunCommandOptions>.Ok(new RunCommandOptions
        {
            WorkingDirectory = currentDirectory,
            ProjectPath = projectPath,
            RunArguments = runArguments,
            ApplicationArguments = applicationArguments,
            NoBuild = noBuild,
            RawOutput = rawOutput,
            Timeout = timeout,
            Json = json,
            LogsDirectoryOverride = logsDirectoryOverride,
            EnvironmentVariables = environmentVariables
        });
    }

    private static bool TryParseEnvironmentAssignment(string text, out string name, out string value)
    {
        var separatorIndex = text.IndexOf('=');
        if (separatorIndex <= 0)
        {
            name = string.Empty;
            value = string.Empty;
            return false;
        }

        name = text[..separatorIndex].Trim();
        value = text[(separatorIndex + 1)..];
        return name.Length > 0;
    }
}
