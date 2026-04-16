namespace DnRelay.Models;

sealed record BuildExecutionResult(
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<string> TopWarnings,
    IReadOnlyList<string> TopErrors);
