namespace DnRelay.Models;

sealed record StatsCommandJsonPayload(
    string Command,
    string Status,
    string Repo,
    IReadOnlyList<StatsProcessJsonEntry> Processes,
    IReadOnlyList<StatsLockJsonEntry> Locks,
    IReadOnlyList<int> RemovedStaleProcessIds,
    IReadOnlyList<string> RemovedStaleLocks);
