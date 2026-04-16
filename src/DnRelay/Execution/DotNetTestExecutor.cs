using System.Diagnostics;
using System.Text.RegularExpressions;
using DnRelay.Models;
using DnRelay.Options;
using DnRelay.Parsing;
using DnRelay.Utilities;

namespace DnRelay.Execution;

static partial class DotNetTestExecutor
{
    [GeneratedRegex(@"\berror\s+(?<code>[A-Z]{2,}\d+)\s*:\s*(?<message>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"^Failed\s+(?<name>.+?)(?:\s+\[[^\]]+\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex FailedTestPattern();

    [GeneratedRegex(@"^失敗\s+(?<name>.+?)(?:\s+\[[^\]]+\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseFailedTestPattern();

    public static async Task<TestExecutionResult> ExecuteAsync(DotNetCommandOptions options, StreamWriter logWriter, string logPath, string trxPath, int timeoutExitCode)
    {
        var errorSet = new HashSet<string>(StringComparer.Ordinal);
        var topErrors = new List<string>();
        var fallbackFailures = new List<string>();
        var fallbackFailureSet = new HashSet<string>(StringComparer.Ordinal);
        var errorCount = 0;

        var startInfo = CreateStartInfo(options, trxPath);
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
                "test",
                options.TargetPath));

        await logWriter.WriteLineAsync();
        await logWriter.WriteLineAsync($"# exit-code: {processResult.ExitCode}");
        await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
        await logWriter.WriteLineAsync($"# log: {logPath}");
        await logWriter.WriteLineAsync($"# trx: {trxPath}");
        await logWriter.FlushAsync();

        var trxSummary = TrxParser.TryParse(trxPath);
        var topFailures = trxSummary?.FailedTests.Take(10).ToList() ?? fallbackFailures;

        return new TestExecutionResult(
            processResult.ExitCode,
            processResult.TimedOut,
            processResult.Duration,
            trxSummary?.TotalCount ?? 0,
            trxSummary?.PassedCount ?? 0,
            trxSummary?.FailedCount ?? 0,
            trxSummary?.SkippedCount ?? 0,
            errorCount,
            trxPath,
            topFailures,
            topErrors);

        void HandleLine(string line)
        {
            var failureMatch = FailedTestPattern().Match(line.Trim());
            if (!failureMatch.Success)
            {
                failureMatch = JapaneseFailedTestPattern().Match(line.Trim());
            }

            if (failureMatch.Success)
            {
                var failureName = failureMatch.Groups["name"].Value.Trim();
                if (!failureName.Contains(" - Failed:", StringComparison.Ordinal) &&
                    !failureName.Contains(" -失敗:", StringComparison.Ordinal) &&
                    fallbackFailureSet.Add(failureName) &&
                    fallbackFailures.Count < 10)
                {
                    fallbackFailures.Add(failureName);
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

    private static ProcessStartInfo CreateStartInfo(DotNetCommandOptions options, string trxPath)
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

        startInfo.ArgumentList.Add("test");
        if (Directory.Exists(options.TargetPath) || File.Exists(options.TargetPath))
        {
            startInfo.ArgumentList.Add(options.TargetPath);
        }

        foreach (var argument in options.PassthroughArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        var resultsDirectory = Path.GetDirectoryName(trxPath) ?? Directory.GetCurrentDirectory();
        startInfo.ArgumentList.Add("--results-directory");
        startInfo.ArgumentList.Add(resultsDirectory);
        startInfo.ArgumentList.Add("--logger");
        startInfo.ArgumentList.Add($"trx;LogFileName={Path.GetFileName(trxPath)}");

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
