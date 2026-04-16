using System.Text.Json;
using DnRelay.Models;
using DnRelay.Serialization;

namespace DnRelay.Utilities;

static class DnRelayConfigLoader
{
    public static DnRelayConfig Load(string repoRoot)
    {
        var dnRelayDirectory = DnRelayDirectory.Ensure(repoRoot);
        var configPath = Path.Combine(dnRelayDirectory, "config.json");
        if (!File.Exists(configPath))
        {
            return new DnRelayConfig(null);
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize(json, DnRelayJsonContext.Default.DnRelayConfig) ?? new DnRelayConfig(null);
        }
        catch
        {
            return new DnRelayConfig(null);
        }
    }

    public static string ResolveLogsDirectory(string repoRoot, DnRelayConfig config, string? commandLineLogsDir)
    {
        var configured = string.IsNullOrWhiteSpace(commandLineLogsDir) ? config.LogsDir : commandLineLogsDir;
        var path = string.IsNullOrWhiteSpace(configured) ? Path.Combine(".dnrelay", "logs") : configured;
        return Path.GetFullPath(path, repoRoot);
    }
}
