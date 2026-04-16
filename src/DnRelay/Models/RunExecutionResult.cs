namespace DnRelay.Models;

sealed record RunExecutionResult(
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration,
    IReadOnlyList<string> OutputTail,
    bool RawOutput);
