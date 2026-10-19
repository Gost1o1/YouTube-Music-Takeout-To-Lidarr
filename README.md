# Music Takeout Toolkit

**A local, Windows PowerShell 5.1 tool to inventory music-related CSVs from a Google Takeout archive.**

- No fixed drive letters, server names, Lidarr IDs, or user-specific API keys.
- No Docker requirement.
- No downloads, artist imports, or moves of media files.
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

A published repo can provide a single pasteable PowerShell launch command. Replace `YOUR_USERNAME` and `REPO_NAME` after uploading these files:

```powershell
$u='https://github.com/YOUR_USERNAME/REPO_NAME/archive/refs/heads/main.zip';$d=Join-Path $env:TEMP ('takeout-'+[guid]::NewGuid().ToString('N'));try{New-Item -ItemType Directory -Path $d|Out-Null;Invoke-WebRequest $u -OutFile (Join-Path $d 'app.zip') -UseBasicParsing;Expand-Archive (Join-Path $d 'app.zip') (Join-Path $d 'app');& (Get-ChildItem (Join-Path $d 'app') -Filter Start.ps1 -Recurse | Select-Object -First 1).FullName}finally{Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue}
```

**Security:** One-line commands that download and run Internet code require trust in the repository. Read the script first, avoid unknown forks, and use a tagged release URL for production instead of an automatically changing `main` branch.

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

## What it doesn't do (yet)

This version **does not** run yt-dlp, download YouTube audio, import artists into Lidarr, upload playlists, or fingerprint audio. Future opt-in modules can be developed separately with explicit confirmation for each action.

## Creating your GitHub repository (no Git needed)

1. Sign in to [github.com/new](https://github.com/new).
2. Choose a public repository named `music-takeout-toolkit` (or another name), set **Add a README** to off, and select **Create repository**.
3. Click **uploading an existing file**, drag the contents of this folder (not your personal music exports), and select **Commit changes**.
4. Once uploaded, share the GitHub repository URL and use the one-line launcher above after updating the username and repository name.
5. Do **not** upload private Takeout archives, credentials, videos, local reports, or `.env` files.

## License

Custom personal-use and noncommercial license (pending publication).

This project is intended to be free for individuals using it for personal purposes. Commercial use, commercial redistribution, incorporation into paid products or services, and misrepresentation of the original authorship are not authorized without separate written permission.

Full license terms will be published in the `LICENSE` file. Until then, no additional permissions are granted beyond those provided by applicable law.
