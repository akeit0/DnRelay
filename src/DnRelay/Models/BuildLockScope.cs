namespace DnRelay.Models;

sealed record BuildLockScope(
    IReadOnlyList<string> LockPaths,
    string DisplayName,
    string Summary);
