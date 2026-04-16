namespace DnRelay.Models;

sealed record StatsProcessJsonEntry(
    int Id,
    string Command,
    string Target,
    string Started);
