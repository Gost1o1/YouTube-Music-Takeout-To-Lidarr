# YouTube Music Takeout to Lidarr v0.6.3

## Launch (Windows)

Extract the whole ZIP first. **Double-click `Launch.cmd`** (the Windows command launcher). No working-directory setup, admin rights, or typed PowerShell command is required. Keep the launcher window open during the session; it hosts the local service. Do not move `Launch.cmd` outside its extracted application folder.

An optional real `.exe` launcher can be built on Windows with `Build-EXE.cmd` (uses the built-in Windows .NET Framework compiler). This creates a small launcher; it does **not** compile the full application or embed the parser.

## Google account

Google Takeout opens in the standard Edge/browser profile to use the user's existing Google session. An embedded WebView2/browser has a separate profile and generally will not reuse existing Edge sign-in cookies. We do not accept Google passwords and do not automatically create an export.

## Analysis

Every scan uses a unique `Documents\MusicTakeoutToolkit\runs\...` output folder. The app checks `tracks.csv` directly, even when PowerShell returns nonfatal warnings. Errors and diagnostic logs live in that run folder, and startup messages go to `Documents\MusicTakeoutToolkit\startup-errors.log`.

## Safety

This remains a staged build. Verified artist imports, release searches, previews, and retries are **not yet enabled**. The library comparison is name-based only, not MusicBrainz verification. No Lidarr imports/downloads without approval.
