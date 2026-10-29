# Windows preview download

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
