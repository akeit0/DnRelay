using System.Diagnostics;
using DnRelay.Models;
using DnRelay.Options;
using DnRelay.Utilities;

namespace DnRelay.Execution;

static class DotNetRunExecutor
{
    public static async Task<RunExecutionResult> ExecuteAsync(RunCommandOptions options, StreamWriter logWriter, string logPath, int timeoutExitCode)
    {
        var outputTail = new Queue<string>();
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
            onHeartbeat: options.RawOutput ? null : elapsed => Console.WriteLine($"running... {elapsed.TotalSeconds:F0}s"),
            trackingOptions: new ProcessTrackingOptions(
                RepositoryRootLocator.Find(options.ProjectPath ?? options.WorkingDirectory),
                "run",
                options.ProjectPath ?? options.WorkingDirectory));

        await logWriter.WriteLineAsync();
        await logWriter.WriteLineAsync($"# exit-code: {processResult.ExitCode}");
        await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
        await logWriter.WriteLineAsync($"# log: {logPath}");
        await logWriter.FlushAsync();

        return new RunExecutionResult(
            processResult.ExitCode,
            processResult.TimedOut,
            processResult.Duration,
            options.RawOutput ? [] : outputTail.ToList(),
            options.RawOutput);

        void HandleLine(string line)
        {
            if (options.RawOutput && !options.Json)
            {
                Console.WriteLine(line);
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (outputTail.Count == 10)
            {
                outputTail.Dequeue();
            }

            outputTail.Enqueue(TrimMessage(line));
        }
    }

    public static ProcessStartInfo CreateBuildStartInfo(RunCommandOptions options)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = options.WorkingDirectory,
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

        foreach (var argument in GetBuildRelevantRunArguments(options.RunArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        return startInfo;
    }

    public static bool RequiresBuild(RunCommandOptions options)
        => !options.NoBuild;

    private static ProcessStartInfo CreateRunStartInfo(RunCommandOptions options)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = options.WorkingDirectory,
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

        foreach (var argument in options.RunArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add("--no-build");

        if (options.ApplicationArguments.Count > 0)
        {
            startInfo.ArgumentList.Add("--");
            foreach (var argument in options.ApplicationArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        DotNetCliEnvironmentDefaults.Apply(startInfo, options.EnvironmentVariables);

        return startInfo;
    }

    private static string QuoteIfNeeded(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string TrimMessage(string message)
    {
        const int maxLength = 160;
        var normalized = message.Trim();
        return normalized.Length <= maxLength ? normalized : $"{normalized[..(maxLength - 3)]}...";
    }

    private static IEnumerable<string> GetBuildRelevantRunArguments(IReadOnlyList<string> runArguments)
    {
        for (var index = 0; index < runArguments.Count; index++)
        {
            var argument = runArguments[index];
            switch (argument)
            {
                case "-c":
                case "--configuration":
                case "-f":
                case "--framework":
                case "-r":
                case "--runtime":
                case "--os":
                case "--arch":
                case "--ucr":
                case "--use-current-runtime":
                    yield return argument;
                    if (index + 1 < runArguments.Count && ExpectsValue(argument))
                    {
                        yield return runArguments[++index];
                    }
                    break;
                case "--disable-build-servers":
                case "--no-restore":
                    yield return argument;
                    break;
            }
        }
    }

    private static bool ExpectsValue(string argument)
        => argument is "-c" or "--configuration" or "-f" or "--framework" or "-r" or "--runtime" or "--os" or "--arch";
}
