# v0.9.1.11 — Restore Every Page After Refresh

Built directly from v0.9.1.10; no changes to MusicBrainz matching, audit rules, or Lidarr-write safety boundaries.

## New behavior
- Refresh during Steps 1–5 returns to the last wizard step, provided its necessary server-side data is still available. Lidarr API keys are **never stored** by the checkpoint.
- Refresh while MusicBrainz/Apple/Deezer verification is running reattaches to the server-owned progress screen, with no new search or Takeout scan.
- Refresh after verification returns to the exact Manual Review tab and review row (and any unsaved highlighted candidate), or the Migration Preview tab and album selection, or final confirmation.
- Refresh during **local staging only** pauses and reconciles the queue using the server's staging state. An explicit **Resume Preparation** button continues items still pending; it never automatically replays work on page load.
- Refresh on completion or the About page retains that screen only after validating the completed staging plan against the current server.
- Page checkpoints use tab-scoped `sessionStorage`, keyed by the .NET service session token. Secrets (Lidarr API key and GitHub tokens), music metadata and audio URLs are not stored. Restarting the app starts a new service session, so a previous navigation checkpoint is not reused.

## Scope
Navigation only. A refresh does not upload an archive, re-run verification, reauthenticate Lidarr, or initiate any Lidarr import. Source-data progress remains in RAM of the running .NET process and is not resumable across **process termination**.

## Note on import progress
The application's import plan remains **local staging only**. A browser refresh while staging is in progress does not automatically re-issue writes; a user can resume the remaining local staging operations after verification of current staging state.
