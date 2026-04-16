using System.Text;
using System.Text.Json;
using DnRelay.Execution;
using DnRelay.Models;
using DnRelay.Options;
using DnRelay.Serialization;
using DnRelay.Utilities;

namespace DnRelay.Commands;

static class ProgramEntry
{
    private const int TimeoutExitCode = 124;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelpToken(args[0]))
        {
            PrintUsage();
            return 0;
        }

        var command = args[0];
        if (string.Equals(command, "build", StringComparison.OrdinalIgnoreCase))
        {
            return await RunBuildAsync(args[1..]);
        }

        if (string.Equals(command, "test", StringComparison.OrdinalIgnoreCase))
        {
            return await RunTestAsync(args[1..]);
        }

        if (string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
        {
            return await RunRunAsync(args[1..]);
        }

        if (string.Equals(command, "bench", StringComparison.OrdinalIgnoreCase))
        {
            return await RunBenchAsync(args[1..]);
        }

        if (string.Equals(command, "stats", StringComparison.OrdinalIgnoreCase))
        {
            return await RunStatsAsync(args[1..]);
        }

        if (string.Equals(command, "kill", StringComparison.OrdinalIgnoreCase))
        {
            return await RunKillAsync(args[1..]);
        }

        if (string.Equals(command, "config", StringComparison.OrdinalIgnoreCase))
        {
            return await RunConfigAsync(args[1..]);
        }

        Console.Error.WriteLine($"Unsupported command: {command}");
        PrintUsage();
        return 1;
    }

    private static Task<int> RunConfigAsync(string[] args)
    {
        if (args.Length == 0 || HasHelpToken(args))
        {
            PrintConfigHelp();
            return Task.FromResult(0);
        }

        if (string.Equals(args[0], "logs", StringComparison.OrdinalIgnoreCase))
        {
            return RunConfigLogsAsync(args[1..]);
        }

        Console.Error.WriteLine($"Unsupported config subcommand: {args[0]}");
        PrintConfigHelp();
        return Task.FromResult(1);
    }

    private static Task<int> RunConfigLogsAsync(string[] args)
    {
        if (args.Length == 0 || HasHelpToken(args))
        {
            PrintConfigLogsHelp();
            return Task.FromResult(0);
        }

        var parseResult = ConfigLogsCommandOptions.Parse(args);
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return Task.FromResult(1);
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(Directory.GetCurrentDirectory());
        var configDirectory = DnRelayDirectory.Ensure(repoRoot);
        var configPath = Path.Combine(configDirectory, "config.json");

        if (File.Exists(configPath) && !options.Force)
        {
            Console.Error.WriteLine($"Config already exists: {configPath}");
            Console.Error.WriteLine("Pass --force to overwrite it.");
            return Task.FromResult(1);
        }

        var config = new DnRelayConfig(options.LogsDirectory);
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(config, DnRelayJsonContext.Default.DnRelayConfig),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Console.WriteLine("CONFIG UPDATED");
        Console.WriteLine($"config: {ToDisplayPath(repoRoot, configPath)}");
        Console.WriteLine($"logsDir: {options.LogsDirectory}");
        return Task.FromResult(0);
    }

    private static async Task<int> RunBuildAsync(string[] args)
    {
        if (args.Length == 0 || HasHelpToken(args))
        {
            PrintBuildHelp();
            return 0;
        }

        var parseResult = DotNetCommandOptions.Parse(args, Directory.GetCurrentDirectory());
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(options.TargetPath);
        var lockScope = BuildGraphLockScopeResolver.Resolve(repoRoot, options.TargetPath);
        var config = DnRelayConfigLoader.Load(repoRoot);
        var logsDirectory = DnRelayConfigLoader.ResolveLogsDirectory(repoRoot, config, options.LogsDirectoryOverride);
        Directory.CreateDirectory(logsDirectory);

        var startTime = DateTimeOffset.Now;
        var logPath = Path.Combine(logsDirectory, $"build-{startTime:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.txt");
        var relativeLogPath = ToDisplayPath(repoRoot, logPath);
        var relativeTargetPath = Path.GetRelativePath(repoRoot, options.TargetPath);
        await using var logWriter = OpenLog(logPath);
        await WriteLogHeaderAsync(logWriter, "build", repoRoot, options.TargetPath, options.EnvironmentVariables);
        PrintCommandStarted("BUILD", relativeTargetPath, "log", relativeLogPath, options.Json);

        await logWriter.WriteLineAsync($"# lock-scope: {lockScope.Summary}");
        await logWriter.FlushAsync();

        using var buildLock = await BuildLockSet.AcquireAsync(lockScope.LockPaths, "build", options.TargetPath, startTime, CancellationToken.None, lockScope.DisplayName);
        await logWriter.WriteLineAsync($"# lock-wait: {buildLock.TotalWaitDuration.TotalSeconds:F1}s");
        await logWriter.FlushAsync();
        var execution = await DotNetBuildExecutor.ExecuteAsync(options, logWriter, logPath, TimeoutExitCode);

        if (options.Json)
        {
            var payload = new BuildCommandJsonPayload(
                "build",
                execution.ExitCode == 0 ? "succeeded" : execution.TimedOut ? "timed_out" : "failed",
                relativeTargetPath,
                execution.Duration.TotalSeconds,
                execution.WarningCount,
                execution.ErrorCount,
                execution.ExitCode,
                execution.TimedOut,
                relativeLogPath,
                execution.TopWarnings,
                execution.TopErrors);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.BuildCommandJsonPayload));
        }
        else
        {
            PrintBuildSummary(relativeTargetPath, relativeLogPath, execution, lockScope.Summary, buildLock.TotalWaitDuration);
        }

        return execution.ExitCode;
    }

    private static async Task<int> RunTestAsync(string[] args)
    {
        if (args.Length == 0 || HasHelpToken(args))
        {
            PrintTestHelp();
            return 0;
        }

        var parseResult = DotNetCommandOptions.Parse(args, Directory.GetCurrentDirectory());
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(options.TargetPath);
        var lockScope = BuildGraphLockScopeResolver.Resolve(repoRoot, options.TargetPath);
        var config = DnRelayConfigLoader.Load(repoRoot);
        var logsDirectory = DnRelayConfigLoader.ResolveLogsDirectory(repoRoot, config, options.LogsDirectoryOverride);
        var testResultsDirectory = Path.Combine(repoRoot, "artifacts", "test-results");
        Directory.CreateDirectory(logsDirectory);
        Directory.CreateDirectory(testResultsDirectory);

        var startTime = DateTimeOffset.Now;
        var logPath = Path.Combine(logsDirectory, $"test-{startTime:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.txt");
        var trxPath = Path.Combine(testResultsDirectory, $"test-{startTime:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.trx");
        var relativeLogPath = ToDisplayPath(repoRoot, logPath);
        var relativeTargetPath = Path.GetRelativePath(repoRoot, options.TargetPath);
        await using var logWriter = OpenLog(logPath);
        await WriteLogHeaderAsync(logWriter, "test", repoRoot, options.TargetPath, options.EnvironmentVariables);
        PrintCommandStarted("TEST", relativeTargetPath, "log", relativeLogPath, options.Json);

        await logWriter.WriteLineAsync($"# lock-scope: {lockScope.Summary}");
        await logWriter.FlushAsync();

        using var buildLock = await BuildLockSet.AcquireAsync(lockScope.LockPaths, "test", options.TargetPath, startTime, CancellationToken.None, lockScope.DisplayName);
        await logWriter.WriteLineAsync($"# lock-wait: {buildLock.TotalWaitDuration.TotalSeconds:F1}s");
        await logWriter.FlushAsync();
        var execution = await DotNetTestExecutor.ExecuteAsync(options, logWriter, logPath, trxPath, TimeoutExitCode);
        var relativeTrxPath = Path.GetRelativePath(repoRoot, execution.TrxPath);

        if (options.Json)
        {
            var payload = new TestCommandJsonPayload(
                "test",
                execution.ExitCode == 0 ? "succeeded" : execution.TimedOut ? "timed_out" : "failed",
                relativeTargetPath,
                execution.Duration.TotalSeconds,
                execution.TotalCount,
                execution.PassedCount,
                execution.FailedCount,
                execution.SkippedCount,
                execution.ErrorCount,
                execution.ExitCode,
                execution.TimedOut,
                relativeLogPath,
                relativeTrxPath,
                execution.TopFailures,
                execution.TopErrors);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.TestCommandJsonPayload));
        }
        else
        {
            PrintTestSummary(relativeTargetPath, relativeLogPath, relativeTrxPath, execution, lockScope.Summary, buildLock.TotalWaitDuration);
        }

        return execution.ExitCode;
    }

    private static async Task<int> RunRunAsync(string[] args)
    {
        if (HasHelpToken(args))
        {
            PrintRunHelp();
            return 0;
        }

        var parseResult = RunCommandOptions.Parse(args, Directory.GetCurrentDirectory());
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(options.ProjectPath ?? options.WorkingDirectory);
        var config = DnRelayConfigLoader.Load(repoRoot);
        var logsDirectory = DnRelayConfigLoader.ResolveLogsDirectory(repoRoot, config, options.LogsDirectoryOverride);
        Directory.CreateDirectory(logsDirectory);

        var startTime = DateTimeOffset.Now;
        var logPath = Path.Combine(logsDirectory, $"run-{startTime:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.txt");
        var targetPath = options.ProjectPath ?? options.WorkingDirectory;
        var relativeLogPath = ToDisplayPath(repoRoot, logPath);
        var relativeTargetPath = options.ProjectPath is null ? "." : Path.GetRelativePath(repoRoot, options.ProjectPath);

        await using var logWriter = OpenLog(logPath);
        await WriteLogHeaderAsync(logWriter, "run", repoRoot, targetPath, options.EnvironmentVariables);
        PrintCommandStarted("RUN", relativeTargetPath, "log", relativeLogPath, options.Json);

        if (DotNetRunExecutor.RequiresBuild(options))
        {
            var lockScope = BuildGraphLockScopeResolver.Resolve(repoRoot, targetPath);
            await logWriter.WriteLineAsync($"# lock-scope: {lockScope.Summary}");
            await logWriter.FlushAsync();

            using var buildLock = await BuildLockSet.AcquireAsync(lockScope.LockPaths, "run-build", targetPath, startTime, CancellationToken.None, lockScope.DisplayName);
            var buildStartInfo = DotNetRunExecutor.CreateBuildStartInfo(options);
            await logWriter.WriteLineAsync($"$ dotnet {string.Join(" ", buildStartInfo.ArgumentList.Select(QuoteIfNeeded))}");
            await logWriter.WriteLineAsync();
            await logWriter.FlushAsync();

            var buildPhase = await ProcessExecution.RunAsync(
                buildStartInfo,
                logWriter,
                options.Timeout,
                TimeoutExitCode,
                trackingOptions: new ProcessTrackingOptions(repoRoot, "run-build", targetPath));

            if (buildPhase.ExitCode != 0)
            {
                await logWriter.WriteLineAsync();
                await logWriter.WriteLineAsync($"# exit-code: {buildPhase.ExitCode}");
                await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
                await logWriter.WriteLineAsync($"# log: {logPath}");
                await logWriter.FlushAsync();

                PrintRunSummary(relativeTargetPath, relativeLogPath, new RunExecutionResult(buildPhase.ExitCode, buildPhase.TimedOut, buildPhase.Duration, [], options.RawOutput), lockScope.Summary, buildLock.TotalWaitDuration);
                return buildPhase.ExitCode;
            }
        }

        var execution = await DotNetRunExecutor.ExecuteAsync(options, logWriter, logPath, TimeoutExitCode);

        if (options.Json)
        {
            var payload = new RunCommandJsonPayload(
                "run",
                execution.ExitCode == 0 ? "succeeded" : execution.TimedOut ? "timed_out" : "failed",
                relativeTargetPath,
                execution.Duration.TotalSeconds,
                execution.ExitCode,
                execution.TimedOut,
                relativeLogPath,
                execution.OutputTail);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.RunCommandJsonPayload));
        }
        else
        {
            PrintRunSummary(relativeTargetPath, relativeLogPath, execution, null, null);
        }

        return execution.ExitCode;
    }

    private static async Task<int> RunBenchAsync(string[] args)
    {
        if (HasHelpToken(args))
        {
            PrintBenchHelp();
            return 0;
        }

        var parseResult = BenchCommandOptions.Parse(args, Directory.GetCurrentDirectory());
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var targetPath = options.ProjectPath ?? options.WorkingDirectory;
        var repoRoot = RepositoryRootLocator.Find(targetPath);
        var lockScope = BuildGraphLockScopeResolver.Resolve(repoRoot, targetPath);
        var config = DnRelayConfigLoader.Load(repoRoot);
        var logsDirectory = DnRelayConfigLoader.ResolveLogsDirectory(repoRoot, config, options.LogsDirectoryOverride);
        Directory.CreateDirectory(logsDirectory);

        var startTime = DateTimeOffset.Now;
        var logPath = Path.Combine(logsDirectory, $"bench-{startTime:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.txt");
        var relativeBenchLogPath = ToDisplayPath(repoRoot, logPath);
        var relativeBenchTargetPath = ResolveBenchDisplayTarget(repoRoot, targetPath);
        await using var logWriter = OpenLog(logPath);
        await WriteLogHeaderAsync(logWriter, "bench", repoRoot, targetPath, options.EnvironmentVariables);
        PrintCommandStarted("BENCH", relativeBenchTargetPath, "log", relativeBenchLogPath, options.Json);

        ProcessExecutionResult buildPhase;
        await logWriter.WriteLineAsync($"# lock-scope: {lockScope.Summary}");
        await logWriter.FlushAsync();

        TimeSpan benchBuildLockWaitDuration;
        using (var buildLock = await BuildLockSet.AcquireAsync(lockScope.LockPaths, "bench-build", targetPath, startTime, CancellationToken.None, lockScope.DisplayName))
        {
            benchBuildLockWaitDuration = buildLock.TotalWaitDuration;
            await logWriter.WriteLineAsync($"# lock-wait: {benchBuildLockWaitDuration.TotalSeconds:F1}s");
            await logWriter.FlushAsync();
            var buildStartInfo = DotNetBenchExecutor.CreateBuildStartInfo(options);
            await logWriter.WriteLineAsync($"$ dotnet {string.Join(" ", buildStartInfo.ArgumentList.Select(QuoteIfNeeded))}");
            await logWriter.WriteLineAsync();
            await logWriter.FlushAsync();

            buildPhase = await ProcessExecution.RunAsync(
                buildStartInfo,
                logWriter,
                options.Timeout,
                TimeoutExitCode,
                trackingOptions: new ProcessTrackingOptions(repoRoot, "bench-build", targetPath));
        }

        if (buildPhase.ExitCode != 0)
        {
            await logWriter.WriteLineAsync();
            await logWriter.WriteLineAsync($"# exit-code: {buildPhase.ExitCode}");
            await logWriter.WriteLineAsync($"# completed: {DateTimeOffset.Now:O}");
            await logWriter.WriteLineAsync($"# log: {logPath}");
            await logWriter.FlushAsync();

            PrintBenchSummary(relativeBenchTargetPath, relativeBenchLogPath, repoRoot, new BenchExecutionResult(buildPhase.ExitCode, buildPhase.TimedOut, buildPhase.Duration, DotNetBenchExecutor.GetDefaultArtifactsPath(options), null, [], []), lockScope.Summary, benchBuildLockWaitDuration);
            return buildPhase.ExitCode;
        }

        var benchmarkListResult = await DotNetBenchExecutor.ListBenchmarksAsync(options);
        var availableBenchmarks = benchmarkListResult.ExitCode == 0
            ? DotNetBenchExecutor.ParseBenchmarkList(benchmarkListResult.Output)
            : [];

        if (options.ListOnly)
        {
            if (options.Json)
            {
                var payload = new BenchCommandJsonPayload(
                    "bench",
                    benchmarkListResult.ExitCode == 0 ? "listed" : "failed",
                    relativeBenchTargetPath,
                    buildPhase.Duration.TotalSeconds,
                    benchmarkListResult.ExitCode,
                    false,
                    relativeBenchLogPath,
                    ToDisplayPath(repoRoot, DotNetBenchExecutor.GetDefaultArtifactsPath(options)),
                    null,
                    false,
                    availableBenchmarks,
                    [],
                    availableBenchmarks);

                Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.BenchCommandJsonPayload));
            }
            else
            {
                PrintBenchList(relativeBenchTargetPath, availableBenchmarks);
            }

            return benchmarkListResult.ExitCode;
        }

        if (!DotNetBenchExecutor.HasExplicitSelection(options) && availableBenchmarks.Count > 1)
        {
            if (options.Json)
            {
                var payload = new BenchCommandJsonPayload(
                    "bench",
                    "selection_required",
                    relativeBenchTargetPath,
                    buildPhase.Duration.TotalSeconds,
                    DotNetBenchExecutor.GetSelectionRequiredExitCode(),
                    false,
                    relativeBenchLogPath,
                    ToDisplayPath(repoRoot, DotNetBenchExecutor.GetDefaultArtifactsPath(options)),
                    null,
                    true,
                    availableBenchmarks,
                    [],
                    []);

                Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.BenchCommandJsonPayload));
            }
            else
            {
                PrintBenchSelectionRequired(relativeBenchTargetPath, availableBenchmarks);
            }

            return DotNetBenchExecutor.GetSelectionRequiredExitCode();
        }

        if (options.Selectors.Count > 0)
        {
            try
            {
                options = DotNetBenchExecutor.ApplySelection(options, availableBenchmarks);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }
        else if (!DotNetBenchExecutor.HasExplicitSelection(options) && availableBenchmarks.Count == 1)
        {
            try
            {
                options = DotNetBenchExecutor.ApplySelection(
                    new BenchCommandOptions
                    {
                        WorkingDirectory = options.WorkingDirectory,
                        ProjectPath = options.ProjectPath,
                        Configuration = options.Configuration,
                        BenchmarkArguments = options.BenchmarkArguments,
                        Selectors = [availableBenchmarks[0]],
                        ListOnly = options.ListOnly,
                        Timeout = options.Timeout,
                        Json = options.Json,
                        LogsDirectoryOverride = options.LogsDirectoryOverride,
                        EnvironmentVariables = options.EnvironmentVariables
                    },
                    availableBenchmarks);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        var benchRuntimeLockPath = CreateBenchRuntimeLockPath(repoRoot, options.ProjectPath ?? targetPath);
        using var benchLock = await BuildLock.AcquireAsync(benchRuntimeLockPath, "bench-run", targetPath, startTime, CancellationToken.None, "bench lock");
        var execution = await DotNetBenchExecutor.ExecuteAsync(options, logWriter, logPath, TimeoutExitCode);
        var relativeArtifactsPath = ToDisplayPath(repoRoot, execution.ArtifactsPath);

        if (options.Json)
        {
            var payload = new BenchCommandJsonPayload(
                "bench",
                execution.ExitCode == 0 ? "succeeded" : execution.TimedOut ? "timed_out" : "failed",
                relativeBenchTargetPath,
                execution.Duration.TotalSeconds,
                execution.ExitCode,
                execution.TimedOut,
                relativeBenchLogPath,
                relativeArtifactsPath,
                execution.Reason,
                false,
                [],
                execution.Highlights,
                execution.OutputTail);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.BenchCommandJsonPayload));
        }
        else
        {
            PrintBenchSummary(relativeBenchTargetPath, relativeBenchLogPath, repoRoot, execution, lockScope.Summary, benchBuildLockWaitDuration);
        }

        return execution.ExitCode;
    }

    private static async Task<int> RunKillAsync(string[] args)
    {
        if (args.Length == 0 || HasHelpToken(args))
        {
            PrintKillHelp();
            return 0;
        }

        var parseResult = KillCommandOptions.Parse(args);
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(Directory.GetCurrentDirectory());
        var result = await DnRelayProcessKiller.KillAsync(repoRoot, options.Selector);
        var status = GetKillStatus(result);

        if (options.Json)
        {
            var payload = new KillCommandJsonPayload(
                "kill",
                status,
                options.Selector,
                repoRoot,
                result.MatchedPids,
                result.KilledPids,
                result.AlreadyGonePids,
                result.FailedPids,
                result.RemovedStaleProcessIds,
                result.RemovedStaleLocks);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.KillCommandJsonPayload));
        }
        else
        {
            Console.WriteLine(status == "no_match" ? "KILL NO MATCH" : status == "failed" ? "KILL FAILED" : "KILL COMPLETED");
            Console.WriteLine($"selector: {options.Selector}");
            Console.WriteLine($"repo: {repoRoot}");
            Console.WriteLine($"matched: {result.MatchedPids.Count}");
            Console.WriteLine($"killed: {result.KilledPids.Count}");
            Console.WriteLine($"already gone: {result.AlreadyGonePids.Count}");
            if (result.RemovedStaleProcessIds.Count > 0 || result.RemovedStaleLocks.Count > 0)
            {
                Console.WriteLine($"cleanup: removed {result.RemovedStaleProcessIds.Count} stale process record(s), {result.RemovedStaleLocks.Count} stale lock record(s)");
            }

            PrintList("matched pids:", result.MatchedPids.Select(static pid => pid.ToString()).ToList());
            PrintList("killed pids:", result.KilledPids.Select(static pid => pid.ToString()).ToList());
            PrintList("already gone pids:", result.AlreadyGonePids.Select(static pid => pid.ToString()).ToList());
            PrintList("failed pids:", result.FailedPids.Select(static pid => pid.ToString()).ToList());
        }

        return status == "failed" ? 1 : 0;
    }

    private static async Task<int> RunStatsAsync(string[] args)
    {
        if (args.Length > 0 && HasHelpToken(args))
        {
            PrintStatsHelp();
            return 0;
        }

        var parseResult = StatsCommandOptions.Parse(args);
        if (!parseResult.Success)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            return 1;
        }

        var options = parseResult.Options!;
        var repoRoot = RepositoryRootLocator.Find(Directory.GetCurrentDirectory());
        var snapshots = await ProcessSnapshotProvider.GetSnapshotsAsync();
        var livePids = snapshots.Select(static snapshot => snapshot.Pid).ToHashSet();
        var removedStaleProcessIds = DnRelayProcessRegistry.RemoveStale(repoRoot, livePids);
        var removedStaleLocks = DnRelayLockRegistry.RemoveStale(repoRoot, livePids);
        var tracked = DnRelayProcessRegistry.ReadAll(repoRoot);
        var locks = DnRelayLockRegistry.ReadAll(repoRoot, livePids);

        var activeProcesses = tracked
            .Where(metadata => livePids.Contains(metadata.Pid))
            .OrderBy(static metadata => metadata.StartedAt)
            .ToList();

        if (options.Json)
        {
            var payload = new StatsCommandJsonPayload(
                "stats",
                "ok",
                repoRoot,
                activeProcesses.Select(process => new StatsProcessJsonEntry(
                    process.Pid,
                    process.Command,
                    ToDisplayPath(repoRoot, process.Target),
                    process.StartedAt.ToString("O"))).ToList(),
                locks.Select(lockInfo => new StatsLockJsonEntry(
                    lockInfo.Name,
                    lockInfo.Metadata.Pid,
                    lockInfo.IsLive ? "live" : "stale",
                    lockInfo.Metadata.Command,
                    ToDisplayPath(repoRoot, lockInfo.Metadata.Target),
                    lockInfo.Metadata.StartedAt.ToString("O"))).ToList(),
                removedStaleProcessIds,
                removedStaleLocks);

            Console.WriteLine(JsonSerializer.Serialize(payload, DnRelayJsonContext.Default.StatsCommandJsonPayload));
            return 0;
        }

        Console.WriteLine("STATS");
        Console.WriteLine($"repo: {repoRoot}");
        if (removedStaleProcessIds.Count > 0 || removedStaleLocks.Count > 0)
        {
            Console.WriteLine($"cleanup: removed {removedStaleProcessIds.Count} stale process record(s), {removedStaleLocks.Count} stale lock record(s)");
        }

        Console.WriteLine($"processes: {activeProcesses.Count}");
        foreach (var process in activeProcesses)
        {
            Console.WriteLine($"- id: {process.Pid}");
            Console.WriteLine($"  command: {process.Command}");
            Console.WriteLine($"  target: {ToDisplayPath(repoRoot, process.Target)}");
            Console.WriteLine($"  started: {process.StartedAt:O}");
        }

        Console.WriteLine($"locks: {locks.Count}");
        foreach (var lockInfo in locks)
        {
            Console.WriteLine($"- name: {lockInfo.Name}");
            Console.WriteLine($"  owner id: {lockInfo.Metadata.Pid}");
            Console.WriteLine($"  state: {(lockInfo.IsLive ? "live" : "stale")}");
            Console.WriteLine($"  command: {lockInfo.Metadata.Command}");
            Console.WriteLine($"  target: {ToDisplayPath(repoRoot, lockInfo.Metadata.Target)}");
            Console.WriteLine($"  started: {lockInfo.Metadata.StartedAt:O}");
        }

        return 0;
    }

    private static void PrintBuildSummary(string relativeTargetPath, string relativeLogPath, BuildExecutionResult execution, string? lockSummary, TimeSpan? lockWaitDuration)
    {
        Console.WriteLine(execution.ExitCode == 0 ? "BUILD SUCCEEDED" : execution.TimedOut ? "BUILD TIMED OUT" : "BUILD FAILED");
        Console.WriteLine($"project: {relativeTargetPath}");
        Console.WriteLine($"duration: {execution.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"warnings: {execution.WarningCount}");
        Console.WriteLine($"errors: {execution.ErrorCount}");
        Console.WriteLine($"log: {relativeLogPath}");
        PrintLockStats(lockSummary, lockWaitDuration);
        if (execution.TimedOut) Console.WriteLine("timeout: true");
        PrintList("top warnings:", execution.TopWarnings);
        PrintList("top errors:", execution.TopErrors);
    }

    private static void PrintTestSummary(string relativeTargetPath, string relativeLogPath, string relativeTrxPath, TestExecutionResult execution, string? lockSummary, TimeSpan? lockWaitDuration)
    {
        Console.WriteLine(execution.ExitCode == 0 ? "TEST SUCCEEDED" : execution.TimedOut ? "TEST TIMED OUT" : "TEST FAILED");
        Console.WriteLine($"project: {relativeTargetPath}");
        Console.WriteLine($"duration: {execution.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"total: {execution.TotalCount}");
        Console.WriteLine($"passed: {execution.PassedCount}");
        Console.WriteLine($"failed: {execution.FailedCount}");
        Console.WriteLine($"skipped: {execution.SkippedCount}");
        Console.WriteLine($"errors: {execution.ErrorCount}");
        Console.WriteLine($"log: {relativeLogPath}");
        Console.WriteLine($"trx: {relativeTrxPath}");
        PrintLockStats(lockSummary, lockWaitDuration);
        if (execution.TimedOut) Console.WriteLine("timeout: true");
        PrintList("top failures:", execution.TopFailures);
        PrintList("top errors:", execution.TopErrors);
    }

    private static void PrintRunSummary(string relativeTargetPath, string relativeLogPath, RunExecutionResult execution, string? lockSummary, TimeSpan? lockWaitDuration)
    {
        Console.WriteLine(execution.ExitCode == 0 ? "RUN SUCCEEDED" : execution.TimedOut ? "RUN TIMED OUT" : "RUN FAILED");
        Console.WriteLine($"project: {relativeTargetPath}");
        Console.WriteLine($"duration: {execution.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"exit code: {execution.ExitCode}");
        Console.WriteLine($"log: {relativeLogPath}");
        PrintLockStats(lockSummary, lockWaitDuration);
        if (execution.TimedOut) Console.WriteLine("timeout: true");
        if (execution.RawOutput) Console.WriteLine("raw output: true");
        PrintList("output tail:", execution.OutputTail);
    }

    private static void PrintBenchSummary(string relativeTargetPath, string relativeLogPath, string repoRoot, BenchExecutionResult execution, string? lockSummary, TimeSpan? lockWaitDuration)
    {
        Console.WriteLine(execution.ExitCode == 0 ? "BENCH SUCCEEDED" : execution.TimedOut ? "BENCH TIMED OUT" : "BENCH FAILED");
        Console.WriteLine($"project: {relativeTargetPath}");
        Console.WriteLine($"duration: {execution.Duration.TotalSeconds:F1}s");
        Console.WriteLine($"exit code: {execution.ExitCode}");
        Console.WriteLine($"log: {relativeLogPath}");
        Console.WriteLine($"artifacts: {ToDisplayPath(repoRoot, execution.ArtifactsPath)}");
        PrintBenchLockStats(lockSummary, lockWaitDuration);
        if (execution.TimedOut) Console.WriteLine("timeout: true");
        if (!string.IsNullOrWhiteSpace(execution.Reason)) Console.WriteLine($"reason: {execution.Reason}");
        PrintList("highlights:", execution.Highlights);
        PrintList("output tail:", execution.OutputTail);
    }

    private static void PrintBenchList(string relativeTargetPath, IReadOnlyList<string> availableBenchmarks)
    {
        Console.WriteLine("BENCH LIST");
        Console.WriteLine($"project: {relativeTargetPath}");
        PrintIndexedList("benchmarks:", availableBenchmarks);
    }

    private static void PrintBenchSelectionRequired(string relativeTargetPath, IReadOnlyList<string> availableBenchmarks)
    {
        Console.WriteLine("BENCH SELECTION REQUIRED");
        Console.WriteLine($"project: {relativeTargetPath}");
        PrintIndexedList("benchmarks:", availableBenchmarks);
        Console.WriteLine("next:");
        Console.WriteLine($"- dotnet dnrelay bench --project {relativeTargetPath} --select 0");
        Console.WriteLine($"- dotnet dnrelay bench --project {relativeTargetPath} --select {availableBenchmarks[0]}");
        Console.WriteLine($"- dotnet dnrelay bench --project {relativeTargetPath} -- --filter *SomeBenchmark*");
    }

    private static void PrintCommandStarted(string commandName, string relativeTargetPath, string detailLabel, string detailValue, bool json)
    {
        if (json)
        {
            return;
        }

        Console.WriteLine($"{commandName} STARTED");
        Console.WriteLine($"target: {relativeTargetPath}");
        Console.WriteLine($"{detailLabel}: {detailValue}");
    }

    private static async Task WriteLogHeaderAsync(StreamWriter logWriter, string command, string repoRoot, string targetPath, IReadOnlyDictionary<string, string> environmentVariables)
    {
        await logWriter.WriteLineAsync($"# dnrelay {command}");
        await logWriter.WriteLineAsync($"# started: {DateTimeOffset.Now:O}");
        await logWriter.WriteLineAsync($"# repo: {repoRoot}");
        await logWriter.WriteLineAsync($"# target: {targetPath}");
        await logWriter.WriteLineAsync("# encoding: utf-8");
        await logWriter.WriteLineAsync($"# dotnet-cli-ui-language: {GetDotNetCliUiLanguage(environmentVariables)}");
        if (environmentVariables.Count > 0)
        {
            await logWriter.WriteLineAsync($"# env-overrides: {string.Join(", ", environmentVariables.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}");
        }

        await logWriter.WriteLineAsync();
        await logWriter.FlushAsync();
    }

    private static StreamWriter OpenLog(string logPath)
        => new(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string ToDisplayPath(string repoRoot, string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path;
        }

        var relative = Path.GetRelativePath(repoRoot, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    private static string CreateBenchRuntimeLockPath(string repoRoot, string targetPath)
    {
        var locksDirectory = Path.Combine(DnRelayDirectory.Ensure(repoRoot), "locks");
        Directory.CreateDirectory(locksDirectory);
        var normalized = Path.GetFullPath(targetPath).ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..12].ToLowerInvariant();
        return Path.Combine(locksDirectory, $"bench-{hash}.lock");
    }

    private static void PrintList(string header, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        Console.WriteLine(header);
        foreach (var line in lines) Console.WriteLine($"- {line}");
    }

    private static void PrintIndexedList(string header, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        Console.WriteLine(header);
        for (var index = 0; index < lines.Count; index++)
        {
            Console.WriteLine($"- [{index}] {lines[index]}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("dnrelay");
        Console.WriteLine("Compact dotnet wrapper for AI agents and humans.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  build         Run dotnet build with compact summary and repo build lock");
        Console.WriteLine("  test          Run dotnet test with compact summary, repo build lock, and trx parsing");
        Console.WriteLine("  run           Run dotnet run with bounded output and build-then-run coordination");
        Console.WriteLine("  bench         Run BenchmarkDotNet via dotnet run -c Release with compact summary");
        Console.WriteLine("  stats         Show active dnrelay processes and lock owners for this repository");
        Console.WriteLine("  kill          Kill dnrelay-related processes for this repository");
        Console.WriteLine("  config        Manage repo-local dnrelay settings");
        Console.WriteLine();
        Console.WriteLine("Help:");
        Console.WriteLine("  dnrelay --help");
        Console.WriteLine("  dnrelay <command> --help");
    }

    private static void PrintBuildHelp()
    {
        Console.WriteLine("dnrelay build");
        Console.WriteLine("Run dotnet build and print a compact summary.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay build <target> [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <extra dotnet build args>]");
        Console.WriteLine("  dnrelay build --target <path> [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <extra dotnet build args>]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <target>      .csproj, .sln, .slnx, or directory to build");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --target      Explicit target path alias for the positional target");
        Console.WriteLine("  --json        Emit a final JSON object instead of text summary");
        Console.WriteLine("  --timeout     Harness timeout, for example 30s, 5m, or 1h");
        Console.WriteLine("  --logs-dir    Override the harness log directory");
        Console.WriteLine("  --env         Add or override a process environment variable");
        Console.WriteLine("  --env-file    Load environment variables from a file");
        Console.WriteLine("  --            Pass remaining arguments directly to dotnet build");
    }

    private static void PrintTestHelp()
    {
        Console.WriteLine("dnrelay test");
        Console.WriteLine("Run dotnet test and summarize trx results.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay test <target> [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <extra dotnet test args>]");
        Console.WriteLine("  dnrelay test --target <path> [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <extra dotnet test args>]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <target>      .csproj, .sln, .slnx, or directory to test");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --target      Explicit target path alias for the positional target");
        Console.WriteLine("  --json        Emit a final JSON object instead of text summary");
        Console.WriteLine("  --timeout     Harness timeout, for example 30s, 5m, or 1h");
        Console.WriteLine("  --logs-dir    Override the harness log directory");
        Console.WriteLine("  --env         Add or override a process environment variable");
        Console.WriteLine("  --env-file    Load environment variables from a file");
        Console.WriteLine("  --            Pass remaining arguments directly to dotnet test");
    }

    private static void PrintRunHelp()
    {
        Console.WriteLine("dnrelay run");
        Console.WriteLine("Run dotnet run with bounded output and optional pre-build coordination.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay run [--project <path>|--target <path>] [--no-build] [--raw] [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <app args>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project     Project to run. Defaults to current directory");
        Console.WriteLine("  --target      Alias for --project");
        Console.WriteLine("  --no-build    Skip dnrelay's coordinated build phase");
        Console.WriteLine("  --raw         Stream child stdout/stderr directly to the console");
        Console.WriteLine("  --json        Emit a final JSON object instead of text summary");
        Console.WriteLine("  --timeout     Harness timeout, for example 30s, 5m, or 1h");
        Console.WriteLine("  --logs-dir    Override the harness log directory");
        Console.WriteLine("  --env         Add or override a process environment variable");
        Console.WriteLine("  --env-file    Load environment variables from a file");
        Console.WriteLine("  --            Pass remaining arguments directly to the application");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  dnrelay run builds under the repo build lock when needed, then runs with --no-build.");
        Console.WriteLine("  Use top-level --no-build when outputs are already up to date.");
    }

    private static void PrintBenchHelp()
    {
        Console.WriteLine("dnrelay bench");
        Console.WriteLine("Run BenchmarkDotNet with compact progress, summary, and artifact pointers.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay bench [--project <path>|--target <path>] [--list] [--select <selector>] [-c|--configuration <configuration>] [--json] [--timeout <duration>] [--logs-dir <path>] [--env KEY=VALUE] [--env-file <path>] [-- <benchmark args>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --project           Benchmark project to run. Defaults to current directory");
        Console.WriteLine("  --target            Alias for --project");
        Console.WriteLine("  --select            Select a benchmark by index, class name, benchmark name, or filter text");
        Console.WriteLine("  --list              List discovered benchmarks and exit");
        Console.WriteLine("  -c, --configuration Build and run configuration. Default is Release");
        Console.WriteLine("  --json              Emit a final JSON object instead of text summary");
        Console.WriteLine("  --timeout           Harness timeout, for example 30s, 5m, or 1h");
        Console.WriteLine("  --logs-dir          Override the harness log directory");
        Console.WriteLine("  --env               Add or override a process environment variable");
        Console.WriteLine("  --env-file          Load environment variables from a file");
        Console.WriteLine("  --                  Pass remaining arguments directly to BenchmarkDotNet");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  bench builds under the repo build lock, then runs BenchmarkDotNet under a project-scoped bench lock.");
        Console.WriteLine("  artifacts points to the BenchmarkDotNet.Artifacts directory created by the benchmark project.");
        Console.WriteLine("  If multiple benchmarks are discovered and you did not pass --select or --filter, bench lists them and exits instead of entering interactive selection.");
    }

    private static void PrintKillHelp()
    {
        Console.WriteLine("dnrelay kill");
        Console.WriteLine("Kill dnrelay-related processes for the current repository.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay kill <id|*> [--json]");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dnrelay stats");
        Console.WriteLine("  dnrelay kill 12345");
        Console.WriteLine("  dnrelay kill *");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  kill uses process ids shown by `dnrelay stats`.");
        Console.WriteLine("  kill * targets all dnrelay-related processes associated with this repository.");
    }

    private static void PrintStatsHelp()
    {
        Console.WriteLine("dnrelay stats");
        Console.WriteLine("Show active dnrelay processes and lock owners for the current repository.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay stats [--json]");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  process ids shown here can be passed to `dnrelay kill <id>`.");
        Console.WriteLine("  lock entries show the current owner pid, command, target, and start time.");
    }

    private static string GetKillStatus(KillResult result)
    {
        if (result.FailedPids.Count > 0)
        {
            return "failed";
        }

        if (result.MatchedPids.Count > 0 || result.RemovedStaleProcessIds.Count > 0 || result.RemovedStaleLocks.Count > 0)
        {
            return "completed";
        }

        return "no_match";
    }

    private static void PrintConfigHelp()
    {
        Console.WriteLine("dnrelay config");
        Console.WriteLine("Manage repo-local dnrelay settings.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay config <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  logs          Create or update .dnrelay\\config.json with logsDir");
        Console.WriteLine();
        Console.WriteLine("Help:");
        Console.WriteLine("  dnrelay config --help");
        Console.WriteLine("  dnrelay config logs --help");
    }

    private static void PrintConfigLogsHelp()
    {
        Console.WriteLine("dnrelay config logs");
        Console.WriteLine("Create or update repo-local log destination config.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dnrelay config logs <path> [--force]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <path>         logsDir value to write into .dnrelay\\config.json");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --force        Overwrite an existing config file");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  Relative paths are resolved from the repository root when dnrelay later uses them.");
    }

    private static void PrintLockStats(string? lockSummary, TimeSpan? lockWaitDuration)
    {
        if (!string.IsNullOrWhiteSpace(lockSummary))
        {
            Console.WriteLine($"lock: {lockSummary}");
        }

        if (lockWaitDuration is { } wait)
        {
            Console.WriteLine($"lock wait: {wait.TotalSeconds:F1}s");
        }
    }

    private static void PrintBenchLockStats(string? lockSummary, TimeSpan? lockWaitDuration)
    {
        if (!string.IsNullOrWhiteSpace(lockSummary))
        {
            Console.WriteLine($"build lock: {lockSummary}");
        }

        if (lockWaitDuration is { } wait)
        {
            Console.WriteLine($"build lock wait: {wait.TotalSeconds:F1}s");
        }
    }

    private static string ResolveBenchDisplayTarget(string repoRoot, string targetPath)
    {
        var resolvedTargetPath = Path.GetFullPath(targetPath);
        if (File.Exists(resolvedTargetPath))
        {
            return Path.GetRelativePath(repoRoot, resolvedTargetPath);
        }

        if (!Directory.Exists(resolvedTargetPath))
        {
            return ToDisplayPath(repoRoot, resolvedTargetPath);
        }

        var candidateProjects = Directory.GetFiles(resolvedTargetPath, "*.csproj", SearchOption.TopDirectoryOnly);
        if (candidateProjects.Length == 1)
        {
            return Path.GetRelativePath(repoRoot, candidateProjects[0]);
        }

        return ToDisplayPath(repoRoot, resolvedTargetPath);
    }

    private static bool HasHelpToken(string[] args)
        => args.Any(IsHelpToken);

    private static bool IsHelpToken(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);

    private static string GetDotNetCliUiLanguage(IReadOnlyDictionary<string, string> environmentVariables)
        => environmentVariables.TryGetValue("DOTNET_CLI_UI_LANGUAGE", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "en-US (default)";
}

