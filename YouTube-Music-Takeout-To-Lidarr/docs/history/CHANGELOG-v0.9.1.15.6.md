# v0.9.1.15.6 — controlled Lidarr import and visible progress

## Final-step import

- **Add to Lidarr Only** adds independently verified missing artists as unmonitored
  entries and monitors explicitly selected, track-backed albums that already
  exist in Lidarr. It never launches a search.
- **Add and Search** performs those actions and sends album-specific Lidarr
  search commands after a separate acknowledgement. Searches may acquire every
  track on the album.
- Existing root folders, quality profiles, and metadata profiles are loaded
  from Lidarr. The importer verifies the chosen settings and all selected
  album IDs before sending any writes.
- Unverified manual selections, saved source metadata, ambiguous songs, and
  proposed albums without verified Lidarr track backing remain excluded.

## Progress and results

- A determinate bar and detailed per-action outcomes track the app's actual
  Lidarr API requests, including failures and accepted searches.
- A second, indeterminate indicator polls Lidarr's active background command
  queue, including artist metadata updates. It is independent of the import
  request count and may show unrelated Lidarr activity.
- A downloadable CSV lists attempted actions and their outcomes. Browser
  refresh resumes status polling, never reissues the import request.
- Migration Preview can now advance to the final step even with zero
  selectable albums; verified artist additions can still be reviewed, and a
  no-op completion is available when nothing is eligible.

## Important limits

- This version does **not** create audio files from YouTube Takeout metadata,
  nor does it automatically import source-only manual entries.
- Lidarr accepts work asynchronously; queued searches, downloads and file
  imports may continue after the app finishes sending commands.
- Lidarr write errors are logged per item. The importer does not automatically
  retry writes, since a network timeout may occur after the server accepted one.
- Offline UI tests passed during preparation. Compile and integration testing
  with a disposable Lidarr instance are required before a public Release.
