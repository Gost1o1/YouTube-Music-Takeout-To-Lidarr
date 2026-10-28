# v0.9.1.7 — Guided manual review and persistent review notebook

- Adds **Guided manual review** panel to Step 6, using current imported song/artist audit and MusicBrainz candidate scores, optionally incorporating Lidarr candidate-recording IDs after read-only comparison. A bounded, searchable list displays artist/song/source album, reasons, candidate IDs, and supporting review-only evidence.
- Imports optional prior **All Candidate Scores and Album Evidence CSV** with strict source-row/artist alignment; historical candidate scores are marked review-only and cannot alter artist/recording confidence. This avoids re-running expensive lookups to populate the new review picker.
- Saves **review notes and candidate-for-planning choices** in `Documents/MusicTakeoutToolkit/review-decisions-v1.json`. The selection must be among IDs actually present in the current review item; saved decisions remain *review-only* and do not alter an audit's confidence, record IDs, or eligibility to search/download.
- Source- and row-fingerprints prevent song-review decisions from applying silently to changed source rows or album editions; missing candidates are explicitly marked stale.
- Saves album-group planning choices on explicit approval. A new run can *manually* reapply saved choices only after verifying current source artist, source album, and the new Lidarr group's complete album coverage. A prior album decision is never replayed automatically.
- Exports a portable review-decision CSV with evidence key, source, note, and clear **NO** columns for artist/recording verification and Lidarr search/write authorization. The local JSON is the durable state; CSV is a review/export copy.
- Adds one bounded extra MusicBrainz retry for terminal **503** from recording-first fallback after the existing retry logic. Responses are still review-only. No exhaustive rescanning is triggered.
- Adds offline review-key, artist/recording candidate validation, JSON roundtrip and album-key self-tests to the existing `--self-test` suite.
- Updates application version, Windows CI change triggers, and documentation; preserves the original ten-step interface, source importer, all prior CSV exports, and *no Lidarr writes*.

**Limitations:** This release does not automatically resolve the remaining 297 songs or 68 artist identities. The existing exact-master confidence and release-version safety rules remain unchanged. Native .NET build and runtime tests are pending Windows confirmation.
