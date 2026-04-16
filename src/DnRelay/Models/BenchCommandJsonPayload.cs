namespace DnRelay.Models;

sealed record BenchCommandJsonPayload(
    string Command,
    string Status,
    string Project,
    double Duration,
    int ExitCode,
    bool TimedOut,
    string Log,
    string Artifacts,
    string? Reason,
    bool SelectionRequired,
    IReadOnlyList<string> AvailableBenchmarks,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> OutputTail);
