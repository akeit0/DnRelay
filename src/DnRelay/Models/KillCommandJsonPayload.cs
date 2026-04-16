namespace DnRelay.Models;

sealed record KillCommandJsonPayload(
    string Command,
    string Status,
    string Selector,
    string Repo,
    IReadOnlyList<int> MatchedPids,
    IReadOnlyList<int> KilledPids,
    IReadOnlyList<int> AlreadyGonePids,
    IReadOnlyList<int> FailedPids,
    IReadOnlyList<int> RemovedStaleProcessIds,
    IReadOnlyList<string> RemovedStaleLocks);
