---
name: release
description: Cut a DotLens release end to end. Write the changelog section, push to main, wait for green CI, confirm with the maintainer, start the Release workflow, then check what it published on GitHub and NuGet.
argument-hint: "[version, such as v1.1.0 or v1.1.0-rc.1]"
disable-model-invocation: true
---

You are cutting a DotLens release. `RELEASING.md` describes the process and `.github/workflows/release.yml` carries it out. Follow the steps below in order.

The version to release: `$ARGUMENTS`. If it is empty, propose one in step 1.

## Hard rules

- **Starting the Release workflow publishes.** The workflow has no draft and no approval gate: one run tags the commit, creates the GitHub release and pushes the package to NuGet.org. NuGet never accepts a version twice, and a pushed version can only be unlisted, not deleted. So start it only after the maintainer has seen the version and the notes and said go, in step 6.
- **Release only a commit on `main` whose CI passed.** `release.yml` does not check CI, so you must.
- **Nothing irreversible without asking.** Never delete or move a tag, delete or edit a published release, unlist a NuGet version, force-push, or re-run a failed release without the maintainer's go-ahead.
- **No nib IDs in public text.** The nib store is private. `CHANGELOG.md`, the release notes and the release commit hold no `dotlens-xxxx` reference. The notes also ship inside the package as its release notes.
- **Halt on any error.** When a `git`, `gh`, `dotnet` or workflow step fails, stop and report the cause. Don't retry blindly or work around a refusal.

## 1. Version

Use the argument if it is `vX.Y.Z` or `vX.Y.Z-<suffix>`, such as `v1.1.0-rc.1`. Otherwise read the latest tag (`git tag --sort=-v:refname | head -1`), the `## Unreleased` section of `CHANGELOG.md` and the commits since that tag, propose the next semantic version, and confirm it with the maintainer. A version with a `-` is a pre-release.

Check that the version is free. Both commands print nothing:

```sh
git ls-remote --tags origin "refs/tags/<version>"
gh release view <version> 2>/dev/null
```

For a stable release, find the milestone nib named after the version (`nibs list -t milestone`). When it still has open children, list them and ask the maintainer whether to release anyway.

## 2. Sync

```sh
git fetch origin && git -C .nibs pull
git status --short
git log --oneline origin/main..main
```

Be on `main` with a clean tree, and not behind `origin/main`. When local `main` holds commits that `origin/main` lacks, list them and confirm with the maintainer that they belong in this release. If the tree is dirty or `main` is behind, stop and ask: don't stash, rebase or merge on your own.

## 3. Documentation

Run `/decaf-quality:coherence-audit` over what a release publishes: `README.md` (it is the package's readme on NuGet), `RELEASING.md`, and the instructions the server sends every client, `src/DotLens/Infrastructure/ServerInstructions.cs`. Fix what it finds, or record with the maintainer why a finding ships as it is.

## 4. Changelog (stable releases only)

A pre-release needs no changelog section: the workflow publishes "Pre-release build from <commit>." as its notes. Skip to step 5.

For a stable release, move the entries under `## Unreleased` into a new section `## <version>` below it, the heading form the earlier releases use, and leave `## Unreleased` empty above. Then read the commits since the last tag (`git log --oneline <last tag>..main`) and the closure summaries of the nibs closed under the version's milestone, and add an entry for each user-facing change that no entry covers. Read every entry as a user would:

- Each entry says what changed for someone who uses DotLens through an MCP client: which tool, and what it now does. Not how the code changed.
- `### Added`, `### Changed` and `### Fixed` group the entries. A fix to a feature that is new in this release is no fix: fold it into the feature's entry.
- American English, and no nib IDs: `grep -nE 'dotlens-[a-z0-9]{4}\b' CHANGELOG.md` prints nothing.

The workflow finds the section with `grep "^## <version>"` and takes everything up to the next `## ` heading, so no other heading may start with the version (a `## <version>-rc.1` section would). Check both, then print the notes the workflow will publish:

```sh
grep -c "^## <version>" CHANGELOG.md          # 1
awk '/^## <version>/{f=1;next} /^## /{if(f)exit} f' CHANGELOG.md
```

## 5. Commit, push and wait for CI

Commit what steps 3 and 4 changed in one commit, such as `Release DotLens 1.1.0`, then `git push origin main`. When nothing changed, push the commits from step 2, if any.

Find the CI run of the pushed commit. The run appears a few seconds after the push, so retry the lookup a few times:

```sh
SHA=$(git rev-parse HEAD)
gh run list --workflow ci.yml --commit "$SHA" --json databaseId,status,conclusion
```

CI builds and tests on Windows and Ubuntu and checks formatting and `THIRD-PARTY-NOTICES.txt`; a run takes about 5 to 15 minutes. Wait for it with `gh run watch <id> --exit-status`. A wait that hits a timeout is no CI failure: read the result itself.

```sh
gh run view <id> --json status,conclusion,jobs --jq '{status, conclusion, jobs: [.jobs[] | {name, conclusion}]}'
```

Anything but `success` stops the release. Report the failed jobs with `gh run view <id> --log-failed | tail -60`.

## 6. Confirm, then start the release

Show the maintainer, and ask them to confirm:

- the version, and whether it is a pre-release;
- the commit (`git rev-parse --short HEAD`) and its CI run's URL;
- for a stable release, the notes that step 4 printed;
- that starting the run publishes to GitHub and NuGet.org at once, with no way to replace the NuGet version afterwards.

Start the run only after they say go:

```sh
gh workflow run release.yml --ref main -f version=<version>
gh run list --workflow release.yml --limit 1 --json databaseId,status,url
```

## 7. Wait for the run to end

Wait with `gh run watch <id> --exit-status`, and read the result with `gh run view <id>` as in step 5. A run takes a few minutes.

When it fails, report the failed step and its log, then check what it left behind, and ask the maintainer before you change anything:

```sh
git ls-remote --tags origin "refs/tags/<version>"
gh release view <version> --json isDraft,assets
curl -s https://api.nuget.org/v3-flatcontainer/dotlens.mcp/index.json | jq -r '.versions[]' | grep -Fx "<version without its v>"
```

The workflow deletes the tag when a step fails, but a GitHub release or a NuGet push that already happened stays. A re-run fails at its tag check while the tag exists.

## 8. Check what the release published

Run each check and report the result. Stop at the first wrong one and report it.

```sh
gh release view <version> --json isDraft,isPrerelease,body,assets \
  --jq '{isDraft, isPrerelease, assets: [.assets[].name] | sort, body}'
```

- The release is no draft, and `isPrerelease` matches the version's suffix.
- Its assets are `DotLens.MCP.<version without its v>.nupkg` and `.snupkg`.
- A stable release's body equals the notes step 4 printed.

NuGet.org takes some minutes to validate and index a pushed package. Check every few minutes, for up to half an hour, until the version appears:

```sh
curl -s https://api.nuget.org/v3-flatcontainer/dotlens.mcp/index.json | jq -r '.versions[]' | grep -Fx "<version without its v>"
```

Last, install the published tool into a new directory in the scratchpad, from a working directory outside the repository, and run it. Its version starts with the version without its `v`:

```sh
dotnet tool install DotLens.MCP --version <version without its v> --tool-path <dir>
<dir>/dotlens --version
```

## 9. Report and tidy up

Report the release's URL, the NuGet page (`https://www.nuget.org/packages/DotLens.MCP/<version without its v>`), and the result of each check in step 8. Fetch the tag the workflow pushed: `git fetch --tags`.

When the notes need a correction after the release, fix the section in `CHANGELOG.md` first, then ask the maintainer before you change the published notes with `gh release edit <version> --notes-file <file>`. The notes inside the NuGet package cannot change.

For a stable release, when all children of the version's milestone nib are closed, offer to close it with a summary that names the release, then commit and push the nib store from within `.nibs/`.
