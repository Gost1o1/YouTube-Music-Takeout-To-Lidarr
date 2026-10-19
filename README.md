# YouTube Music Takeout to Lidarr

**A local Windows PowerShell 5.1 tool to inventory music-related Google Takeout CSVs, preview matching tracks in Lidarr, and optionally request Lidarr album searches with explicit confirmation.**

- No fixed drive letters, server names, Lidarr IDs, or user-specific API keys.
- No Docker requirement.
- No direct media downloads, artist imports, or file moves. Optional, explicitly confirmed album-search requests can be sent to Lidarr using its configured download clients and indexers.
- Reports stay in **Documents/MusicTakeoutToolkit/<timestamp>**.
- Temporary ZIP extraction is removed at the end. The original Takeout archive and reports are **never deleted automatically**.
- Optional read-only Lidarr artist name/alias comparison requires `-LidarrUrl` and `-LidarrApiKey` supplied by the user. These values are not stored in output files.

## Quick start: on Windows

Download this repository as a ZIP, extract, open PowerShell in that folder, and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1
```

The tool will ask for an extracted Google Takeout folder or `.zip` export and display an interactive menu. Or run it without prompts:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Start.ps1 -InputPath "$HOME\Downloads\takeout.zip" -NoPrompt
```

A published repo can provide a single pasteable PowerShell launch command. The command below points at this project’s `main` branch. Review its source before running:

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip';$d=Join-Path $env:TEMP ('takeout-'+[guid]::NewGuid().ToString('N'));try{New-Item -ItemType Directory -Path $d|Out-Null;Invoke-WebRequest $u -OutFile (Join-Path $d 'app.zip') -UseBasicParsing;Expand-Archive (Join-Path $d 'app.zip') (Join-Path $d 'app');& (Get-ChildItem (Join-Path $d 'app') -Filter Start.ps1 -Recurse | Select-Object -First 1).FullName}finally{Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue}
```

**Security:** One-line commands that download and run Internet code require trust in the repository. Read the script first, avoid unknown forks, and use a tagged release URL for production instead of an automatically changing `main` branch.

## v0.3: Guided Google Takeout export and archive discovery

Launching `Start.ps1` **without** `-InputPath` opens an interactive setup menu:

1. Open the official [Google Takeout export page](https://takeout.google.com/settings/takeout) and follow on-screen instructions to select **YouTube and YouTube Music** and request an export.
2. Wait for Google's export-ready notification and download the archive using your browser.
3. Select **Find in Downloads**, which lists up to 12 recent likely Takeout ZIPs or extracted folders. Choose and confirm an entry. If your browser saved it elsewhere, paste the ZIP or folder path instead.
4. The selected path is saved in `Documents/MusicTakeoutToolkit/last-takeout-path.json` so you can resume later. The original archive is **never deleted**.
5. Continue to the existing inventory, dashboard, optional Lidarr album-search review, and unmatched YouTube Music link report.

The helper cannot sign in to Google, request the export through an API, download an archive from your Google account, or reliably determine a browser's custom download destination. It only opens the official page and detects files you have already downloaded. **Do not upload your Google Takeout archive, reports, or API keys to GitHub.**

Noninteractive runs remain supported with `-InputPath` and `-NoPrompt`. Neither the Takeout process nor Lidarr album searches start automatically.

## Supported input

- A ZIP or extracted folder containing music/video CSV exports.
- Automatically recognizes common title fields (`Song`, `Title`, `Track Name`, `Video Title`) and artist fields (`Artist`, `Artist Name`, `Channel Title`).
- Captures YouTube video IDs and produces direct review links when the CSV supplies them.

**Limitations:** Google Takeout formats vary. This initial implementation parses CSV; some exports use HTML/JSON, which will be reported as unsupported rather than silently counted as songs. Artist names may be absent from playlist/video CSVs. Track rows are *not* assumed to represent downloaded audio. A name match in Lidarr is *not* proof of recording identity.

## Output

- `dashboard.html`: searchable overview and status
- `tracks.csv`: inventory with source columns
- `artists.csv`: distinct artist credits
- `optional-youtube-queue.csv`: review links only, `DownloadRequested=NO`

## Optional Lidarr download workflow

Run `Start.ps1`, choose **5 — Review Lidarr album searches**, and supply a Lidarr URL and API key (the key is not written to reports). The review script:

1. Retrieves Lidarr artists and their catalog tracks (read-only).
2. Finds **unique artist name/alias + exact normalized track title** matches; ambiguous albums and unmatched music go in `unmatched-youtube-review.csv`, with original YouTube Music links where provided by Takeout.
3. Saves `lidarr-track-review.csv`. **Inspect this CSV before approving.** An artist-name match plus track-title match is not independent proof the source recording is the same version.
4. Requires two deliberate typed confirmations before posting an `AlbumSearch` command for the distinct album IDs. Lidarr may search/download **whole albums**, not individual tracks, and actual downloads depend on the user’s configured indexers and download clients.

Neither `Start.ps1` nor `Lidarr-Review.ps1` imports artists, automatically downloads YouTube audio, modifies media files, or relocates a library. **Nothing is searched or downloaded by default.** The YouTube CSV is for review and for separate, authorized download tools; no automatic YouTube downloading is included.

### Security and compatibility

- Support for Windows PowerShell 5.1 is intended; this version has not been end-to-end tested on every Takeout format or Lidarr version.
- Do not paste Lidarr API keys into public issues or upload personal Takeout exports to GitHub.
- Run with a user account that has access only to the systems you intend to manage.
- Store reports in Documents; the launcher deletes only its own temporary extracted repository copy.

## What it doesn't do (yet)

This version **does not** run yt-dlp, directly download YouTube audio, automatically add new artists to Lidarr, upload playlists, or fingerprint audio. It cannot guarantee downloaded albums contain the precise versions of songs saved in YouTube Music.

## Updating this GitHub repository

Upload or replace the files in [the repository](https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr) via **Add file → Upload files**, or use GitHub Desktop. Do not upload private Takeout exports, API keys, generated reports, or media files. For an existing project, upload the updated files in this package, and remove the outdated instructions by replacing `README.md`.

## License

See `LICENSE` for the proposed **Personal and Noncommercial Use License**. Commercial use is not granted. This is **source-available**, not an OSI-approved open-source license. The custom terms are a draft and should be reviewed by a qualified lawyer before release. All author and third-party copyright rights remain with their respective holders.
