# v0.9.1.3 — Candidate-ranking and album-level corroboration

## Implementation

- Added `CatalogCorroboration.cs` and compiled it as part of the existing .NET 8 project.
- Added artist-ID candidate ranking based on MusicBrainz canonical/alias name, credited recording MBIDs and distinct Takeout song titles. Name-only matches remain unverified.
- Added bounded release-group/tracklist queries for remaining multi-song unresolved albums, including explicit edition safeguards and a full-inventory requirement before accepting a provisional song identity.
- Added a dedicated candidate scoring and album evidence CSV export with alternative IDs, source rows, match evidence, request status, reason and acceptance/hold outcome.
- Updated UI to show scored candidate count and a one-click CSV export after targeted research.
- Added offline C# tests for wrong artist, unrelated song, live-versus-studio and mismatched edition handling, and read-only export.
- Updated GitHub Actions to rebuild whenever `CatalogCorroboration.cs` changes.

## Non-goals and safeguards

This version does **not** automatically accept artist aliases, compare audio waveforms, certify recording masters, start Lidarr searches/downloads, modify track metadata or alter the ten-step UI. No persistent API result cache is introduced. Artist identities and songs with ambiguous evidence remain held for review.

## Baseline

Input source CSV (latest v0.9.1.2): 2,505 source songs, 951 primary artist strings, 303 manual-review songs, 71 manual-review artists. Candidate scoring outcomes depend on live MusicBrainz responses and cannot be predicted from the CSVs alone.
