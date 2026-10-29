# v0.9.1.10 — Browser refresh recovery and real candidate metadata

This is a focused hotfix on v0.9.1.9. Matching thresholds and read-only Lidarr behavior remain unchanged.

## Fixed

- Browser reload checks the existing .NET pipeline and source data using `/api/pipeline/status` and `/api/state`, then restores the current progress page without re-importing the Takeout ZIP or starting another scan. If the pipeline is complete, it resumes Manual Review.
- `/api/scan` no longer modifies the selected archive while the pipeline is active and instead gives a relevant resume instruction.
- Artist review rows now show a real source song and album from the corresponding YouTube Music audit, plus a link to the original watch URL when safely formatted.
- MusicBrainz candidate UUIDs are queried only after they are confirmed to belong to the current review item. The UI displays the name returned by MusicBrainz, rather than displaying a generic candidate descriptor and raw UUID as if those were an artist name.
- Neutral artwork placeholders are clearly labeled "No artwork available". No synthetic portrait or invented probability is shown.
- An optional, strictly title-and-artist-matched Apple/iTunes result supplies album art and an audio preview if available. Playback is limited to the first ten seconds; pressing Play again stops and resets it. External-catalog media may not prove the exact original version, and unavailable previews stay disabled.
- The review detail endpoint restricts candidate IDs to the current queue; returned media links are restricted to HTTPS Apple/iTunes CDN domains. No review action writes to Lidarr.

## Limitations

- The Apple thumbnail is album artwork, not guaranteed to be a portrait of the artist.
- Some artists/tracks have no licensed preview in the Apple catalog. A MusicBrainz identity ID alone does not provide images or audio, and this release does not fake them.
- .NET compilation, real MusicBrainz lookups, and playback must be tested in Windows. This container does not include the .NET SDK.
- An existing v0.9.1.9 process must not be replaced during an hours-long scan. Use the live browser-console recovery command documented in TEST-V0920.md. The new executable cannot take over the in-memory scan from the older process.
