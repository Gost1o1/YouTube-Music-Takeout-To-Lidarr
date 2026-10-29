# v0.9.1.15.8 — import safety and truthful review progress

## Changes

- Artist/song Addressed, handled and remaining-review counts now originate
  from the same server-side review queue and durable user decisions. Saved
  choices, explicit None of these, deferred cases and manually preserved
  source metadata are counted as addressed once. A stale selected candidate
  must be reviewed again. **Addressed is not verified or downloaded.**
- Successful Lidarr writes and read-back results are checkpointed atomically
  to `Documents/MusicTakeoutToolkit/lidarr-import-checkpoint-v1.json`.
  Each write is marked before sending it. The checkpoint stores **no API key**.
- On restart, a previous import can be inspected, exported and safely
  rechecked after reconnecting to the original Lidarr instance. An uncertain
  write or search is **not automatically retried**.
- Browser tab ownership: closing the final app tab eventually exits the EXE;
  refresh and multiple open tabs are supported. Active imports/rechecks finish
  before automatic exit. The console stays visible in this version.
- Separate request, confirmed-catalog and outstanding-source status. Manual
  source-only entries block whole-migration confetti even if the selected
  Lidarr catalog batch was registered successfully.
- Large album previews and saved manual summaries now have bounded, scrollable
  or collapsed lists. Import progress shows elapsed time, speed and ETA.

## Not changed

- The app does not repair Lidarr's download client or fetch audio from Takeout
  metadata. A search command is not evidence of a downloaded file.
- A manual choice remains review evidence, never an independently verified
  MusicBrainz identity or permission to write an unverified recording.
- No automatic retry of the 1,241 previously queued searches from v0.9.1.15.6.

## Validation

Node offline UI regressions passed. Windows .NET 8 compilation and disposable
Lidarr API integration testing are still required before publishing a public
release. Do not overwrite a running older version or rerun prior search jobs.
