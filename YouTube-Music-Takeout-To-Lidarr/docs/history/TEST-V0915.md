# v0.9.1.5 — Windows test plan

## Build and compile self-tests

From the extracted repository folder in PowerShell, run each only after the previous passes:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Launch **this version's** `publish\YouTubeMusicToLidarr.exe`. Confirm the UI shows `v0.9.1.5` at the top.

## Replay a saved baseline (NO full verification)

- Original Google Takeout ZIP (same as previous run)
- Song-by-Song Evidence: `musicbrainz-songs-audit (16).csv`
- Artist & Song Lists: `musicbrainz-artists-audit (16).csv`
- Optional Original Evidence: blank
- Import Previous Evidence.
- Use **Recover Artist IDs and ISRCs**. This includes the improved MusicBrainz targeted release stage.
- Export updated Song Audit, Artist Audit, Detailed Recovery Diagnostics and **All Candidate Scores and Album Evidence**.
- Run **Compare Lidarr Track & File Inventory**, then export Migration Readiness Preview.

## In the candidate scores CSV verify

- `v0915-release-discovery` entries exist for eligible albums, even for rejected candidates.
- Related editions or index-only candidates get `related-edition-needs-approval`, **not a recovered exact recording**.
- Release counts, number of browse pages and examined full tracklists are present in `Request HTTP statuses` and `Decision reason`.
- An incomplete or truncated release inventory gets `partial-release-inventory-review-only`.
- An existing contradictory MBID, different live/remix version or competing group does not get overwritten.
- No write operations were sent to Lidarr; all source audio file counts should remain unchanged.

## What to send back

Report build errors/warnings if any; provide Song and Artist Audit, Migration Readiness, Detailed Recovery Diagnostics, and All Candidate Scores and Album Evidence CSV. Record the final manual-review count and number of **new** song MBIDs (do not confuse cumulative recovery counts with new matches).
