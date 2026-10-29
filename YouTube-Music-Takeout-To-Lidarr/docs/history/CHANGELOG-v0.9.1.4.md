# v0.9.1.4 — Targeted single-song release corroboration

## Why this release
The previous album-corroboration phase required at least two unresolved titles from the same source album. The v0.9.1.3 audit includes 169 source rows with unresolved song identities but validated artists; many are isolated singles and could not enter that phase.

## Changes
- New stage D in the existing identifier recovery workflow: checks up to 160 eligible artist/album cohorts (148 in the current test fixture), prioritized by prior Apple/Deezer corroboration.
- Uses a validated artist MusicBrainz ID and exact-normalized album/release-group search, then reads **every release only when the release group has at most four releases and the inventory is complete**.
- Requires actual recording-level credited artist MBIDs and version-compatible track titles. Only one compatible recording MBID may remain for automatic provisional song identity recovery.
- Existing conflicting recording MBIDs remain untouched. Ambiguous release groups, incomplete provider responses, alternate editions and competing recordings produce diagnostics, not automatic matches.
- Improves version-conflict diagnostics and populates the previously unused `VersionConflict` field.
- Retains candidate-scoring CSV and detailed recovery diagnostic exports, with a new `targeted-song-to-release` stage.
- Updates the recovery panel wording to explain the single-song and whole-album checks.
- Self-tests now include incomplete inventory, competing releases/recordings and original-MBID conflict rejection.
- No Lidarr writes, searches, downloads or automatic final approvals.

## Evidence limits
A recovered recording MBID is evidence of an artist-and-song identity. It does **not** establish that the YouTube Music audio is the same exact master. Release title and version mismatch safeguards remain in force. Complete means complete **within the retrieved release group**, not the artist's full MusicBrainz catalog.

## What this does not do
It does not automatically resolve Lidarr-specific release decisions for the 38 songs whose source artist/song identity is already supported, or the 68 unresolved artist identities. Those retain their previous review protections. The artist candidate pass remains available.

## Source evidence baseline
`musicbrainz-songs-audit (14).csv`, `musicbrainz-artists-audit (14).csv` and `lidarr-manual-review-triage (3).csv` were used for the offline replay. The source package does not distribute the user's private CSVs or API keys.
