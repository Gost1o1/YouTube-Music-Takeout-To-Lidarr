# v0.9.0.8 — Album-search reconciliation (source-only test release)

## What was wrong

Prior versions equated **exact recording MBID confidence** with **eligibility to propose which Lidarr album to search**. A different MusicBrainz recording ID on a Lidarr track could force a source song to Manual Review even when its artist was validated and the source title and album agreed. This is reasonable caution for identifying a master, but needlessly blocks creation of a **read-only, separately approved album-search proposal**.

## New decision tiers

1. **Exact recording**: MusicBrainz recording MBID agrees and supported track/release is selected. Existing behavior remains.
2. **Album search supported; recording MBID disputed**: Independent artist/song identity is supported, the source artist and title agree, and the source album title agrees with a Lidarr album, but the MBIDs differ. If exactly one Lidarr album matches, propose ONLY that album for future separate approval. Do NOT replace the original recording MBID or report the track as verified/downloaded. If several albums match, create an album-group choice and require explicit selection.
3. **Edition alias review**: Exact source title/artist match and the source album name differs by a recognized `Deluxe`, `EP`, `Expanded`, etc. suffix. Do NOT silently treat editions as equal. Move to a held album-choice group, retaining any conflicting MBID and requiring explicit review before approval.
4. **Unsupported/mismatched identities**: Remain in Manual Review. No guessing, no wildcard title-only automatic acceptance, no automatic downloads.

Album-group approval is STILL **planning-only**; it does not search, download, add, or alter anything in Lidarr. An album choice for a disputed recording leaves the individual Lidarr track ID blank, and the original recording ID remains unchanged.

## Actual exported-data replay

Input: 2,505 source song rows from user's v0.9.0.7.1 `migration-readiness-preview (4).csv` and `lidarr-release-candidates (1).csv`.

| Cohort | Baseline | Offline replay projection |
|---|---:|---:|
| Manual-review songs | 609 | 349 |
| Planned addition songs (includes held/reviewable proposals) | 1,896 | 2,156 |
| Recording-MBID conflict rows moved to album-search planning | 0 | 187 |
| Edition-alias rows moved to an album-choice state | 0 | 73 |

**These are prospective categories only.** They do not represent recovered MusicBrainz recording IDs, a successful .NET build, an accepted search, or actual downloaded music. The user’s Lidarr contains no downloaded music; the app still reports file presence independently.

Maintainers can run the local, no-network `tools/offline_replay.py` with their own previously exported CSVs to reproduce the category-count comparison. The source ZIP does **not** contain user audit CSVs or any private library data.

## Safeguards / invariants

- Never accept two distinct MBIDs as the same recording from an album title alone.
- Never call write operations on Lidarr in this release.
- Require validated source artist/song identity before applying new rules.
- Exact trackfile status still requires a matching Lidarr file record; mismatched recording IDs with file records are not marked downloaded.
- Hold editions and ambiguous albums for deliberate album-level review; no automatic release-equivalence.
- Export album-target evidence and MBID dispute flags in candidate, action-plan and readiness CSV files.
- Keep the existing green centered UI and the full original wizard, with slightly more explicit category labels.

## Build status

Native .NET 8 is not installed in the authoring environment and the network is unavailable for installation. JavaScript syntax, C# bracket/token balance, source consistency, ZIP hashes, and the offline 2,505-row replay have been checked. **Native Windows build, C# self-test and live Lidarr comparison remain unverified until run on Windows or GitHub Actions.**
