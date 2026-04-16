namespace DnRelay.Tool.Models;

sealed record ToolProjectMetadata(
    string PackageId,
    string Version,
    bool PackAsTool,
    string VersionElementName,
    string? VersionSuffix,
    string DefaultBumpKind);
