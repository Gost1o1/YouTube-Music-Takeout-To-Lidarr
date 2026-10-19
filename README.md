# YouTube Music Takeout to Lidarr — v0.5.1 wizard preview

A Windows desktop wizard that guides users one step at a time through choosing their Google account, requesting a YouTube Music Takeout archive, locating the ZIP, and generating a local music inventory.

## Launch (regular Windows PowerShell, no Administrator required)

Review the repository before executing remote code.

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip';$d=Join-Path $env:LOCALAPPDATA 'YouTubeMusicTakeoutToLidarr';New-Item -ItemType Directory -Path $d -Force|Out-Null;Invoke-WebRequest $u -OutFile "$d\repo.zip" -UseBasicParsing -ErrorAction Stop;Expand-Archive "$d\repo.zip" "$d\source" -Force;$s=Get-ChildItem "$d\source" -Filter Start.ps1 -Recurse|Select-Object -First 1;powershell.exe -NoProfile -ExecutionPolicy Bypass -File $s.FullName
```

## Guided steps

1. Choose your Google account using Google's own account chooser (no extra confirmation checkbox).
2. Open [Google Takeout with YouTube selected](https://takeout.google.com/settings/takeout/custom/youtube). On Google's page, review **email download link**, **export once**, **ZIP**, **50 GB**, and submit **Create export** yourself. These settings and submission are **not** controlled by the app.
3. Optionally open a targeted Gmail search for Google's **ready to download** email. This is **not automatic Gmail monitoring**: the app does not read email or request Gmail permissions. Download from Google's official Takeout page.
4. The application scans Downloads for the newest `takeout*.zip`, asks before using it, or opens a Windows file picker. It preserves the original archive.
5. Analyze the ZIP using the existing music-library CSV parser. Export local reports and optionally perform read-only Lidarr checks.

No automatic Takeout submission, Gmail read access, artist imports, album searches, or downloads are included in this preview. More advanced OAuth inbox monitoring will require a verified Google Cloud OAuth client; it is intentionally not simulated.

## Privacy and license

The app does not collect Google credentials or browser cookies, and the Lidarr API key remains in memory. See [`LICENSE`](LICENSE) for the proposed personal/noncommercial source-available terms (draft; legal review recommended).
