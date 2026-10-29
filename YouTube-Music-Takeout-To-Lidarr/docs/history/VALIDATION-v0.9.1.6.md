# v0.9.1.6 validation report

## Confirmed from user-provided v0.9.1.5 evidence
- 194 candidate-scoring rows.
- 28 `incomplete-discovery-review-only` results.
- Closure's *I'm Gone* and Sam Tinnesz's *Ready Set Let's Go* each show a complete release inspection (1/1), one compatible credited track and one recording MBID, yet were marked incomplete.
- MusicBrainz published search API uses top-level JSON `count`; browse API uses resource-specific `release-count` / `release-group-count`.

## Code changes and available checks
- Search parser uses `count` with limited offset paging and safe refusal on incomplete pages.
- Browse parsers retained unchanged behavior.
- New C# fixtures added to the built-in `--self-test`; they have NOT been executed here.
- JavaScript syntax checked using Node.js.
- C# brace/token structural scan checked; this is NOT C# compilation.
- No Lidarr-writing method or endpoint was introduced.
- Packaging ZIP integrity and SHA-256 manifest verified.

## Limits
- Native .NET build and C# self-tests unavailable in this environment (SDK not installed).
- The number of newly promoted recordings is unknown until live targeted recovery runs against MusicBrainz on Windows.
- Recording-first fallback never changes audit MBIDs, and source recording conflicts remain held.
