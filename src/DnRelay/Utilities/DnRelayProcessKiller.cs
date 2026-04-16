using System.Diagnostics;
using DnRelay.Models;

namespace DnRelay.Utilities;

static class DnRelayProcessKiller
{
    public static async Task<KillResult> KillAsync(string repoRoot, string selector)
    {
        var snapshots = await ProcessSnapshotProvider.GetSnapshotsAsync();
        var livePids = snapshots.Select(static snapshot => snapshot.Pid).ToHashSet();
        var removedStaleProcessIds = DnRelayProcessRegistry.RemoveStale(repoRoot, livePids).ToList();
        var removedStaleLocks = DnRelayLockRegistry.RemoveStale(repoRoot, livePids).ToList();
        var tracked = DnRelayProcessRegistry.ReadAll(repoRoot);
        var locks = DnRelayLockRegistry.ReadAll(repoRoot, livePids);
        var candidatePids = CollectCandidatePids(repoRoot, selector, tracked, locks, snapshots);
        var matchedPids = candidatePids.OrderBy(static pid => pid).ToList();

        var killed = new List<int>();
        var alreadyGone = new List<int>();
        var failed = new List<int>();
        foreach (var pid in candidatePids.OrderByDescending(static pid => pid))
        {
            if (!livePids.Contains(pid))
            {
                alreadyGone.Add(pid);
                DnRelayProcessRegistry.Unregister(repoRoot, pid);
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                killed.Add(pid);
            }
            catch
            {
                failed.Add(pid);
            }
            finally
            {
                DnRelayProcessRegistry.Unregister(repoRoot, pid);
            }
        }

        if (failed.Count > 0)
        {
            var remainingLivePids = (await ProcessSnapshotProvider.GetSnapshotsAsync()).Select(static snapshot => snapshot.Pid).ToHashSet();
            foreach (var pid in failed.ToArray())
            {
                if (remainingLivePids.Contains(pid))
                {
                    continue;
                }

                failed.Remove(pid);
                alreadyGone.Add(pid);
            }
        }

        var finalLivePids = await WaitForFinalLivePidsAsync(candidatePids);
        removedStaleProcessIds.AddRange(DnRelayProcessRegistry.RemoveStale(repoRoot, finalLivePids));
        removedStaleLocks.AddRange(DnRelayLockRegistry.RemoveStale(repoRoot, finalLivePids));

        removedStaleProcessIds = removedStaleProcessIds
            .Distinct()
            .OrderBy(static pid => pid)
            .ToList();
        removedStaleLocks = removedStaleLocks
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();
        alreadyGone.Sort();
        failed.Sort();
        killed.Sort();
        return new KillResult(selector, matchedPids, killed, alreadyGone, failed, removedStaleProcessIds, removedStaleLocks);
    }

    private static async Task<HashSet<int>> WaitForFinalLivePidsAsync(IReadOnlySet<int> candidatePids)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var livePids = (await ProcessSnapshotProvider.GetSnapshotsAsync()).Select(static snapshot => snapshot.Pid).ToHashSet();
            if (!candidatePids.Any(livePids.Contains))
            {
                return livePids;
            }

            await Task.Delay(200);
        }

        return (await ProcessSnapshotProvider.GetSnapshotsAsync()).Select(static snapshot => snapshot.Pid).ToHashSet();
    }

    private static IReadOnlySet<int> CollectCandidatePids(string repoRoot, string selector, IReadOnlyList<TrackedProcessMetadata> tracked, IReadOnlyList<ActiveLockInfo> locks, IReadOnlyList<ProcessSnapshot> snapshots)
    {
        var candidates = new HashSet<int>();
        var snapshotByParent = snapshots
            .GroupBy(static snapshot => snapshot.ParentPid)
            .ToDictionary(static group => group.Key, static group => group.Select(static snapshot => snapshot.Pid).ToList());

        var trackedRoots = ResolveRootPids(selector, tracked, locks, snapshots, repoRoot);

        foreach (var pid in trackedRoots)
        {
            candidates.Add(pid);
            AddDescendants(pid, snapshotByParent, candidates);
        }

        candidates.Remove(Environment.ProcessId);
        return candidates;
    }

    private static void AddDescendants(int pid, IReadOnlyDictionary<int, List<int>> snapshotByParent, HashSet<int> candidates)
    {
        if (!snapshotByParent.TryGetValue(pid, out var children))
        {
            return;
        }

        foreach (var childPid in children)
        {
            if (candidates.Add(childPid))
            {
                AddDescendants(childPid, snapshotByParent, candidates);
            }
        }
    }

    private static IReadOnlyList<int> ResolveRootPids(string selector, IReadOnlyList<TrackedProcessMetadata> tracked, IReadOnlyList<ActiveLockInfo> locks, IReadOnlyList<ProcessSnapshot> snapshots, string repoRoot)
    {
        if (string.Equals(selector, "*", StringComparison.Ordinal))
        {
            var all = new HashSet<int>(tracked.Select(static item => item.Pid));
            foreach (var lockInfo in locks)
            {
                all.Add(lockInfo.Metadata.Pid);
            }

            foreach (var snapshot in snapshots)
            {
                if (BelongsToRepo(repoRoot, snapshot))
                {
                    all.Add(snapshot.Pid);
                }
            }

            return all.ToList();
        }

        if (TryParsePid(selector, out var pid))
        {
            return [pid];
        }

        return [];
    }

    private static bool TryParsePid(string selector, out int pid)
    {
        var normalized = selector.StartsWith("p:", StringComparison.OrdinalIgnoreCase)
            ? selector[2..]
            : selector;
        return int.TryParse(normalized, out pid);
    }

    private static bool BelongsToRepo(string repoRoot, ProcessSnapshot snapshot)
        => (snapshot.CommandLine ?? string.Empty).Contains(repoRoot, StringComparison.OrdinalIgnoreCase);
}
