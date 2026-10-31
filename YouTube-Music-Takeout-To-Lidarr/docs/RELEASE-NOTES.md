# Windows preview download

## v0.9.1.15.9 — faster manual review, EXE-local runtime files

- Prepares artist and song candidate details in the background as soon as the review queue becomes available. Metadata requests are limited to three at once; duplicate Apple preview lookups are shared.
- All **new application-managed files** (review decisions, manual-source metadata, Lidarr import checkpoint and diagnostic log) are saved only under `RunData` beside the EXE. The app cleans these exact files on a normal, safe exit. It retains a checkpoint if a Lidarr import is interrupted or uncertain.
- Reads prior saved reviews, manual metadata and import checkpoints from the old `Documents\MusicTakeoutToolkit` folder **once as read-only carryover**. Existing Documents and media-drive files are never automatically deleted.
- Windows release ZIP contains only the EXE and required `AppUI` folder; no unnecessary `.pdb` debugging symbol.
- Browser Exit clears its own session keys. Browser-managed storage and Windows temporary files remain outside the app's direct control.

**Important:** After a normal exit, review choices in `RunData` are intentionally erased. Keep any reports you want before closing. Legacy files from older versions stay in their original locations until you review and remove them yourself. Read `docs/DATA-LOCATIONS.md` in the source package for the exact paths.

This Windows x64 ZIP contains the executable and its required `AppUI` folder.
Extract the entire ZIP, then double-click `YouTubeMusicToLidarr.exe`.

This is an **early development preview**. It reads YouTube Music Takeout data,
assists with metadata matching/review, and compares against a local Lidarr
library. The final step can now **add independently verified artists**, monitor
selected existing Lidarr albums, and optionally **queue album searches** after
explicit approval. Lidarr may download entire albums when a search is enabled.
Unverified and source-only metadata are not written to Lidarr, and this app
cannot create audio files from a Takeout archive.

The import page shows the progress of requests sent by this tool and a separate
panel for ongoing Lidarr background commands. Completed requests do **not**
guarantee a completed download or file import.

Please report unexpected behavior through the repository's Issues page.
Do not upload private Google Takeout archives, Lidarr API keys, or CSV audit
exports with issues.
