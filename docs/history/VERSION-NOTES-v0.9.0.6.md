# v0.9.0.6 — Release-aware Lidarr migration planning

## Changes

- **Known recording, many releases:** Songs whose MusicBrainz recording ID matches multiple Lidarr track entries are grouped by identity. They are not all treated as unidentified songs. No arbitrary album is chosen.
- **Unique album selection:** When one candidate among those same-recording-ID tracks uniquely agrees with the YouTube Music source album, it becomes the proposed track. If multiple candidates still qualify, search eligibility is held pending explicit release choice.
- **Contradictions remain manual:** Differing known recording MBIDs, unsupported song identities, and inconsistent file indicators are not forcibly promoted to accepted matches.
- **Read-only planning statuses:** Each decision carries an explicit `Action eligibility (proposal only)` and `Release selection status`. Nothing adds an artist, changes monitored albums, starts a Lidarr search, or downloads audio.
- **Six user-facing categories:** YouTube Music artist/song totals; Lidarr already-registered artist count and downloaded-file song count; to-be-added artist/song proposals; and separate artist/song manual review totals. Counts reflect the current in-memory audit and completed Lidarr inventory, not a new MusicBrainz verification.
- **New debugging export:** `lidarr-release-candidates.csv` includes every per-song candidate, album, MBID, track ID, candidate-match evidence, selected track (if unique), and reason for a held decision. The existing `migration-readiness-preview.csv` is extended (new columns appended to preserve old positions).
- **Self-tests expanded:** Tests include safe album disambiguation, duplicate-MBID grouping with held release choice, and blocking conflicting recording IDs.

## Interpretation

*Already in Lidarr — Artists* means the artist's metadata MBID is registered. *Already in Lidarr — Songs* means Lidarr reports a matching trackfile record, not merely a track listing. *To Be Added — Songs* can include a recognized recording with a **release choice pending**; these rows are **not eligible for unattended searching**. Manual review covers genuinely uncertain or conflicting metadata. The app cannot infer actual disk contents independently from the Lidarr file records.

## Scope and safety

This is a **read-only, inspect-and-export source release**. It does not yet provide the user-approved Lidarr write workflow or authorized audio acquisition. It does not recover an exact recording ID from a matching title alone. It does not change or replay MusicBrainz matching. CSV imports are explicitly saved-evidence review; no persistent matching cache is introduced.

## Verification limits

The build environment used to package this source does not have the .NET SDK or a reachable local Lidarr server. JavaScript syntax, archive integrity, static source rules, and baseline-CSV checks were executed; C# compilation, built-in C# self-tests, and the new counts on your 803-artist Lidarr inventory remain to be verified on Windows. Do not assume all 666 duplicate-recording manual reviews will move: the exact outcome depends on their candidate IDs and albums.
