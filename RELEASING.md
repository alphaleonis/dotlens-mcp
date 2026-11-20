# Releasing DotLens

Releases are automated via the [Release workflow](.github/workflows/release.yml), triggered manually from GitHub Actions.

## Prerequisites

- A [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) policy on NuGet.org for repository owner `alphaleonis`, repository `dotlens-mcp` and workflow file `release.yml`, with no environment
- `NUGET_USER` repository secret holding the NuGet.org profile name (not the email address) of the policy's owner
- All changes merged to `main`
- CI passing on `main`

## Stable Release (e.g., v1.0.0)

1. **Add a changelog entry** in `CHANGELOG.md` with a heading matching the version:
   ```markdown
   ## v1.0.0

   Description of changes...
   ```
   The workflow validates this entry exists and extracts everything between the version heading and the next `## ` as release notes.

2. **Commit and push** the changelog update to `main`.

3. **Trigger the release**:
   - Go to Actions > Release > Run workflow
   - Enter the version (e.g., `v1.0.0`)
   - Click "Run workflow"

## Pre-release (e.g., v1.0.0-alpha.1)

Pre-releases skip changelog validation. Release notes are auto-generated as "Pre-release build from {commit}."

1. **Trigger the release**:
   - Go to Actions > Release > Run workflow
   - Enter the version with a suffix (e.g., `v1.0.0-alpha.1`, `v1.0.0-rc.1`)
   - Click "Run workflow"

## What the Workflow Does

1. Validates version format (`vX.Y.Z` or `vX.Y.Z-suffix`)
2. Checks the git tag doesn't already exist
3. For stable releases: verifies `CHANGELOG.md` has a matching `## vX.Y.Z` entry
4. Extracts release notes from the changelog
5. Creates and pushes a git tag
6. Builds, tests, and packs the NuGet package
7. Creates a GitHub Release with the `.nupkg` and `.snupkg` attached
8. Pushes to NuGet.org with a one-hour API key that Trusted Publishing issues for the run
9. On failure: cleans up the tag automatically

## Version Format

- Stable: `v1.0.0`, `v1.2.3`
- Pre-release: `v1.0.0-alpha.1`, `v1.0.0-beta.2`, `v1.0.0-rc.1`

The `v` prefix is stripped for the NuGet package version (NuGet uses `1.0.0`, not `v1.0.0`).
