namespace DnRelay.Models;

sealed record BenchExecutionResult(
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    string ArtifactsPath,
    string? Reason,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> OutputTail);
