using System.Diagnostics;
using System.Text.RegularExpressions;
using DnRelay.Models;
using DnRelay.Options;
using DnRelay.Utilities;

namespace DnRelay.Execution;

static partial class DotNetBuildExecutor
{
    [GeneratedRegex(@"\bwarning\s+(?<code>[A-Z]{2,}\d+)\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex WarningPattern();

    [GeneratedRegex(@"\berror\s+(?<code>[A-Z]{2,}\d+)\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern();

    public static async Task<BuildExecutionResult> ExecuteAsync(DotNetCommandOptions options, StreamWriter logWriter, string logPath, int timeoutExitCode)
    {
        var warningSet = new HashSet<string>(StringComparer.Ordinal);
        var errorSet = new HashSet<string>(StringComparer.Ordinal);
        var topWarnings = new List<string>();
        var topErrors = new List<string>();
        var warningCount = 0;
        var errorCount = 0;

        var startInfo = CreateStartInfo(options);
        await logWriter.WriteLineAsync($"$ dotnet {string.Join(" ", startInfo.ArgumentList.Select(QuoteIfNeeded))}");
        await logWriter.WriteLineAsync();
        await logWriter.FlushAsync();

        var processResult = await ProcessExecution.RunAsync(
            startInfo,
            logWriter,
            options.Timeout,
            timeoutExitCode,
            HandleLine,
            trackingOptions: new ProcessTrackingOptions(
                RepositoryRootLocator.Find(options.TargetPath),
                "build",
                options.TargetPath));

        await logWriter.WriteLineAsync();
        await logWriter.WriteLineAsync($"# exit-code: {processResult.ExitCode}");
        await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
        await logWriter.WriteLineAsync($"# log: {logPath}");
        await logWriter.FlushAsync();

        return new BuildExecutionResult(processResult.ExitCode, processResult.TimedOut, processResult.Duration, warningCount, errorCount, topWarnings, topErrors);

        void HandleLine(string line)
        {
            var warningMatch = WarningPattern().Match(line);
            if (warningMatch.Success)
            {
                warningCount++;
                var summary = $"{warningMatch.Groups["code"].Value}: {TrimMessage(warningMatch.Groups["message"].Value)}";
                if (warningSet.Add(summary) && topWarnings.Count < 5)
                {
                    topWarnings.Add(summary);
                }
            }

            var errorMatch = ErrorPattern().Match(line);
            if (errorMatch.Success)
            {
                errorCount++;
                var summary = $"{errorMatch.Groups["code"].Value}: {TrimMessage(errorMatch.Groups["message"].Value)}";
                if (errorSet.Add(summary) && topErrors.Count < 5)
                {
                    topErrors.Add(summary);
                }
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(DotNetCommandOptions options)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Directory.Exists(options.TargetPath) ? options.TargetPath : Path.GetDirectoryName(options.TargetPath) ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OutputEncoding.Utf8,
            StandardErrorEncoding = OutputEncoding.Utf8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("build");
        if (Directory.Exists(options.TargetPath) || File.Exists(options.TargetPath))
        {
            startInfo.ArgumentList.Add(options.TargetPath);
        }

        foreach (var argument in options.PassthroughArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        return startInfo;
    }

    private static string QuoteIfNeeded(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string TrimMessage(string message)
    {
        const int maxLength = 120;
        var normalized = message.Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..(maxLength - 3)]}...";
    }
}
