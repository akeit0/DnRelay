using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DnRelay.Models;

namespace DnRelay.Utilities;

static class ProcessSnapshotProvider
{
    public static async Task<IReadOnlyList<ProcessSnapshot>> GetSnapshotsAsync()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? await GetWindowsSnapshotsAsync()
            : await GetPosixSnapshotsAsync();

    private static async Task<IReadOnlyList<ProcessSnapshot>> GetWindowsSnapshotsAsync()
    {
        var startInfo = new ProcessStartInfo("powershell")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,CommandLine | ConvertTo-Json -Compress");

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            return ParseWindowsJson(document.RootElement);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ProcessSnapshot> ParseWindowsJson(JsonElement root)
    {
        var results = new List<ProcessSnapshot>();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                TryAddWindowsSnapshot(item, results);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            TryAddWindowsSnapshot(root, results);
        }

        return results;
    }

    private static void TryAddWindowsSnapshot(JsonElement item, List<ProcessSnapshot> results)
    {
        if (!item.TryGetProperty("ProcessId", out var pidElement) ||
            !item.TryGetProperty("ParentProcessId", out var parentPidElement))
        {
            return;
        }

        var name = item.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
        var commandLine = item.TryGetProperty("CommandLine", out var commandLineElement) ? commandLineElement.GetString() ?? string.Empty : string.Empty;
        results.Add(new ProcessSnapshot(pidElement.GetInt32(), parentPidElement.GetInt32(), name, commandLine));
    }

    private static async Task<IReadOnlyList<ProcessSnapshot>> GetPosixSnapshotsAsync()
    {
        var startInfo = new ProcessStartInfo("ps")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-ax");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("pid=");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("ppid=");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("comm=");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add("args=");

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        var results = new List<ProcessSnapshot>();
        foreach (var line in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split([' '], 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 ||
                !int.TryParse(parts[0], out var pid) ||
                !int.TryParse(parts[1], out var parentPid))
            {
                continue;
            }

            results.Add(new ProcessSnapshot(pid, parentPid, parts[2], parts[3]));
        }

        return results;
    }
}
