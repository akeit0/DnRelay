namespace DnRelay.Models;

sealed record TestExecutionResult(
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    int TotalCount,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    int ErrorCount,
    string TrxPath,
    IReadOnlyList<string> TopFailures,
    IReadOnlyList<string> TopErrors);
