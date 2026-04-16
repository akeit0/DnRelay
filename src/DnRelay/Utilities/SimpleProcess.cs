using System.Diagnostics;
using DnRelay.Models;

namespace DnRelay.Utilities;

static class SimpleProcess
{
    public static async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environmentVariables = null, bool applyDotNetCliDefaults = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OutputEncoding.Utf8,
            StandardErrorEncoding = OutputEncoding.Utf8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (applyDotNetCliDefaults)
        {
            DotNetCliEnvironmentDefaults.Apply(startInfo, environmentVariables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
        else if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
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
