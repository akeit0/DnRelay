namespace DnRelay.Models;

sealed record KillResult(
    string Selector,
    IReadOnlyList<int> MatchedPids,
    IReadOnlyList<int> KilledPids,
    IReadOnlyList<int> AlreadyGonePids,
    IReadOnlyList<int> FailedPids,
    IReadOnlyList<int> RemovedStaleProcessIds,
    IReadOnlyList<string> RemovedStaleLocks);
