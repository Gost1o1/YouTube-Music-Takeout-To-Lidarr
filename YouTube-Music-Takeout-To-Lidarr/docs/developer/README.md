# For contributors and maintainers

The public [README](../../../README.md) has download and launch instructions.
This folder holds the build, testing and release-maintenance information.

## Project layout

| Folder/file | Purpose |
| --- | --- |
| `src/` | C# application and matching logic |
| `AppUI/` | Local browser interface; publish output must preserve this folder |
| `tools/` | Offline checks and the one-time GitHub cleanup helper |
| `docs/history/` | Old changelogs, validation notes and package manifests |
| `.github/workflows/build-windows.yml` (repository root) | Windows compilation, self-test and downloadable CI artifact |
| `.github/workflows/publish-windows-release.yml` (repository root) | **Manually** publish a GitHub Release download |
| `BUILD-AND-RUN.ps1` | Compile, self-test, publish and launch on Windows |

The `.csproj` sits inside `YouTube-Music-Takeout-To-Lidarr/` with the source.
GitHub Actions workflows stay at the **repository root** and run build commands
with that nested project directory as the working directory. SDK-style .NET
projects compile the `.cs` files under `src/` automatically.

## Local development on Windows

Install the .NET 8 SDK, then open PowerShell inside the
`YouTube-Music-Takeout-To-Lidarr` **project folder** (not the repository root):

```powershell
powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1
```

Or build and test without launching:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
node tools/offline_v091155_review_progress_provenance.js
node tools/offline_v091156_lidarr_import.js
node tools/offline_v091158_checkpoint_safety.js
```

The workflow also checks the other offline JavaScript regression fixtures.
After moving the C# files into `src/`, run the new GitHub Actions build once
to confirm the layout on a Windows runner.

## Publishing a user-facing Windows download

The build workflow's **Artifacts** are development builds and may require
a GitHub account to download. GitHub **Releases** are the recommended public
download destination.

1. Wait for **Build and smoke-test Windows .NET** to pass on `main`.
2. In GitHub **Actions**, select **Publish Windows download**.
3. Click **Run workflow** and enter a new version tag, for example `v0.9.1.15.8`.
4. Leave **prerelease** checked while this application remains a preview.
5. Wait for the workflow to build, test, and upload `YouTubeMusicToLidarr-win-x64.zip` to the new Release.
6. Confirm the ZIP contains both `YouTubeMusicToLidarr.exe` and `AppUI/` and launches on Windows.

No release is created by ordinary commits. A tag must be new; the workflow
does not overwrite existing releases. The README's **Download** button leads
to the Releases list, including prereleases. Users can download the ZIP without
building the source themselves.

## First-time GitHub repository cleanup

See [repository layout](REPOSITORY_LAYOUT.md) and
[one-time cleanup instructions](GITHUB-CLEANUP.md) when converting an existing
flat GitHub checkout to this nested source layout.

## Import actions and safety limits

All scan, matching, manual review, metadata fallback, and migration-preview actions
remain read-only for Lidarr. Only an explicit click on **Start Lidarr Import**
on the final step may send writes. The final choice is:

- **Add to Lidarr Only:** add independently verified artist MusicBrainz IDs not
  present in Lidarr, then monitor selected Lidarr albums backed by verified
  track evidence. New artists are added unmonitored to avoid selecting an
  entire discography.
- **Add and Search:** do the same and queue `AlbumSearch` commands for those
  selected, existing Lidarr albums. A search is for a *whole album*, so it
  may acquire music not in the user's Takeout. A separate affirmative checkbox
  is required. No searches are sent for newly added artists.

The importer does not create a Lidarr track, album or audio file from source-only
metadata, manual selections or ambiguous evidence. A successful HTTP write
means Lidarr accepted the action; downloads and file imports are asynchronous
and must be checked in Lidarr. The import results CSV records successes and
failures. Writes are not retried automatically because a timeout may follow
a successfully accepted request. The background activity panel polls Lidarr's
command queue (which also includes unrelated library tasks).

Import-specific local API endpoints: `lidarr/import/options`, `/start`,
`/status`, `/background`, `/results.csv`; all under `/api/` and protected by
the existing local session token. Preview and staging are not write endpoints.

Do not describe the package as a complete source-to-audio importer or as a
single-file EXE with no supporting files; the `AppUI` folder is still required.

Additional historical test plans: [TESTING.md](TESTING.md).
