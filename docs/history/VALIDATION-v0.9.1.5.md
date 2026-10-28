# v0.9.1.5 validation report

## Input baseline (read-only inspection)

- Previous source: complete v0.9.1.4 ZIP (not a mockup or replacement application).
- Latest source metadata: `musicbrainz-songs-audit (16).csv` has 2,505 rows; `musicbrainz-artists-audit (16).csv` has 951 rows.
- Source songs: 2,246 previously supported song identities and 259 unresolved song identities.
- Previously high-confidence artist IDs: 883, requiring review: 68.
- Previous Lidarr migration CSV: 2,206 proposal rows and 299 manual-review song rows. These are baseline results, NOT new v0.9.1.5 outcomes.
- 171 unresolved source songs have a validated artist and nonempty source album, forming approximately 150 artist/album cohorts under simple casefold grouping; the application applies its own normalizer. 149 of those rows have previous Apple/Deezer support.

## Checks performed in packaging environment

- New C# discovery source exists, is referenced by the live recovery method, and is included by project wildcard compilation and GitHub Windows CI triggers.
- New C# self-test fixtures for release search result parsing, nested release-group IDs, duplicate groups, explicit EP review, edition prioritization, offset based on actual response length, and conflicting version safeguards. These fixtures are **included but not executed here**.
- Browser JavaScript extracted from HTML and passed `node --check` (Node 22).
- Source assertions verify all discovery strategies, requested page caps, complete-inventory/no-conflict rule, artist-credit checks, and no new Lidarr write endpoints.
- All 2,505 song rows and 951 artist rows in prior exports were counted; no private CSVs included in source package.
- Source archive integrity and checksums confirmed.

## Not verified here

- Native .NET 8 compilation or execution of the C# self-tests (SDK not installed).
- Live MusicBrainz response formats/performance with the new queries, or Lidarr end-to-end comparison.
- Actual change in unresolved song/artist counts; no new recording IDs were claimed from an offline static test.

The user must run `dotnet build`, `--self-test`, and `dotnet publish` on Windows and check `v0.9.1.5` in the web UI. Release-title and alias index hits are for review, not exact recording proof. A bounded incomplete release set cannot promote an MBID.
