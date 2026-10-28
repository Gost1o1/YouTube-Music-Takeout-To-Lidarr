# v0.9.1.15.4 — Exact-title automatic metadata filing and navigation repair

- Before Song Review appears, the bounded catalog preflight identifies complete
  source entries with zero candidates or confirmed matching song titles, including
  one trailing parenthetical title qualifier. These are filed as **source metadata
  for later manual import**, not as verified MusicBrainz matches.
- A single viable candidate with a confirmed catalog title is similarly filed on
  completion of display metadata. Missing/inaccessible catalog identity is never
  inferred from the original song title or an Apple sample; Retry remains available.
- Previous revisits the actual earlier review key even if the server reorders the
  queue or a later automatic filing removes entries in between.
- New offline regression test covers Two Steps From Hell, Miley Cyrus, League of
  Legends, parenthetical song versions, failed lookups and back navigation.
- Windows source ZIP includes the full build script and CI test wiring. No Lidarr
  writes or media downloads were added. Native .NET build must run on Windows CI.

See `CHANGELOG-v0.9.1.15.4.md` and `TEST-V091154.md`.

---

# v0.9.1.15.3 — Song Review fallback and progression

Complete no-candidate songs now preserve Takeout metadata automatically, while
incomplete entries prompt for missing fields and advance after manual save.
Song candidates are compared by recording title, and lookup failures are
retryable. The matching audit and Lidarr staging remain read-only.
See `CHANGELOG-v0.9.1.15.3.md` and `VALIDATION-v0.9.1.15.3.md`.

---

# v0.9.1.15.2 — Migration Preview and review reconciliation

Fixed missing inventory loading in Migration Preview, added visibility of
saved review selections and existing Lidarr track metadata, and stopped
collapsing distinct MusicBrainz IDs that share a preview. Automatic filing
now runs before Manual Review, without mutating the live queue. No Lidarr
writes/downloads have been added. See `CHANGELOG-v0.9.1.15.2.md` and
`VALIDATION-v0.9.1.15.2.md`. Native Windows compilation remains pending.

---

# v0.9.1.13.1 — Build hotfix

Fixed three `CS1009` invalid C# string escapes in `ManualFallbackV912.cs`. Use `BUILD-AND-RUN.ps1` or the commands in `TEST-V091131.md` for the full Windows build, self-tests, publication, and launch. No matching or Lidarr-write behavior changed. Native Windows compilation remains unverified in the packaging environment.

---

## Current release — v0.9.1.11

See `CHANGELOG-v0.9.1.11.md` for wizard-wide refresh recovery, `TEST-V0921.md` for Windows verification, and `VALIDATION-v0.9.1.11.md` for test limitations.

## Current release — v0.9.1.11

See `CHANGELOG-v0.9.1.11.md` for wizard-wide refresh recovery, `TEST-V0921.md` for Windows verification, and `VALIDATION-v0.9.1.11.md` for test limitations.

# Latest: v0.9.1.9

Fresh, automatically triggered unified evidence pipeline with bounded in-run MusicBrainz caching, shared rate limiting, provider-aware retries and simplified progress screen. See `CHANGELOG-v0.9.1.9.md` for exact functionality and limitations. **Read-only Lidarr:** final import screen prepares a local plan, not a real Lidarr import. Windows compilation and provider runs have not been performed in the source packaging environment.

---

# Latest update: v0.9.1.7

The existing ten-step app now supports durable, *read-only* guided review notes and candidate-for-planning choices, saved in the user's Documents folder across EXE upgrades. Album-group choices can be re-applied manually after current inventory evidence validates the same album. Adds a bounded 503 retry for recording-first review-only searches. **No automatic Lidarr additions, searches, downloads, or MusicBrainz MBID promotions arise from review notes.** See `CHANGELOG-v0.9.1.7.md` and `TEST-V0917.md`.

---

# v0.9.1.6 — MusicBrainz search parser correction

Fixes successful release searches incorrectly marked incomplete because JSON search returns `count`, unlike browse responses. Adds cautious paged search, recording-first review-only diagnostics, and missing Candidate Scores CSV button. Windows native build is not verified here. All Lidarr operations remain read-only. See `TEST-V0916.md` and `CHANGELOG-v0.9.1.6.md`.

---

# v0.9.1.5 — Safer, broader release discovery

Release-group direct title lookup, alias/release title lookup, direct release index, and bounded artist release-group browse. Paginated release browsing no longer assumes four releases. Incomplete inventories and related-but-not-identical editions remain review-only. Full release tracklists are checked for independently verified artist credits and version-compatible titles. Candidate scores and competing groups are exported. UI displays the running version. No writes or downloads. See CHANGELOG-v0.9.1.5.md.

---

# v0.9.1.2 — ISRC title and artist-credit reconciliation

See `CHANGELOG-v0.9.1.2.md` for the current update. Previous release notes follow.

---

## v0.9.1.0 — EP review, local staging, triage export (source test release)

- Safely recognizes explicit `EP`, `E.P.`, `[EP]` and `(EP)` album suffix variants. This is an **album-review suggestion only**, not evidence that recording IDs or releases are identical. Regression cohort: five formerly manual-review songs in the v0.9.0.9 CSV exports.
- Adds searchable, centered local staging of track-backed album-search proposals. No automatic stage, no final download authorization, no Lidarr writes. Staging is invalidated when comparison/source/release choice changes. Batch inputs are validated before mutations.
- Adds per-album staged queue export and per-song manual-review triage evidence export. Extends no-writes action plan with local staged flag.
- New offline regression replay and C# self-tests for EP false positives, local staging safety, and invalidation. Windows CI also includes ApprovalQueue.cs and an RID-correct self-test run.
- **Limitation:** no native C# compilation or live Lidarr API execution was possible in the authoring environment. Validate Windows build and self-tests locally before running inventory.

## v0.9.0.9 — Album-title alias review and action-plan fix

- Fixed the 174 album-only targets incorrectly held due to missing Lidarr track IDs. Album-search proposals remain approval gated, explicitly marked recording-unverified, and deduplicated by album.
- Introduced review-only typed album title aliases: `Vol.`/`Volume`, named edition suffixes, and narrowly defined Unicode glyph variants. No exact master or recording MBID substitution is inferred.
- Added album-only and alias pending counters, album relationship diagnostics, and extra action-plan evidence columns.
- Offline regression replay: 2,505 rows, 22 new alias-review candidates, 349 -> projected 327 manual. Artists needing identity review remain 71.
- Kept the green centered interface, read-only Lidarr integration and pre-existing review safeguards. Native .NET compile and live Lidarr test remain pending.

---

## v0.9.0.8 — Album-search proposals, explicit recording conflict and edition safeguards

- Distinguished album-search planning eligibility from exact recording MBID confidence; kept both separate in CSV exports.
- Exact artist/title/source-album agreement may support selecting the album as a *future search target* even if the source MusicBrainz recording differs. This NEVER selects/overwrites a specific track or proves file presence.
- Deluxe/EP/expanded album title aliases are grouped for explicit user review, not silently treated as equivalent.
- Preserved no-write/no-download behavior and added regression tests for MBID conflicts and edition group approval.
- Added maintenance-only offline replay script and reproducible 2,505-row prospective comparison: 609 → 349 review, with 187 MBID disputes still flagged and 73 edition-related album proposals held.
- Source-only; native .NET build not run in authoring environment. See VERSION-NOTES-v0.9.0.8.md and TEST-V0908.md.

---

## v0.9.0.7.1 — build and album-review approval hotfix

- Fixed CS0136 in `Program.cs` by removing an inner `body` variable that conflicted with the request handler's existing `body` local.
- Fixed a second bug in the same route: it was trying to parse an already-consumed request stream. The group ID and album ID are now taken from the already-parsed request JSON.
- Preserves all v0.9.0.7 review/matching logic, green UI, session-only approvals, and zero-write Lidarr safeguards.
- Complete source archive; Windows/.NET compilation and end-to-end album approval still need to be confirmed.

## v0.9.0.7

- Album-cohort release recommendation and user-reviewed, session-only album choice.
- Clickable/searchable artist and song category details.
- Release-group evidence and safe action-plan CSV exports (no writes; duplicate album searches consolidated).
- No automatic acceptance of deluxe-versus-standard releases or conflicting recording IDs.
- Native .NET build pending Windows validation.

## v0.9.0.6 — Duplicate recording grouping and migration-category planning

See `VERSION-NOTES-v0.9.0.6.md`. A known recording shared by multiple Lidarr releases is grouped; when the source album uniquely selects a track it can be proposed. Otherwise the song is recognized but future searching remains held for explicit release choice. New `lidarr-release-candidates.csv` provides full diagnostics. All operations remain read-only.

# v0.9.0.5 — Album inventory payload fix

- **Fix:** eliminate the unfiltered `GET /api/v1/album` response that exceeded 80 MB before the first artist was checked.
- Retrieve albums for each relevant local artist with `GET /api/v1/album?artistId=N` alongside filtered track and trackfile data; keep the two-artist concurrency limit.
- Log oversized and unexpected JSON responses in the inventory diagnostics CSV, including the sanitized API path and artist ID.
- Offline regression assertion checks that *all three* inventory endpoints are artist-scoped and invalid artist IDs are rejected.
- No changes to previous artist/song evidence, matching thresholds, green UI, or read-only behavior.
- **Not yet certified:** compilation and live comparison must be tested on Windows.

---

# v0.9.0.4 — Timeout and interruption diagnostic hotfix

- Fixed misreporting an HTTP request timeout as an interrupted comparison. The earlier timeout was 90 seconds, and the shared `HttpClient` also had a default 100-second timeout.
- Separate inventory HTTP client, 180-second per-attempt deadline, up to three attempts for timeouts, transient HTTP statuses or network failures.
- Reduced simultaneous artist inventory reads from four to two, with track and trackfile reads sequential for a given artist.
- Added per-endpoint diagnostic CSV export with HTTP status, retry attempt, elapsed milliseconds and error classification, without API key or music metadata.
- Exposes request/retry/artist progress counts during the read-only inventory step.
- Retains atomic comparison results: no Lidarr writes and no partial set of tracks classified as present/missing.
- Source-only patch; native .NET compilation and live Lidarr integration have not been performed in this environment.

---

# v0.9.0.3 — Lidarr inventory API request hotfix

- **Root cause:** v0.9.0.2 requested `GET /api/v1/track` and `GET /api/v1/trackfile` without the required filters, resulting in `400 Bad Request`. Confirmed against Lidarr's actual `TrackController.GetTracks` and `TrackFileController.GetTrackFiles` source.
- **Fix:** Query read-only `GET /api/v1/track?artistId=<numeric ID>` and `GET /api/v1/trackfile?artistId=<numeric ID>`, selecting only validated artists relevant to the imported Takeout. Requests are grouped into small batches (four artists at once). Progress is displayed in the existing UI.
- **Robustness:** When any query fails, expose the failed GET route and status without exposing a secret; never commit partial inventory. Added query parameter regression checks to the offline self-test.
- **Preserved:** Matching and evidence-restoration logic, recovered IDs, CSV columns, the centered green theme, and the read-only guarantee. No fresh MusicBrainz scan required.
- **Validation limit:** Source inspected and structural checks run; the .NET compiler, Windows CI and a real Lidarr server are not available in this environment. Native compilation and live integration remain unverified.

---

# v0.9.0.2 — Import and Lidarr comparison fixes

## Fixes

- Round-trip preservation for all v0.9.0.1 song audit columns, including album agreement, version ambiguity, cross-song corroboration, MusicBrainz query score, Deezer ISRC result and decision.
- Restores artist `Unique source songs`, high-confidence and unresolved title counts, and recovery status fields on audit re-import. If importing a damaged `(6)` report, aggregate counts are reconstructed from its row-level data.
- New optional **original song audit** selector: restores original recording evidence from an earlier export (such as `(5)`) when the song/source row and both candidate MBIDs have not changed. Newer recovered identifiers, decisions and provider evidence are preserved.
- Exported `Prior evidence restored` and `ISRC candidate validation details` fields support review and debugging.
- ISRC lookup records per-candidate title and credited artist agreement/rejection reasons in both song exports and recovery diagnostics.

## New read-only inventory comparison

- Lidarr `GET /api/v1/track`, `/api/v1/album`, `/api/v1/trackfile`, behind an explicit UI control.
- Matches by MusicBrainz recording MBID, or unique artist/title/album evidence without overriding conflicting recording IDs.
- Only displays **Already in Library / Songs** when Lidarr reports `hasFile`, a positive trackFileId and a matching trackfile record.
- Distinguishes missing tracks or files (proposed additions), ambiguous matches (manual review), and unavailable inventory (not checked).
- Migration readiness CSV includes track ID, album, trackfile ID, decision method, candidates and exact reason. Nothing is written to Lidarr.

## Build status

JavaScript syntax and ZIP structural validation were performed in the authoring environment. Windows .NET compilation and integration with a real Lidarr instance still require user testing or Windows CI. Run the new offline `--self-test` suite before launching a full workflow. This is **not** a statement that the Windows build succeeds.

---

# v0.9.0.1 — Compilation hotfix (2026-10-21)

- Corrects malformed C# string literals in `SupplementalEvidence.cs` CSV import self-test (original source lines 307–308).
- Replaces fragile quote-based CSV text replacement with a directly generated test fixture using `CsvLine(...)`.
- No changes to the matching algorithms, Lidarr connections, UI styling, imported music metadata, or verification behavior.
- Previously reported 32 C# syntax diagnostics were caused by the malformed lines; this patch addresses that issue.
- **Not yet compiled in this environment** (no .NET SDK available). Build and offline self-test on Windows remain required.

---

# v0.9.0 — Independent Artist ID and ISRC Recovery (source test)

## Focus

Reduce truly unresolved artist/song identities **through stronger identifier evidence**, rather than changing threshold labels or re-running 2,505 title searches. Keep artist+song and exact-recording-master confidence separate.

## Implemented

- Re-import previous audit CSV provider columns (previous import dropped Apple/Deezer IDs and status).
- On-demand artist MBID validation: Lidarr foreignArtistId → MusicBrainz canonical artist/alias, with corroborating source-song evidence; same-name ambiguity remains reviewable.
- Direct MusicBrainz artist-name candidate search when no Lidarr MBID is available. Candidates are never automatically accepted.
- Deezer track details → validated ISRC → MusicBrainz ISRC-linked recording candidates, with title, artist ID, and existing recording conflict checks. No exact-master upgrade.
- Comprehensive per-attempt recovery event audit and additional song/artist evidence columns.
- Conservative migration-readiness preview CSV: verified artists categorized, song file presence explicitly unknown until Lidarr track/trackfile inspection is implemented.
- Green-centered Step 6 UI recovery status/progress, export buttons, and offline recovery parser fixtures.
- GitHub Actions workflow updated for new source files.

## Not implemented

- Full Lidarr track, trackfile, album/release inventory comparison; accordingly no definitive Song 'Already in Library' / 'To Be Added' counts.
- Manual decisions, actual write/import/search/download operations, final portable one-click EXE, standalone creator/about/support page, or automatic cleanup.
- Audio acquisition. Future support must use authorized audio sources and explicitly approved user operations.

## Validation limitations

JavaScript parse/static checks and source archive/data checks may be performed locally here. C# compiler, live provider requests, Windows execution and end-to-end integration remain unavailable until Windows CI/user testing is completed. No claim of a successful production release.
