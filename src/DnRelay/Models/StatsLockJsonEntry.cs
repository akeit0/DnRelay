namespace DnRelay.Models;

sealed record StatsLockJsonEntry(
    string Name,
    int OwnerId,
    string State,
    string Command,
    string Target,
    string Started);
