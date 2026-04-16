namespace DnRelay.Models;

sealed record ProcessSnapshot(
    int Pid,
    int ParentPid,
    string Name,
    string CommandLine);
