namespace DnRelay.Models;

sealed record TrxSummary(
    int TotalCount,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    IReadOnlyList<string> FailedTests);
