using DnRelay.Parsing;

namespace DnRelay.Options;

sealed class ConfigLogsCommandOptions
{
    public required string LogsDirectory { get; init; }
    public required bool Force { get; init; }

    public static ParseOutcome<ConfigLogsCommandOptions> Parse(string[] args)
    {
        string? logsDirectory = null;
        var force = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (string.Equals(arg, "--force", StringComparison.Ordinal))
            {
                force = true;
                continue;
            }

            if (logsDirectory is null && !arg.StartsWith("-", StringComparison.Ordinal))
            {
                logsDirectory = arg;
                continue;
            }

            return ParseOutcome<ConfigLogsCommandOptions>.Fail($"Unsupported argument: {arg}");
        }

        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            return ParseOutcome<ConfigLogsCommandOptions>.Fail("Missing log directory. Usage: dnrelay config logs <path> [--force]");
        }

        return ParseOutcome<ConfigLogsCommandOptions>.Ok(new ConfigLogsCommandOptions
        {
            LogsDirectory = logsDirectory,
            Force = force
        });
    }
}
