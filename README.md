# YouTube Music Takeout to Lidarr — v0.5.0 desktop preview

**Source-available project.** See [`LICENSE`](LICENSE) for the draft personal/noncommercial terms; obtain legal review before relying on them.

## Quick start (one PowerShell command)

Review the repository source before running remote code. Run in **regular PowerShell** (Administrator is not needed). The following launcher keeps its downloaded application files in your own AppData folder so they remain available while the native desktop window is open.

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip';$d=Join-Path $env:LOCALAPPDATA 'YouTubeMusicTakeoutToLidarr';New-Item -ItemType Directory -Path $d -Force|Out-Null;Invoke-WebRequest $u -OutFile "$d\repo.zip" -UseBasicParsing -ErrorAction Stop;Expand-Archive "$d\repo.zip" "$d\source" -Force;$s=Get-ChildItem "$d\source" -Filter Start.ps1 -Recurse|Select-Object -First 1;powershell.exe -NoProfile -ExecutionPolicy Bypass -File $s.FullName
```

The safe launcher keeps code under `%LOCALAPPDATA%` and does not delete files while the desktop app is running. Future updates should use versioned releases and update checks.

## Desktop v0.5.0

- Native Windows Forms GUI, **not** an HTML browser dashboard.
- Select a Takeout ZIP, discover the newest Takeout ZIP in Downloads, or open Google Takeout.
- Scan the full export with the existing proven Takeout CSV parser. Show song/artist counts and a searchable-grid-ready track inventory.
- Connect to Lidarr using your own URL and API key (left sidebar → Settings → General → API Key). Key is kept in memory only.
- Compare installed artist names and aliases **as unverified name hints**; generate a CSV of unresolved songs and original YouTube Music links.
- **This initial desktop release does not add artists or request downloads.** Recording verification, candidate review, and explicit opt-in album searches are the next stages. Do not assume that an existing artist name proves recording identity.
- No audio files are moved. Takeout originals remain untouched. Reports are saved under `Documents\MusicTakeoutToolkit`.

The older PowerShell tools are included as separate legacy scripts to preserve the working parser and workflows. They are **not run automatically** by the desktop app.

## Known limitations

The engine supports the recognized YouTube Music library CSV structure. Extra HTML/JSON exports need dedicated parsers. The desktop comparison is name-only and must not be used to authorize music downloads. Windows testing is needed before release.
