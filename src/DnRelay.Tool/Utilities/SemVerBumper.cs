namespace DnRelay.Tool.Utilities;

static class SemVerBumper
{
    public static string Bump(string version, string bumpKind)
    {
        var buildMetadataIndex = version.IndexOf('+');
        var versionWithoutBuild = buildMetadataIndex >= 0 ? version[..buildMetadataIndex] : version;
        var buildMetadata = buildMetadataIndex >= 0 ? version[buildMetadataIndex..] : string.Empty;

        var suffixIndex = versionWithoutBuild.IndexOf('-');
        var core = suffixIndex >= 0 ? versionWithoutBuild[..suffixIndex] : versionWithoutBuild;
        var suffix = suffixIndex >= 0 ? versionWithoutBuild[suffixIndex..] : string.Empty;
        var parts = core.Split('.');
        if (parts.Length < 3)
        {
            throw new InvalidOperationException($"Unsupported version format: {version}");
        }

        if (!int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            throw new InvalidOperationException($"Unsupported version format: {version}");
        }

        switch (bumpKind.ToLowerInvariant())
        {
            case "patch":
                patch += 1;
                break;
            case "minor":
                minor += 1;
                patch = 0;
                break;
            case "major":
                major += 1;
                minor = 0;
                patch = 0;
                break;
            default:
                throw new InvalidOperationException($"Unsupported bump kind: {bumpKind}");
        }

        return $"{major}.{minor}.{patch}{suffix}{buildMetadata}";
    }
}
