# v0.9.0.7 test guide

- Build & run `--self-test` first. New `AlbumGroupSelfTest` checks edition-alias hints, refusal to equate unrelated album titles, and group approval with **no** final download/search authorization.
- Reuse a completed MusicBrainz audit and the unchanged Google Takeout archive. The album planner requires Lidarr inventory, not fresh MusicBrainz verification.
- Inspect the album-group shortlist: multiple full-coverage albums must have **no** automatically selected recommendation. Deluxe-versus-standard titles must be marked as a possible edition difference and require review.
- Choose a complete album for one held cohort. The held count should fall by the number of songs in the cohort and the group approval count should rise by one. Download count must remain unchanged.
- Do not approve a candidate explicitly labeled `incomplete` (the button should reject it or disable selection).
- Click every main category count, test filtering artist, song, album and reason.
- Export `lidarr-album-review-groups.csv`, `lidarr-proposed-action-plan-NO-WRITES.csv`, `migration-readiness-preview.csv`, `lidarr-release-candidates.csv` and `lidarr-inventory-diagnostics.csv`.
- In action plan, every value under `Final search approved` and `Lidarr write executed` must be `NO`. Shared album plans should not request duplicate operations for each source song.
- A fresh **Lidarr comparison** resets the session-only selected groups. Restarting the app also clears these session decisions. Existing MusicBrainz audit results are not silently reused in fresh verification runs.
- If a failure happens, provide exact PowerShell compiler output and diagnostic CSV; don't repeat a two-hour verification to check compile fixes.
