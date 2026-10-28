# v0.9.1.5 — Discovery, pagination and diagnostics

Base: v0.9.1.4, full .NET 8 project retained.

## Algorithm

- Replaces the targeted single-song release pass with `ReleaseDiscoveryV915.cs`, searching MusicBrainz in stages: artist-MBID-restricted release-group title; aliases/release titles; release index; then bounded artist catalog browse.
- Preserves index-returned release-group MBIDs even where a release title or alias differs from the parent group title, for **review only**. An indexed search hit is not automatic album equivalence.
- Release browse uses actual returned item count, not requested page size, for the next offset. At most 4 pages of 25 released items are requested per candidate, up to 18 inspected full release tracklists.
- Ranks candidate group/edition titles with documented explicit relationships; for each group exports discovered MBIDs, alternative groups, version conflicts, title overlaps, paging completion and reasons.
- **Automatic provisional MBID recovery requires**: a uniquely highest-ranked exact-normalized release group; complete discovery; all releases returned; every release tracklist examined successfully; exactly one matching MBID per song; independently verified artist MBID in recording credits; compatible version title; no conflict with an already accepted MBID. All other cases stay for review.
- Existing ISRC checks, artist identity matching, UI workflow and album staging remain intact. Full verification is not run as part of targeted recovery.

## User interface and diagnostics

- Shows `v0.9.1.5` in every wizard screen.
- Candidate scoring CSV adds `v0915-release-discovery` stage entries, including search methods and browse status.
- Console version updated. Windows CI build job includes the new C# file in change triggers.

## Scope and limits

- The existing v0.9.1.4 run had 299 songs and 68 artists needing manual review. No new live lookups were conducted during packaging; **no reduction is guaranteed or claimed**.
- MusicBrainz imposes rate limits; the application retains its minimum 1.2-second pacing and retry behavior. The expanded pass can take longer.
- No Lidarr writes, album searches, media downloads, or accidental staging. The private Takeout ZIP, audit CSVs, and Lidarr credentials are not packaged.
