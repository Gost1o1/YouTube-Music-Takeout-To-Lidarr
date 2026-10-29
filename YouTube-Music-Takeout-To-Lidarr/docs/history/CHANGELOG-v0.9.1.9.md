# v0.9.1.9 — Unified Automatic Verification and Request Efficiency

This is a complete source release derived from v0.9.1.8. It preserves the existing ten-step Takeout/Lidarr setup, manual-review/preview/import-plan UI, review decision ledger, GitHub device authorization and star integration, and read-only Lidarr safety boundary.

## User workflow
- Connecting to Lidarr automatically starts the entire fresh-verification pipeline. There is no "Begin Fresh Verification" step and no previous-audit import panel in the normal UI.
- One coordinated workflow performs MusicBrainz verification, Apple/Deezer catalog corroboration, MBID/ISRC/release checks, and read-only Lidarr inventory comparison, then opens Manual Review.
- The verification page shows only progress bars, songs checked, artists resolved, elapsed time, estimated remaining time, and speed. It omits stage labels and diagnostic panels. Estimates are intentionally tentative, especially during follow-up phases.
- A provider-disclosure notice appears at Lidarr connection, before any external title searches.

## Performance changes
- One reusable `HttpClient` connection pool for the whole MusicBrainz verification phase, rather than recreating clients every 100 source rows.
- All MusicBrainz searches and release/artist/ISRC follow-ups use **one shared, serialized API rate limiter**. Do not parallelize MusicBrainz requests to bypass its rate limit.
- Successful MB JSON responses cached **only in the current fresh run** with both a memory and entry limit; errors, timeouts, and IDs inferred from cache entries are never automatically accepted.
- Source rows with identical artist, song, album and credited artists share their first-pass result; duplicate rows do not count as independent artist corroboration.
- Primary and later recording searches use the same query-page size to maximize safe in-run cache reuse.
- Apple/iTunes and Deezer searches overlap by provider, while the Apple per-provider pacing is retained. Exact repeated catalog rows may reuse successful provider evidence.
- A single retry manager handles MusicBrainz transient HTTP 429/5xx, timeouts and network failures, with Retry-After support and bounded exponential delays.
- New `/api/pipeline/status` provides stage-weighted overall progress and network/cache counters for internal diagnostics. They are not shown in the streamlined UI.

## Safety
- Evidence caches are cleared on each fresh pipeline start; there is no on-disk response cache, silent reuse of old audit results, or broadened automatic matching confidence.
- The Lidarr comparison is read-only. "Confirm" in the current import-flow screens still stages a local plan, **not** a real Lidarr write, search, or download.
- External catalog hits alone do not establish recording MBIDs or exact audio masters; identity and edition conflicts remain review-only.
- Failures stop the automatic workflow with a visible error and retry option, never a false completion.

## Known limits
- Actual speed and reductions in manual review require real MusicBrainz and Lidarr responses on the user's machine.
- The .NET SDK was unavailable during source packaging. Windows build and self-tests are necessary before relying on this version.
- A full fresh run can still take substantial time because MusicBrainz/API provider limits are respected. Timing is not guaranteed.
