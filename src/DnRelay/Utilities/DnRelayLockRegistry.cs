using System.Text.Json;
using DnRelay.Models;
using DnRelay.Serialization;

namespace DnRelay.Utilities;

static class DnRelayLockRegistry
{
    public static IReadOnlyList<ActiveLockInfo> ReadAll(string repoRoot, IReadOnlySet<int>? livePids = null)
    {
        var locksDirectory = Path.Combine(DnRelayDirectory.Ensure(repoRoot), "locks");
        if (!Directory.Exists(locksDirectory))
        {
            return [];
        }

        var results = new List<ActiveLockInfo>();
        foreach (var metadataPath in Directory.GetFiles(locksDirectory, "*.lock.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize(File.ReadAllText(metadataPath), DnRelayJsonContext.Default.LockMetadata);
                if (metadata is null)
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(metadataPath));
                var lockPath = Path.Combine(locksDirectory, $"{name}.lock");
                var isLive = livePids?.Contains(metadata.Pid) ?? true;
                results.Add(new ActiveLockInfo(name, lockPath, metadataPath, metadata, isLive));
            }
            catch
            {
            }
        }

        return results.OrderBy(static item => item.Name, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<string> RemoveStale(string repoRoot, IReadOnlySet<int> livePids)
    {
        var removed = new List<string>();
        var locksDirectory = Path.Combine(DnRelayDirectory.Ensure(repoRoot), "locks");

        foreach (var lockInfo in ReadAll(repoRoot, livePids))
        {
            if (lockInfo.IsLive)
            {
                continue;
            }

            if (TryDeleteFile(lockInfo.MetadataPath) | TryDeleteFile(lockInfo.LockPath))
            {
                removed.Add(lockInfo.Name);
            }
        }

        foreach (var lockPath in Directory.GetFiles(locksDirectory, "*.lock", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(lockPath);
            var metadataPath = Path.Combine(locksDirectory, $"{name}.lock.json");
            if (File.Exists(metadataPath))
            {
                continue;
            }

            if (TryDeleteFile(lockPath))
            {
                removed.Add(name);
            }
        }

        removed.Sort(StringComparer.Ordinal);
        return removed.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
