namespace DnRelay.Models;

sealed record TestCommandJsonPayload(
    string Command,
    string Status,
    string Project,
    double Duration,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    int Errors,
    int ExitCode,
    bool TimedOut,
    string Log,
    string Trx,
    IReadOnlyList<string> TopFailures,
    IReadOnlyList<string> TopErrors);
