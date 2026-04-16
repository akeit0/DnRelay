using System.Diagnostics;
using DnRelay.Tool.Models;

namespace DnRelay.Tool.Utilities;

static class SimpleProcess
{
    public static async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await standardOutput;
        var error = await standardError;
        var combined = string.IsNullOrEmpty(error) ? output : $"{output}{Environment.NewLine}{error}".Trim();
        return new ProcessRunResult(process.ExitCode, combined);
    }
}
