namespace DnRelay.Models;

sealed record ProcessTrackingOptions(
    string RepoRoot,
    string Command,
    string Target);
