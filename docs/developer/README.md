# For contributors and maintainers

The public [README](../../README.md) has download and launch instructions.
This folder holds the build, testing and release-maintenance information.

## Project layout

| Folder/file | Purpose |
| --- | --- |
| `src/` | C# application and matching logic |
| `AppUI/` | Local browser interface; publish output must preserve this folder |
| `tools/` | Offline checks and the one-time GitHub cleanup helper |
| `docs/history/` | Old changelogs, validation notes and package manifests |
| `.github/workflows/build-windows.yml` | Windows compilation, self-test and downloadable CI artifact |
| `.github/workflows/publish-windows-release.yml` | **Manually** publish a GitHub Release download |
| `BUILD-AND-RUN.ps1` | Compile, self-test, publish and launch on Windows |

The `.csproj` stays at the repository root. SDK-style .NET projects compile
the `.cs` files under `src/` automatically.

## Local development on Windows

Install the .NET 8 SDK, then open PowerShell in the project root:

```powershell
powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1
```

Or build and test without launching:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
node tools/offline_v091155_review_progress_provenance.js
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
3. Click **Run workflow** and enter a new version tag, for example `v0.9.1.15.4`.
4. Leave **prerelease** checked while this application remains a preview.
5. Wait for the workflow to build, test, and upload `YouTubeMusicToLidarr-win-x64.zip` to the new Release.
6. Confirm the ZIP contains both `YouTubeMusicToLidarr.exe` and `AppUI/` and launches on Windows.

No release is created by ordinary commits. A tag must be new; the workflow
does not overwrite existing releases. The README's **Download** button leads
to the Releases list, including prereleases. Users can download the ZIP without
building the source themselves.

## First-time GitHub repository cleanup

See [one-time cleanup instructions](GITHUB-CLEANUP.md) if your GitHub `main`
still contains the old C# files and version-by-version notes at its root.

## Safety limits

The current program is read-only for Lidarr: there are no actual artist adds,
album searches, downloads, or destructive writes. Source-metadata filing
only prepares local records for later manual import. Do not describe the
package as a completed music importer or as a single-file EXE with no supporting
files; the `AppUI` folder is still required.

Additional historical test plans: [TESTING.md](TESTING.md).
