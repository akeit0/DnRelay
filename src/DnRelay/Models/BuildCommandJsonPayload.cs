namespace DnRelay.Models;

sealed record BuildCommandJsonPayload(
    string Command,
    string Status,
    string Project,
    double Duration,
    int Warnings,
    int Errors,
    int ExitCode,
    bool TimedOut,
    string Log,
    IReadOnlyList<string> TopWarnings,
    IReadOnlyList<string> TopErrors);
