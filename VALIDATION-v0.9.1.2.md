# Validation report — v0.9.1.2

## Checked successfully here

- Archive construction, manifest hashes and ZIP CRC entries
- JavaScript syntax (`node --check`)
- Static source invariants for expected MusicBrainz ISRC URL, feature-credit safeguard, explicit rejection categories, and read-only UI
- Simplified C# lexical delimiter balance across all project `.cs` files (advisory only)
- Regression-fixture input sizes: 2,505 latest song audit rows and 309 latest manual-review rows
- Added C# `--self-test` fixtures for equivalent version labels, incompatible live/studio/venue versions, feature credits, and known artist-MBID conflicts

## Not checked here

- Native C# compilation / execution (no `dotnet` toolchain present and no external download access)
- Live MusicBrainz/Deezer availability, audio-master accuracy or Lidarr connectivity
- Whether the new version reduces 309 remaining reviews (requires a new *targeted* identifier recovery run)

## Data policies

- No private Takeout archive, API keys, user song audits or recovery diagnostics included in the distributable ZIP.
- Do not represent ISRC identity recovery as proof of a particular edition or audio master.
- Artist-search results are candidate-only until independently corroborated.
- No Lidarr searches, downloads or write operations are enabled.
