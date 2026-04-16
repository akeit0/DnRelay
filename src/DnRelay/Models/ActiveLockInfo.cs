namespace DnRelay.Models;

sealed record ActiveLockInfo(
    string Name,
    string LockPath,
    string MetadataPath,
    LockMetadata Metadata,
    bool IsLive);
