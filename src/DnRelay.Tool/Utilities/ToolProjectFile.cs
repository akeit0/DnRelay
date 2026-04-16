using System.Xml.Linq;
using DnRelay.Tool.Models;

namespace DnRelay.Tool.Utilities;

static class ToolProjectFile
{
    public static ToolProjectMetadata? Load(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            var propertyGroups = document.Root?.Elements("PropertyGroup").ToArray() ?? [];
            var packageId = propertyGroups.Elements("PackageId").Select(static e => e.Value.Trim()).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageId = Path.GetFileNameWithoutExtension(projectPath);
            }

            var version = propertyGroups.Elements("Version").Select(static e => e.Value.Trim()).FirstOrDefault();
            var versionPrefix = propertyGroups.Elements("VersionPrefix").Select(static e => e.Value.Trim()).FirstOrDefault();
            var versionSuffix = propertyGroups.Elements("VersionSuffix").Select(static e => e.Value.Trim()).FirstOrDefault();
            var packAsTool = propertyGroups
                .Elements("PackAsTool")
                .Select(static e => bool.TryParse(e.Value.Trim(), out var parsed) && parsed)
                .FirstOrDefault();
            var defaultBumpKind = propertyGroups.Elements("ToolRefreshBump").Select(static e => e.Value.Trim()).FirstOrDefault();

            string versionElementName;
            if (!string.IsNullOrWhiteSpace(version))
            {
                versionElementName = "Version";
            }
            else if (!string.IsNullOrWhiteSpace(versionPrefix))
            {
                versionElementName = "VersionPrefix";
                version = string.IsNullOrWhiteSpace(versionSuffix) ? versionPrefix : $"{versionPrefix}-{versionSuffix}";
            }
            else
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            return new ToolProjectMetadata(
                packageId,
                version,
                packAsTool,
                versionElementName,
                string.IsNullOrWhiteSpace(versionSuffix) ? null : versionSuffix,
                string.IsNullOrWhiteSpace(defaultBumpKind) ? "patch" : defaultBumpKind);
        }
        catch
        {
            return null;
        }
    }

    public static void UpdateVersion(string projectPath, ToolProjectMetadata metadata, string version)
    {
        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var propertyGroups = document.Root?.Elements("PropertyGroup").ToArray() ?? [];
        var versionElement = propertyGroups.Elements(metadata.VersionElementName).FirstOrDefault();
        if (versionElement is null)
        {
            throw new InvalidOperationException($"No <{metadata.VersionElementName}> element found in {projectPath}.");
        }

        if (string.Equals(metadata.VersionElementName, "VersionPrefix", StringComparison.Ordinal))
        {
            var dashIndex = version.IndexOf('-');
            versionElement.Value = dashIndex >= 0 ? version[..dashIndex] : version;

            var versionSuffixElement = propertyGroups.Elements("VersionSuffix").FirstOrDefault();
            if (versionSuffixElement is not null)
            {
                versionSuffixElement.Value = dashIndex >= 0 ? version[(dashIndex + 1)..] : string.Empty;
            }
        }
        else
        {
            versionElement.Value = version;
        }

        document.Save(projectPath);
    }
}
