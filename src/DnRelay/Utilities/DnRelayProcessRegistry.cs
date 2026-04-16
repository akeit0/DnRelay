using System.Text;
using System.Text.Json;
using DnRelay.Models;
using DnRelay.Serialization;

namespace DnRelay.Utilities;

static class DnRelayProcessRegistry
{
    public static void Register(ProcessTrackingOptions options, int pid)
    {
        var metadata = new TrackedProcessMetadata(options.Command, options.Target, pid, DateTimeOffset.Now);
        var directory = EnsureRegistryDirectory(options.RepoRoot);
        var path = Path.Combine(directory, $"{pid}.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(metadata, DnRelayJsonContext.Default.TrackedProcessMetadata),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Unregister(string repoRoot, int pid)
    {
        var path = Path.Combine(EnsureRegistryDirectory(repoRoot), $"{pid}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static IReadOnlyList<TrackedProcessMetadata> ReadAll(string repoRoot)
    {
        var directory = EnsureRegistryDirectory(repoRoot);
        var results = new List<TrackedProcessMetadata>();

        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize(File.ReadAllText(path), DnRelayJsonContext.Default.TrackedProcessMetadata);
                if (metadata is not null)
                {
                    results.Add(metadata);
                }
            }
            catch
            {
            }
        }

        return results;
    }

    public static IReadOnlyList<int> RemoveStale(string repoRoot, IReadOnlySet<int> livePids)
    {
        var removed = new List<int>();
        foreach (var metadata in ReadAll(repoRoot))
        {
            if (livePids.Contains(metadata.Pid))
            {
                continue;
            }

            Unregister(repoRoot, metadata.Pid);
            removed.Add(metadata.Pid);
        }

        removed.Sort();
        return removed;
    }

    private static string EnsureRegistryDirectory(string repoRoot)
    {
        var dnRelayDirectory = DnRelayDirectory.Ensure(repoRoot);
        var pidsDirectory = Path.Combine(dnRelayDirectory, "pids");
        Directory.CreateDirectory(pidsDirectory);
        return pidsDirectory;
    }
}
