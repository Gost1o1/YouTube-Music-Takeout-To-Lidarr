# Test v0.9.0.8 — Do not repeat the 10-hour MusicBrainz scan

1. Extract the source ZIP into a new folder, not over an older version.
2. Open PowerShell in that folder and run one command at a time:

   `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`

   **Stop if it reports any errors.** If successful:

   `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`

   Stop if any self-test fails. If successful:

   `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`

3. Run `publish\YouTubeMusicToLidarr.exe`. Keep the console open.
4. Select the same original Google Takeout ZIP you used previously and connect to Lidarr.
5. At Step 6 **import prior evidence**, do NOT start a fresh MusicBrainz verification:
   - Song-by-Song Evidence: `musicbrainz-songs-audit (8).csv`
   - Artist & Song Lists: `musicbrainz-artists-audit (8).csv`
   - Optional original evidence: leave blank unless the new import reports that important earlier evidence is missing. Do not mix mismatched exports.
6. Click **Compare Lidarr Track & File Inventory**. This re-fetches Lidarr metadata (read-only); it does not rerun MusicBrainz, Apple or Deezer.
7. Export **Migration Readiness Preview**, **All Lidarr Release Candidates**, **Album Review Groups**, and **Proposed Action Plan — NO WRITES**.
8. Compare the new counts to the previous 609 Manual Review / 1,896 To Be Added baseline. The offline replay suggests 349 / 2,156 if the underlying Lidarr records and imported evidence are identical. Results can differ as metadata changes.
9. Test selecting one *edition mismatch* group (e.g., a Deluxe versus standard album). Verify it says `planning only`, still discloses the recording-ID difference, and does not search/download music. Export the action plan again.

**Important:** Rows with `To Be Added` and `recording version unverified` are merely album-search proposals, not automatic exact-track matches. None is approved for an actual Lidarr search in this version. If you see an unexpected count, share the new CSVs (but not the Lidarr API key).

## Maintainer-only offline benchmark (optional Python 3)

`python tools/offline_replay.py --readiness "migration-readiness-preview (4).csv" --candidates "lidarr-release-candidates (1).csv"`

The command only reads the CSVs, makes no HTTP requests, and prints a prospective category-count comparison. It requires neither Lidarr nor .NET. Do not commit these private CSVs to GitHub.
