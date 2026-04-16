namespace DnRelay.Models;

sealed record ProcessExecutionResult(
    int ExitCode,
    bool TimedOut,
    TimeSpan Duration);
