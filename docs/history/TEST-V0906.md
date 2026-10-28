# v0.9.0.6 test sequence (no repeat 2-hour verification)

1. Unpack this complete source ZIP into a *new* folder (not over an old build). Run from that folder:

   ```powershell
   dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
   ```

2. **Only if the build succeeds**, run:

   ```powershell
   dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
   ```

3. **Only if self-tests pass**, publish:

   ```powershell
   dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
   ```

4. Run `publish\YouTubeMusicToLidarr.exe` while keeping its PowerShell/terminal open. Select the **same original Google Takeout ZIP** and connect to your *same local Lidarr instance*.
5. Import `musicbrainz-songs-audit (6).csv` into **Song-by-Song Evidence**, `musicbrainz-artists-audit (6).csv` into **Artist & Song Lists**, and optionally `musicbrainz-songs-audit (5).csv` into **Original Song Evidence**. Do **not** press fresh MusicBrainz verification or re-run the independent provider checks.
6. Click **Compare Lidarr Track & File Inventory**. Wait until the artist count finishes. This operation is read-only and uses scoped Lidarr API requests.
7. Export **Migration Readiness Preview (CSV)** and **All Lidarr Release Candidates (CSV)**; keep **Inventory Diagnostics (CSV)** in case requests fail.
8. Check that artist counts add up to source artist count, song counts add up to 2,505, **To Be Added + Manual Review + Already in Library = YouTube Music songs**, and songs marked `requires-release-selection-before-search` have no automatically chosen Lidarr track ID.
9. Compare a handful of source albums against the proposed Lidarr album titles, particularly one of the 666 formerly duplicate-track cases, before any future write behavior is implemented. If anything is wrong, share all three exported CSVs (with sensitive details removed if desired).

**Important:** This release does **not** start downloads, add artists, alter monitored status, or write to Lidarr. Since you confirmed that no audio has been downloaded, zero file-backed songs is expected.
