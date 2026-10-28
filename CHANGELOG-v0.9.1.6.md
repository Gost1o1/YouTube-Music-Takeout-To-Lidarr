# v0.9.1.6 — Correct MusicBrainz search counts

## Fixed
- MusicBrainz **search** responses use the top-level `count` property. The v0.9.1.5 parser incorrectly looked for `release-group-count` or `release-count`, mistaking successful searches for incomplete discovery.
- Search results now page using `offset` and the actual number of returned results. Search pages are bounded; an incomplete result cannot automatically approve a recording MBID.
- MusicBrainz **browse** response parsers retain `release-count` or `release-group-count` as appropriate.
- Added source-level self-test fixtures for the search-vs-browse distinction and the two rejected examples from the previous candidate audit: Closure / *I'm Gone* and Sam Tinnesz / *Ready Set Let's Go*. These cases still require live-provider confirmation.
- Added the missing **Export All Candidate Scores and Album Evidence (CSV)** browser button.
- Browser and console now display **v0.9.1.6**.

## Added
- A bounded, review-only recording-first fallback when no related release group is discovered. It checks a single source title against a validated artist MBID and exports compatible recording candidates without assigning any IDs.

## Safeguards unchanged
- No Lidarr writes, album searches, downloads, or audio files are initiated.
- Conflicting MusicBrainz recording MBIDs and incomplete release inventories remain held.
- Album proposals, exact recording confidence and downloaded-audio state remain separate.
