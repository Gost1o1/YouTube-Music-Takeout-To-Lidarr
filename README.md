# YouTube Music Takeout to Lidarr — v0.6.1

## Windows application

Run `Start.ps1` with Windows PowerShell 5.1 (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1`). No admin permission required. The launcher starts a localhost-only HTTP service and opens a standalone Microsoft Edge app window where Edge is installed. Keep the PowerShell process running while using the application. If Edge is not installed, the default web browser is used.

**This is the actual, screenshot-matched 10-screen UI**, not the earlier Windows Forms dashboard. Takeout detection, file-picker fallback, the existing archive parser, music preview, and Lidarr connection / read-only name comparison are wired to real local operations. Other screens are displayed to review the design, but importing artists, recording-level MusicBrainz verification, licensed audio preview, release searches, downloads, persistence, retry, and export-report controls are **not implemented**. They remain disabled or clearly identified as unavailable.

No file content, API key or library data is sent to external services by the local UI. Clicking Google Takeout, MusicBrainz or YouTube Music links opens those external sites explicitly. Lidarr credentials are transmitted only from the local app to the Lidarr server address the user configures; no credentials are written to reports.

Output reports from the existing Takeout parser are stored in `Documents\MusicTakeoutToolkit`. If analysis fails, engine output and diagnostic logs are stored there.

## Windows-specific validation

This package was statically reviewed and its ZIP tested, but it has **not been run on Windows**. Test with an existing Takeout ZIP; verify scan -> summary -> connection. Report errors before trying future import features.
