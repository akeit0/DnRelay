namespace DnRelay.Models;

sealed record TrackedProcessMetadata(
    string Command,
    string Target,
    int Pid,
    DateTimeOffset StartedAt);
