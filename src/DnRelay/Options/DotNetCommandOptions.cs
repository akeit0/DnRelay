using DnRelay.Parsing;
using DnRelay.Utilities;

namespace DnRelay.Options;

sealed class DotNetCommandOptions
{
    public required string TargetPath { get; init; }
    public required List<string> PassthroughArguments { get; init; }
    public required TimeSpan? Timeout { get; init; }
    public required bool Json { get; init; }
    public required string? LogsDirectoryOverride { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public static ParseOutcome<DotNetCommandOptions> Parse(string[] args, string currentDirectory)
    {
        string? target = null;
        var passthrough = new List<string>();
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
                passthrough.Add(arg);
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

            if (string.Equals(arg, "--logs-dir", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Missing value for --logs-dir.");
                }

                logsDirectoryOverride = args[++index];
                continue;
            }

            if (string.Equals(arg, "--target", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Missing value for --target.");
                }

                target = args[++index];
                continue;
            }

            if (string.Equals(arg, "--timeout", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Missing value for --timeout.");
                }

                var timeoutText = args[++index];
                if (!DurationParser.TryParse(timeoutText, out timeout))
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail($"Invalid timeout value: {timeoutText}");
                }

                continue;
            }

            if (string.Equals(arg, "--env", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Missing value for --env.");
                }

                if (!TryParseEnvironmentAssignment(args[++index], out var name, out var value))
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Invalid --env value. Expected KEY=VALUE.");
                }

                environmentVariables[name] = value;
                continue;
            }

            if (string.Equals(arg, "--env-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail("Missing value for --env-file.");
                }

                var envFilePath = Path.GetFullPath(args[++index], currentDirectory);
                if (!File.Exists(envFilePath))
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail($"Environment file not found: {envFilePath}");
                }

                var envFileResult = EnvFileParser.Parse(envFilePath);
                if (!envFileResult.Success)
                {
                    return ParseOutcome<DotNetCommandOptions>.Fail(envFileResult.ErrorMessage!);
                }

                foreach (var pair in envFileResult.EnvironmentVariables!)
                {
                    environmentVariables[pair.Key] = pair.Value;
                }

                continue;
            }

            if (target is null && !arg.StartsWith("-", StringComparison.Ordinal))
            {
                target = arg;
                continue;
            }

            passthrough.Add(arg);
        }

        target ??= currentDirectory;
        var resolvedTarget = Path.GetFullPath(target, currentDirectory);
        return ParseOutcome<DotNetCommandOptions>.Ok(new DotNetCommandOptions
        {
            TargetPath = resolvedTarget,
            PassthroughArguments = passthrough,
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
