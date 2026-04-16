namespace DnRelay.Models;

sealed record LockMetadata(int Pid, string Command, string Target, DateTimeOffset StartedAt);
