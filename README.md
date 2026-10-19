# YouTube Music Takeout to Lidarr

A Windows PowerShell tool for reviewing a Google Takeout export of YouTube Music data, comparing recognizable tracks with Lidarr, and exporting unmatched source links. This is an early, read-only-by-default version (v0.3.3). **It does not automatically download music or import new artists.** Album searches in Lidarr require explicit confirmation.

## Start with one command

Open **Windows PowerShell** (Administrator is **not** required), copy this entire command, paste it once, and press Enter:

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip'; $d=Join-Path $env:TEMP ('YTMusic-'+[guid]::NewGuid().ToString('N')); try { New-Item -ItemType Directory -Path $d -Force -ErrorAction Stop | Out-Null; Invoke-WebRequest -Uri $u -OutFile "$d\app.zip" -UseBasicParsing -ErrorAction Stop; Expand-Archive -LiteralPath "$d\app.zip" -DestinationPath "$d\app" -ErrorAction Stop; $s=Get-ChildItem "$d\app" -Filter Start.ps1 -Recurse | Select-Object -First 1; if (-not $s) { throw 'Start.ps1 not found' }; & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $s.FullName } finally { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue }
```

The launcher downloads the current project to a temporary directory, opens its interactive menu, and removes only the temporary application copy when you exit. It does not change PowerShell's permanent execution policy. **Review this repository's code before running software fetched from the internet.**

## What happens next

1. Open [Google Takeout](https://takeout.google.com/settings/takeout) from the setup menu and request a **YouTube and YouTube Music** export.
2. Once Google makes the export available, download its ZIP file(s). Return to the tool to search Downloads or choose another folder.
3. Review the inventory dashboard and its source-file coverage report. Some Takeout exports include formats this version cannot fully parse, so an incomplete inventory is not a complete music library.
4. Optionally connect to your own Lidarr server and **preview** album searches. Any search request requires explicit confirmation; Lidarr may acquire whole albums rather than exact tracks.
5. Export unmatched songs and available original YouTube Music links for separate review. Nothing is downloaded from YouTube by default.

Reports are saved in `Documents\MusicTakeoutToolkit`. Your original Takeout files are not deleted. Do not upload private Takeout exports or API keys to GitHub.

## Diagnosing incomplete exports

If the dashboard shows a small number of track rows but **0 artists** and **0 YouTube video IDs**, this is **not** a successful full music import. Open the generated report folder in `Documents\MusicTakeoutToolkit`, inspect `source-files.csv`, and look for skipped/unsupported file types. Google Takeout may split a single export across multiple ZIP files; make sure you have all its parts. This version processes one selected ZIP/folder per run; to include all parts, you can extract all ZIPs to one common directory and choose that folder. Extract only archives you trust.

`source-files.csv` lists filenames and metadata classifications; do not publish private export data or local paths. An unsupported format cannot be assumed to have no music.

## Current limitations

Google Takeout layouts vary. The current importer does not recognize every HTML/JSON format or guarantee it finds all saved songs or artists. A matched artist or song title is not proof of the exact recording version. The program does not directly download audio, automatically import artists, or move media files.

## License

See [LICENSE](LICENSE) for the draft **Personal and Noncommercial Use License**. This project is source-available, not OSI-approved open source. The license draft should receive legal review.
