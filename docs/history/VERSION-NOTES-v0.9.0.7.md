# v0.9.0.7 — Album-cohort review and safe action planning

## Why this version exists

In v0.9.0.6, the 2,505 source-song rows were sorted as 0 audio files present, 1,912 proposals, and 593 manual reviews. Another 130 **recognized** songs were held for album/release selection. This version tackles those 130 by reviewing related songs **together**, instead of forcing song-by-song selection. It does not automatically erase MusicBrainz conflicts or pretend an edition and its standard release are equivalent.

## Features implemented

- **Album-level release cohorts:** group held songs by source primary artist + album; collect all candidate Lidarr album IDs and count corroborated MusicBrainz recording+title matches per album.
- **Explainable recommendations:** exact normalized album titles, possible edition/suffix variants (e.g., “Awake (Deluxe Edition)” versus “Awake”), and unique full-cohort coverage generate recommendations, but do **not** automatically approve anything. Ambiguous multiple full-coverage albums remain unresolved.
- **Group-level selection:** select one Lidarr album for **all** held songs in a cohort only when it has exactly one corroborated track per song. If not, selection is blocked with an explanation. Session-only approvals affect a local plan, not Lidarr itself.
- **Click-to-inspect categories:** open a searchable artist/song list for YouTube Music, Already in Library, To Be Added, or Manual Review. Rows carry the actual source names, album and reason.
- **Two new CSV exports:** `lidarr-album-review-groups.csv` (one row per candidate album with coverage, suggestions, title relationship, approval and reasons); `lidarr-proposed-action-plan-NO-WRITES.csv` (all source rows, chosen IDs or hold reason, and duplicated album-search plans consolidated by Lidarr album ID).
- **Existing CSV exports retained:** audit, migration readiness, inventory diagnostics, release candidates.
- **No networking for recommendations:** cohort calculation uses only the Lidarr inventory that the comparison already fetched. No MusicBrainz rescan is needed.
- **No Lidarr writes:** no artist or album creation, monitoring changes, search requests, download commands or audio acquisition. An approval is strictly **release choice for future planning**, not final search/download approval.

## Design influences / research

MusicBrainz Picard recommends comparing an album/cluster and examining alternate releases rather than relying on a single track alone. Picard also documents a minimum margin between best and second-best match to avoid false confidence. Beets' autotagger similarly uses a strong recommendation threshold and a gap threshold and considers albums and unmatched tracks. Lidarr uses MusicBrainz release-group/album metadata, so a familiar edition name in YouTube Takeout may not map to an identically titled local Lidarr album. We preserve uncertainties rather than choosing on name similarity alone.

- https://picard-docs.musicbrainz.org/en/latest/workflows/workflow_album.html
- https://picard-docs.musicbrainz.org/en/latest/config/options_matching.html
- https://beets.readthedocs.io/en/v2.9.0/reference/config.html
- https://lidarr.audio/docs/api/

## Test on Windows

1. Extract the ZIP. `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`.
2. `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`.
3. `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`.
4. Open the EXE. Use the same Takeout ZIP and v0.9.0.6 song/artist audit exports (the `(7)` files), OR reuse earlier `(6)` exports with the `(5)` song evidence baseline if needed. Do not click Begin Fresh Verification.
5. Connect to your unchanged Lidarr, import audits and use **Compare Lidarr Track & File Inventory**.
6. Click a category tile and test search; inspect album-review groups, choose a completely covered album for a safe test, and export both new CSVs plus the updated migration-readiness CSV.
7. Confirm **no Lidarr API writes occur** and your unchanged 0 downloaded music files remain 0.

## Regression expectations

From the user's v0.9.0.6 exported candidate CSV, 130 held songs form approximately 77 source-artist/album groups. An offline replay suggests approximately 22 groups have a unique, plausibly related album-title/edition candidate; many others have competing full-coverage releases or incomplete support. These are **suggestions**, not 22 validated new matches. The review decision count can fall when the user selects groups; the 593 genuinely uncertain manual-review songs are intentionally not auto-reclassified.

## Verification limitations

This package has been statically inspected and the browser JavaScript validated. No native .NET SDK is installed in this authoring environment and the Lidarr server is on the user's computer. A successful build and functional API behavior must be verified on Windows. Do not use this source to operate on a production library without that validation.
