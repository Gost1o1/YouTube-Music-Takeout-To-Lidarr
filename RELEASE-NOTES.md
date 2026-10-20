# v0.8.5 — Independent artist/song confidence audit

- Preserves the functioning read-only .NET Takeout, Lidarr, and 100-recording-batch MusicBrainz verifier from v0.8.4.
- Adds **separate** confidence outcomes for source artists and source recordings.
- Audits all automatic matches using recorded title, credits, album and multi-version evidence; declines to call multiple-version recordings high-confidence without an album match.
- Counts distinct source titles supporting each artist identity; three distinct corroborating songs are required for a high-confidence artist classification.
- Produces downloadable, per-recording and per-artist CSVs with named source songs and recorded evidence.
- Separately labels insufficient evidence and conflicting artist IDs. Does not invent MusicBrainz-wide song totals or extra catalog items; catalog coverage remains partial.
- Read-only; no Lidarr modifications, artist additions, searches, or downloads.

## Future work

1. Optional paginated MusicBrainz artist-catalog browse with full/partial coverage and exact song totals and name lists.
2. More alias/transliteration/featured-artist and version-specific searches to reduce remaining unresolved cases safely.
3. Manual-review and explicit opt-in import stages.
4. Build the .NET project on Windows and run validation, including Defender checks.
