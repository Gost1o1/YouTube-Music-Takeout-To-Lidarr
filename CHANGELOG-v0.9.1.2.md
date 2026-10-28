# v0.9.1.2 — Targeted MusicBrainz title and artist-credit reconciliation

## Changes

1. Introduced `IsrcTitleReconciliation.cs`: compares normalized base titles, version descriptors, and featuring annotations. Holds live/studio, remix/edit, venue and substantive-subtitle conflicts.
2. ISRC candidate matching now records the exact title-reconciliation decision, credited artist names and artist MBIDs, name-only corroboration and whether the resolved MBID matches the expected validated artist.
3. A featured performer present in only one title is accepted as harmless title formatting **only when that person is separately credited in the MusicBrainz recording**.
4. Artist candidate discovery now queries canonical names and aliases; unvalidated search results remain candidate-only, not high-confidence identities.
5. Preserve all candidate rejection reasons in the recovery CSV, rather than lumping artist/name/qualifier mismatches into one status.
6. The saved-evidence verification screen now displays **Imported CSV** instead of misleading `0s / Calculating...`, and states that optional targeted queries may run without a fresh full-library scan.
7. Added self-test fixtures for featured-artist conflicts, exact normalized titles, version qualifiers, and known-MBID disagreement.
8. Updated Windows GitHub Actions watch paths for the added reconciliation C# source.

## Safety rules

- No Lidarr writes or downloads.
- Exact-master confidence never automatically raised by these new heuristics.
- Existing conflicting MusicBrainz artist and recording IDs are not silently overwritten.
- Alias/name search alone cannot prove artist identity.
- New title matching can only change **provisional** artist+song identity status when a unique ISRC recording and validated artist MBID also corroborate it.

## Known limits

- No local .NET SDK is available in the packaging environment, so C# compilation and self-tests have **not** been executed here. Build and test on Windows before using the revised app.
- The result cannot be predicted from the supplied audit CSVs because the actual MusicBrainz artist-credit and recording title payloads were not fully preserved in those exports. A targeted rerun will reveal new decisions.
- Ambiguous artist relationships, releases and exact audio versions may still require manual review.
