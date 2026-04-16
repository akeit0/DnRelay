# Releasing

Tags publish both NuGet tool packages through nuget.org trusted publishing.

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The release workflow is `.github/workflows/nuget-publish.yml`.

Required GitHub repository settings:

- create an environment named `nuget`
- create a repository variable named `NUGET_USER` with the nuget.org profile name that owns the packages

Required nuget.org trusted publishing policy:

- Repository Owner: the GitHub owner
- Repository: this GitHub repository name
- Workflow File: `nuget-publish.yml`
- Environment: `nuget`

The tag controls the package version. Both `v1.2.3` and `1.2.3` are accepted. The workflow passes the tag version to pack as `PackageVersion` and `Version`, so the project files do not need to be edited just to publish a tagged release.
