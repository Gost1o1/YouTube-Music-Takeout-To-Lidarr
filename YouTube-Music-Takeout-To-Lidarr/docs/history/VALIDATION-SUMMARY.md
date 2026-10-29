# v0.9.0.7 — Validation summary

## What was checked in the authoring environment

- Unpacked the complete v0.9.0.6 source and preserved all existing source, UI, and release workflow assets.
- Reviewed the five user-supplied v0.9.0.6 CSV exports. There are 2,505 source songs, 951 source artist-credit strings, 130 known songs still awaiting a release choice, and 593 manual-review songs in the previous comparison.
- The 130 release-choice rows cluster into 77 unique source artist+album combinations; an offline replay suggests 22 groups with a unique related album-name/edition candidate (not 22 proven edition matches), 48 groups with multiple complete candidate albums, 6 with no complete candidate, and 1 with unique complete group coverage despite a different album title. These are advisory, not automatic approvals.
- Confirmed JavaScript parses with Node.js and output source ZIP integrity checks pass.
- Added native C# self-tests to check album matching and plan-only selection without Lidarr write authorization; **these self-tests could not be executed in this environment.**
- Confirmed no new Lidarr write methods were introduced (new routes query in-memory inventory or export CSV, or update session-only proposed selections).
- Updated GitHub Windows CI workflow to compile and run the enhanced self-tests when the source is pushed to a repository.

## Not checked

- **The .NET SDK/compiler is not available in this environment.** C# has not been compiled or run, so syntax/static checks are not a substitute for Windows `dotnet build`.
- No live Lidarr API call, MusicBrainz lookup, or actual audio download/import was performed here.
- No change was made to the user's remote GitHub repository.

## Approval semantics

"Approve release for planning only" updates the current in-memory plan to identify a Lidarr album and track ID. It does not trigger a Lidarr search, modify monitoring, download anything, or permanently save user decisions. The proposed action CSV explicitly marks **Final search approved = NO** and **Lidarr write executed = NO** for every source row.
