# v0.9.1.0 validation report

Source basis: v0.9.0.9 archive supplied in the current conversation. All changes made to actual C# and AppUI source. The repository remains READ-ONLY with respect to Lidarr.

## Completed offline checks

- Reviewed v0.9.0.9 `migration-readiness-preview (6).csv`, `lidarr-release-candidates (3).csv`, and `lidarr-proposed-action-plan-NO-WRITES (2).csv`.
- Confirmed input contains 2,505 song rows and 332 Manual Review rows.
- Conservative explicit EP suffix matching proposed EXACTLY five otherwise manual-review rows:
  - Skrillex — Summit (Bangarang EP)
  - Skrillex — Bangarang (Bangarang EP)
  - Skrillex — Breakn' a Sweat (Bangarang EP)
  - Green Day — 21 Guns (21 Guns EP)
  - Krewella — Alive (Play Hard EP)
- Tested rejecting unrelated albums (`Bangarang EP` vs `Triple Vinyl Box Set`) and generic title `EP`.
- Counted 1,690 source songs with independently supported Lidarr track IDs and no reported file, grouped in 1,216 distinct Lidarr album IDs. These are PROPOSALS, not verified audio downloads.
- UI JavaScript parse via Node.js v22 passed.
- C# lexical delimiter/static structure checks passed on all 9 source files.
- Added C# in-project self-tests for EP decisions, multiple candidate recordings per album, staging atomicity, invalidation and isolation from disputed records. These have NOT been executed without a .NET SDK.
- Confirmed ZIP integrity and checksums at packaging time.

## What was NOT tested

- Native `dotnet build`, self-test execution, and self-contained publish: .NET SDK is not installed in this environment, and the download host is inaccessible.
- The Windows web UI in a real browser, or live connection to the user's Lidarr server.
- Any actual Lidarr search/download/artist write. These are intentionally NOT implemented.

## User's next action

Build in Windows, run `--self-test`, import the existing aligned audit CSVs, run the read-only Lidarr comparison, and export both `lidarr-album-staging-queue-NO-WRITES.csv` and `lidarr-manual-review-triage.csv`. Do not run another all-library MusicBrainz scan.

Predicted manual-review count: approximately 327 (332 minus five explicit EP variants), subject to the next actual Lidarr comparison. No new MusicBrainz recording-ID verification is claimed.
