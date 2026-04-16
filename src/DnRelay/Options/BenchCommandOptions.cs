using DnRelay.Parsing;
using DnRelay.Utilities;

namespace DnRelay.Options;

sealed class BenchCommandOptions
{
    public required string WorkingDirectory { get; init; }
    public required string? ProjectPath { get; init; }
    public required string Configuration { get; init; }
    public required List<string> BenchmarkArguments { get; init; }
    public required List<string> Selectors { get; init; }
    public required bool ListOnly { get; init; }
    public required TimeSpan? Timeout { get; init; }
    public required bool Json { get; init; }
    public required string? LogsDirectoryOverride { get; init; }
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public static ParseOutcome<BenchCommandOptions> Parse(string[] args, string currentDirectory)
    {
        string? projectPath = null;
        var configuration = "Release";
        var benchmarkArguments = new List<string>();
        var selectors = new List<string>();
        TimeSpan? timeout = null;
        var json = false;
        string? logsDirectoryOverride = null;
        var listOnly = false;
        var passThroughMode = false;
        var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (passThroughMode)
            {
                benchmarkArguments.Add(arg);
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
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --logs-dir.");
                }

                logsDirectoryOverride = args[++index];
                continue;
            }

            if (string.Equals(arg, "--list", StringComparison.Ordinal))
            {
                listOnly = true;
                continue;
            }

            if (string.Equals(arg, "--select", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --select.");
                }

                selectors.Add(args[++index]);
                continue;
            }

            if (string.Equals(arg, "--project", StringComparison.Ordinal) || string.Equals(arg, "--target", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail($"Missing value for {arg}.");
                }

                projectPath = Path.GetFullPath(args[++index], currentDirectory);
                continue;
            }

            if (string.Equals(arg, "-c", StringComparison.Ordinal) || string.Equals(arg, "--configuration", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --configuration.");
                }

                configuration = args[++index];
                continue;
            }

            if (string.Equals(arg, "--timeout", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --timeout.");
                }

                var timeoutText = args[++index];
                if (!DurationParser.TryParse(timeoutText, out timeout))
                {
                    return ParseOutcome<BenchCommandOptions>.Fail($"Invalid timeout value: {timeoutText}");
                }

                continue;
            }

            if (string.Equals(arg, "--env", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --env.");
                }

                if (!TryParseEnvironmentAssignment(args[++index], out var name, out var value))
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Invalid --env value. Expected KEY=VALUE.");
                }

                environmentVariables[name] = value;
                continue;
            }

            if (string.Equals(arg, "--env-file", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail("Missing value for --env-file.");
                }

                var envFilePath = Path.GetFullPath(args[++index], currentDirectory);
                if (!File.Exists(envFilePath))
                {
                    return ParseOutcome<BenchCommandOptions>.Fail($"Environment file not found: {envFilePath}");
                }

                var envFileResult = EnvFileParser.Parse(envFilePath);
                if (!envFileResult.Success)
                {
                    return ParseOutcome<BenchCommandOptions>.Fail(envFileResult.ErrorMessage!);
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

            benchmarkArguments.Add(arg);
        }

        return ParseOutcome<BenchCommandOptions>.Ok(new BenchCommandOptions
        {
            WorkingDirectory = currentDirectory,
            ProjectPath = projectPath,
            Configuration = configuration,
            BenchmarkArguments = benchmarkArguments,
            Selectors = selectors,
            ListOnly = listOnly,
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
