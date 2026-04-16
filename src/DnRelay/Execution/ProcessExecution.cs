using System.Diagnostics;
using System.Text;
using DnRelay.Models;
using DnRelay.Utilities;

namespace DnRelay.Execution;

static class ProcessExecution
{
    public static async Task<ProcessExecutionResult> RunAsync(
        ProcessStartInfo startInfo,
        StreamWriter logWriter,
        TimeSpan? timeout,
        int timeoutExitCode,
        Action<string>? onLine = null,
        Action<TimeSpan>? onHeartbeat = null,
        TimeSpan? heartbeatInterval = null,
        bool forwardConsoleInput = false,
        ProcessTrackingOptions? trackingOptions = null)
    {
        startInfo.StandardOutputEncoding ??= OutputEncoding.Utf8;
        startInfo.StandardErrorEncoding ??= OutputEncoding.Utf8;

        var sync = new object();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = timeout is { } timeoutValue ? new CancellationTokenSource(timeoutValue) : null;
        using var heartbeatCts = new CancellationTokenSource();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                outputClosed.TrySetResult();
                return;
            }

            HandleLine(eventArgs.Data);
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                errorClosed.TrySetResult();
                return;
            }

            HandleLine(eventArgs.Data);
        };

        process.Start();
        if (trackingOptions is not null)
        {
            DnRelayProcessRegistry.Register(trackingOptions, process.Id);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var inputForwardTask = forwardConsoleInput && startInfo.RedirectStandardInput
            ? ForwardConsoleInputAsync(process)
            : null;

        var heartbeatTask = RunHeartbeatLoopAsync(
            onHeartbeat,
            stopwatch,
            heartbeatCts.Token,
            heartbeatInterval ?? TimeSpan.FromSeconds(10));

        var timedOut = false;

        try
        {
            if (timeoutCts is null)
            {
                await process.WaitForExitAsync();
            }
            else
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            if (trackingOptions is not null)
            {
                DnRelayProcessRegistry.Unregister(trackingOptions.RepoRoot, process.Id);
            }
        }

        if (!process.HasExited)
        {
            await process.WaitForExitAsync();
        }

        try
        {
            if (startInfo.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }
        }
        catch
        {
        }

        await Task.WhenAll(outputClosed.Task, errorClosed.Task, heartbeatTask);
        stopwatch.Stop();

        return new ProcessExecutionResult(
            timedOut ? timeoutExitCode : process.ExitCode,
            timedOut,
            stopwatch.Elapsed);

        void HandleLine(string line)
        {
            lock (sync)
            {
                logWriter.WriteLine(line);
                logWriter.Flush();
            }

            onLine?.Invoke(line);
        }
    }

    private static Task ForwardConsoleInputAsync(Process process)
        => Task.Run(async () =>
        {
            try
            {
                while (!process.HasExited)
                {
                    var line = await Console.In.ReadLineAsync();
                    if (line is null)
                    {
                        break;
                    }

                    await process.StandardInput.WriteLineAsync(line);
                    await process.StandardInput.FlushAsync();
                }
            }
            catch
            {
            }
        });

    private static async Task RunHeartbeatLoopAsync(Action<TimeSpan>? onHeartbeat, Stopwatch stopwatch, CancellationToken cancellationToken, TimeSpan interval)
    {
        if (onHeartbeat is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    onHeartbeat(stopwatch.Elapsed);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
