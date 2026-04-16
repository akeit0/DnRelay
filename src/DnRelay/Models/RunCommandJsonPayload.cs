namespace DnRelay.Models;

sealed record RunCommandJsonPayload(
    string Command,
    string Status,
    string Project,
    double Duration,
    int ExitCode,
    bool TimedOut,
    string Log,
    IReadOnlyList<string> OutputTail);
