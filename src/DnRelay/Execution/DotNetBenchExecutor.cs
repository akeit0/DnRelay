using System.Diagnostics;
using System.Text.RegularExpressions;
using DnRelay.Models;
using DnRelay.Options;
using DnRelay.Utilities;

namespace DnRelay.Execution;

static partial class DotNetBenchExecutor
{
    private const int SelectionRequiredExitCode = 2;
    private const int BenchmarkIssueExitCode = 3;

    [GeneratedRegex(@"BenchmarkDotNet\.Artifacts[\\/][^\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactPattern();

    [GeneratedRegex(@"^#\d+\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedBenchmarkPattern();

    [GeneratedRegex(@"^System\.[A-Za-z0-9_.]+Exception:\s+.+$", RegexOptions.CultureInvariant)]
    private static partial Regex ExceptionSummaryPattern();

    [GeneratedRegex(@"^\-\-\->\s+(System\.[A-Za-z0-9_.]+Exception:\s+.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex InnerExceptionSummaryPattern();

    public static async Task<BenchExecutionResult> ExecuteAsync(BenchCommandOptions options, StreamWriter logWriter, string logPath, int timeoutExitCode)
    {
        var outputTail = new Queue<string>();
        var highlights = new List<string>();
        var highlightSet = new HashSet<string>(StringComparer.Ordinal);
        var summaryHighlights = new List<string>();
        var summaryTableHighlights = new List<string>();
        var artifactsPath = GetDefaultArtifactsPath(options);
        var benchmarkRunnerStarted = false;
        var benchmarkIssueDetected = false;
        var summarySectionStarted = false;
        var summarySectionEnded = false;
        string? reason = null;
        var reasonScore = 0;

        var startInfo = CreateRunStartInfo(options);
        await logWriter.WriteLineAsync($"$ dotnet {string.Join(" ", startInfo.ArgumentList.Select(QuoteIfNeeded))}");
        await logWriter.WriteLineAsync();
        await logWriter.FlushAsync();

        var processResult = await ProcessExecution.RunAsync(
            startInfo,
            logWriter,
            options.Timeout,
            timeoutExitCode,
            onLine: HandleLine,
            onHeartbeat: elapsed => Console.WriteLine($"benchmark running... {elapsed.TotalSeconds:F0}s"),
            heartbeatInterval: TimeSpan.FromSeconds(15),
            forwardConsoleInput: true,
            trackingOptions: new ProcessTrackingOptions(
                RepositoryRootLocator.Find(options.ProjectPath ?? options.WorkingDirectory),
                "bench",
                options.ProjectPath ?? options.WorkingDirectory));

        await logWriter.WriteLineAsync();
        await logWriter.WriteLineAsync($"# exit-code: {processResult.ExitCode}");
        await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
        await logWriter.WriteLineAsync($"# log: {logPath}");
        await logWriter.FlushAsync();

        benchmarkIssueDetected = benchmarkIssueDetected || outputTail.Any(IsBenchmarkIssueLine);
        var finalHighlights = processResult.ExitCode == 0 && !benchmarkIssueDetected && (summaryTableHighlights.Count > 0 || summaryHighlights.Count > 0)
            ? summaryTableHighlights.Count > 0 ? summaryTableHighlights : summaryHighlights
            : highlights;

        return new BenchExecutionResult(
            processResult.ExitCode == 0 && benchmarkIssueDetected ? BenchmarkIssueExitCode : processResult.ExitCode,
            processResult.TimedOut,
            processResult.Duration,
            artifactsPath,
            reason,
            finalHighlights,
            FilterOutputTail(outputTail.ToList(), processResult.ExitCode == 0 && !benchmarkIssueDetected && !processResult.TimedOut));

        void HandleLine(string line)
        {
            var trimmed = line.Trim();
            if (!options.Json && ShouldEchoPreambleLine(trimmed, benchmarkRunnerStarted))
            {
                Console.WriteLine(line);
            }

            if (!benchmarkRunnerStarted && IsBenchmarkRunnerStart(trimmed))
            {
                benchmarkRunnerStarted = true;
            }

            if (trimmed.Length == 0)
            {
                return;
            }

            if (string.Equals(trimmed, "// * Summary *", StringComparison.Ordinal))
            {
                summarySectionStarted = true;
                AddSummaryHighlight(trimmed);
                return;
            }

            if (summarySectionStarted && !summarySectionEnded)
            {
                if (string.Equals(trimmed, "// * Legends *", StringComparison.Ordinal))
                {
                    summarySectionEnded = true;
                    return;
                }

                AddSummaryHighlight(trimmed);
                if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    AddSummaryTableHighlight(trimmed);
                }
            }

            if (IsBenchmarkIssueLine(trimmed))
            {
                benchmarkIssueDetected = true;
                TryUpdateReason(trimmed);
            }

            if (!ShouldKeepInOutputTail(trimmed))
            {
                return;
            }

            if (outputTail.Count == 12)
            {
                outputTail.Dequeue();
            }

            outputTail.Enqueue(TrimMessage(trimmed));

            if (trimmed.StartsWith("// * Export *", StringComparison.Ordinal) ||
                trimmed.StartsWith("BenchmarkDotNet.Artifacts", StringComparison.Ordinal) ||
                TryGetExceptionSummary(trimmed, out _))
            {
                if (!TryGetExceptionSummary(trimmed, out var summary) || ShouldKeepExceptionHighlight(summary))
                {
                    AddHighlight(TryGetExceptionSummary(trimmed, out summary) ? summary : trimmed);
                }
            }

            var artifactMatch = ArtifactPattern().Match(trimmed);
            if (artifactMatch.Success)
            {
                var candidate = artifactMatch.Value.Replace('/', Path.DirectorySeparatorChar);
                var candidatePath = Path.GetFullPath(candidate, startInfo.WorkingDirectory);
                artifactsPath = Path.GetDirectoryName(candidatePath) ?? candidatePath;
            }
        }

        void AddHighlight(string text)
        {
            if (highlightSet.Add(text) && highlights.Count < 20)
            {
                highlights.Add(TrimMessage(text));
            }
        }

        void AddSummaryHighlight(string text)
        {
            if (summaryHighlights.Count < 80)
            {
                summaryHighlights.Add(TrimMessage(text));
            }
        }

        void AddSummaryTableHighlight(string text)
        {
            if (summaryTableHighlights.Count < 80)
            {
                summaryTableHighlights.Add(TrimMessage(text));
            }
        }

        void TryUpdateReason(string line)
        {
            var (candidate, score) = GetReasonCandidate(line);
            if (candidate is null || score < reasonScore)
            {
                return;
            }

            reason = candidate;
            reasonScore = score;
        }
    }

    private static bool IsBenchmarkRunnerStart(string line)
        => line.StartsWith("// Validating benchmarks:", StringComparison.Ordinal) ||
           line.StartsWith("// ***** BenchmarkRunner:", StringComparison.Ordinal);

    private static bool ShouldEchoPreambleLine(string line, bool benchmarkRunnerStarted)
    {
        if (benchmarkRunnerStarted)
        {
            return false;
        }

        return line.Length > 0 && !line.StartsWith("// Validating benchmarks:", StringComparison.Ordinal);
    }

    public static ProcessStartInfo CreateBuildStartInfo(BenchCommandOptions options)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = GetExecutionWorkingDirectory(options),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OutputEncoding.Utf8,
            StandardErrorEncoding = OutputEncoding.Utf8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("build");
        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            startInfo.ArgumentList.Add(options.ProjectPath);
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(options.Configuration);

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        return startInfo;
    }

    public static async Task<ProcessRunResult> ListBenchmarksAsync(BenchCommandOptions options)
    {
        var arguments = new List<string>
        {
            "run"
        };

        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            arguments.Add("--project");
            arguments.Add(options.ProjectPath);
        }

        arguments.Add("-c");
        arguments.Add(options.Configuration);
        arguments.Add("--no-build");
        arguments.Add("--");
        arguments.Add("--list");
        arguments.Add("flat");

        return await SimpleProcess.RunAsync("dotnet", arguments, GetExecutionWorkingDirectory(options), options.EnvironmentVariables, applyDotNetCliDefaults: true);
    }

    public static IReadOnlyList<string> ParseBenchmarkList(string output)
    {
        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        var numbered = lines
            .Select(static line => NumberedBenchmarkPattern().Match(line))
            .Where(static match => match.Success)
            .Select(static match => match.Groups[1].Value.Trim())
            .ToList();

        return numbered.Count > 0 ? numbered : lines;
    }

    public static bool HasExplicitSelection(BenchCommandOptions options)
        => options.ListOnly || options.Selectors.Count > 0 || ContainsFilterArgument(options.BenchmarkArguments);

    public static BenchCommandOptions ApplySelection(BenchCommandOptions options, IReadOnlyList<string> availableBenchmarks)
    {
        if (options.Selectors.Count == 0)
        {
            return options;
        }

        var benchmarkArguments = new List<string>(options.BenchmarkArguments);
        foreach (var selector in options.Selectors)
        {
            benchmarkArguments.Add("--filter");
            benchmarkArguments.Add(ToFilter(selector, availableBenchmarks));
        }

        return new BenchCommandOptions
        {
            WorkingDirectory = options.WorkingDirectory,
            ProjectPath = options.ProjectPath,
            Configuration = options.Configuration,
            BenchmarkArguments = benchmarkArguments,
            Selectors = [],
            ListOnly = options.ListOnly,
            Timeout = options.Timeout,
            Json = options.Json,
            LogsDirectoryOverride = options.LogsDirectoryOverride,
            EnvironmentVariables = options.EnvironmentVariables
        };
    }

    public static int GetSelectionRequiredExitCode()
        => SelectionRequiredExitCode;

    private static ProcessStartInfo CreateRunStartInfo(BenchCommandOptions options)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = GetExecutionWorkingDirectory(options),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OutputEncoding.Utf8,
            StandardErrorEncoding = OutputEncoding.Utf8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("run");
        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(options.ProjectPath);
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(options.Configuration);
        startInfo.ArgumentList.Add("--no-build");

        if (options.BenchmarkArguments.Count > 0)
        {
            startInfo.ArgumentList.Add("--");
            foreach (var argument in options.BenchmarkArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        return startInfo;
    }

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static bool ContainsFilterArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "--filter", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ToFilter(string selector, IReadOnlyList<string> availableBenchmarks)
    {
        if (int.TryParse(selector, out var index))
        {
            if (index < 0 || index >= availableBenchmarks.Count)
            {
                throw new InvalidOperationException($"Invalid benchmark index: {selector}");
            }

            return availableBenchmarks[index];
        }

        if (selector.Contains('*', StringComparison.Ordinal) || selector.Contains('?', StringComparison.Ordinal))
        {
            return selector;
        }

        return $"*{selector}*";
    }

    public static string GetExecutionWorkingDirectory(BenchCommandOptions options)
        => options.ProjectPath is null
            ? options.WorkingDirectory
            : Path.GetDirectoryName(options.ProjectPath) ?? options.WorkingDirectory;

    public static string GetDefaultArtifactsPath(BenchCommandOptions options)
        => Path.GetFullPath("BenchmarkDotNet.Artifacts", GetExecutionWorkingDirectory(options));

    private static string TrimMessage(string message)
    {
        const int maxLength = 180;
        var normalized = message.Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..(maxLength - 3)]}...";
    }

    private static bool ShouldKeepInOutputTail(string line)
    {
        if (line.StartsWith("// * Legends *", StringComparison.Ordinal) ||
            line.StartsWith("// * Diagnostic Output -", StringComparison.Ordinal) ||
            line.StartsWith("// * Artifacts cleanup *", StringComparison.Ordinal) ||
            line.StartsWith("// * Warnings *", StringComparison.Ordinal) ||
            string.Equals(line, "Artifacts cleanup is finished", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(line, "Environment", StringComparison.Ordinal) ||
            line.StartsWith("[Host]", StringComparison.Ordinal) ||
            line.StartsWith("ShortRun", StringComparison.Ordinal) ||
            line.StartsWith("Job=", StringComparison.Ordinal) ||
            line.StartsWith("WarmupCount=", StringComparison.Ordinal) ||
            line.StartsWith("Summary -> Detected error exit code from one of the benchmarks.", StringComparison.Ordinal) ||
            line.StartsWith("- Windows Defender", StringComparison.Ordinal) ||
            line.StartsWith("Use InProcessEmitToolchain or InProcessNoEmitToolchain", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.StartsWith("Mean", StringComparison.Ordinal) ||
            line.StartsWith("Error", StringComparison.Ordinal) ||
            line.StartsWith("StdDev", StringComparison.Ordinal) ||
            line.StartsWith("Gen0", StringComparison.Ordinal) ||
            line.StartsWith("Gen1", StringComparison.Ordinal) ||
            line.StartsWith("Allocated", StringComparison.Ordinal) ||
            line.StartsWith("1 ns", StringComparison.Ordinal) ||
            line.StartsWith("1 us", StringComparison.Ordinal) ||
            line.StartsWith("1 ms", StringComparison.Ordinal))
        {
            return line.Contains('=', StringComparison.Ordinal);
        }

        return true;
    }

    private static IReadOnlyList<string> FilterOutputTail(IReadOnlyList<string> outputTail, bool successfulRun)
    {
        if (!successfulRun)
        {
            return outputTail;
        }

        return [];
    }

    private static bool IsBenchmarkIssueLine(string line)
        => line.StartsWith("Benchmarks with issues:", StringComparison.Ordinal) ||
           line.StartsWith("There are not any results runs", StringComparison.Ordinal) ||
           line.Contains("Detected error exit code from one of the benchmarks", StringComparison.Ordinal) ||
           line.Contains("Exception has been thrown by the target of an invocation", StringComparison.Ordinal) ||
           TryGetExceptionSummary(line, out _);

    private static bool TryGetExceptionSummary(string line, out string summary)
    {
        var innerExceptionMatch = InnerExceptionSummaryPattern().Match(line);
        if (innerExceptionMatch.Success)
        {
            summary = innerExceptionMatch.Groups[1].Value.Trim();
            return true;
        }

        var directExceptionMatch = ExceptionSummaryPattern().Match(line);
        if (directExceptionMatch.Success)
        {
            summary = directExceptionMatch.Value.Trim();
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private static (string? Reason, int Score) GetReasonCandidate(string line)
    {
        if (TryGetExceptionSummary(line, out var summary))
        {
            return (summary, line.StartsWith("--->", StringComparison.Ordinal) ? 3 : 2);
        }

        if (line.StartsWith("Benchmarks with issues:", StringComparison.Ordinal))
        {
            return ("benchmarks with issues", 1);
        }

        if (line.StartsWith("There are not any results runs", StringComparison.Ordinal))
        {
            return ("no workload results were produced", 1);
        }

        if (line.Contains("Detected error exit code from one of the benchmarks", StringComparison.Ordinal))
        {
            return ("benchmark process returned an error exit code", 1);
        }

        if (line.Contains("Exception has been thrown by the target of an invocation", StringComparison.Ordinal))
        {
            return ("benchmark invocation threw an exception", 1);
        }

        return ("benchmark issue detected", 1);
    }

    private static bool ShouldKeepExceptionHighlight(string summary)
        => !summary.Contains("Exception has been thrown by the target of an invocation", StringComparison.Ordinal);
}
