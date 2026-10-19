# YouTube Music Takeout to Lidarr

Inventory a **fresh Google Takeout music library**, then review exact artist/track matches in your own Lidarr instance. No account-specific paths, Docker dependency, or embedded credentials.

## One-command launch (Windows PowerShell)

Paste this entire line into **regular Windows PowerShell**. Review the repository before running downloaded code:

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip';$d=Join-Path $env:TEMP ('YTMusic-'+[guid]::NewGuid().ToString('N'));try{New-Item -ItemType Directory -Path $d -Force|Out-Null;Invoke-WebRequest -Uri $u -OutFile "$d\app.zip" -UseBasicParsing -ErrorAction Stop;Expand-Archive -LiteralPath "$d\app.zip" -DestinationPath "$d\app" -ErrorAction Stop;$s=Get-ChildItem "$d\app" -Filter Start.ps1 -Recurse|Select-Object -First 1;if(-not $s){throw 'Start.ps1 not found'};powershell.exe -NoProfile -ExecutionPolicy Bypass -File $s.FullName}finally{Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue}
```

The process-level execution-policy bypass does **not** change your persistent Windows policy. Administrator access is **not required**. Production releases should pin verified tags/checksums rather than the changing `main` branch.

## How it works

1. Opens [Google Takeout](https://takeout.google.com/settings/takeout). Choose **YouTube and YouTube Music** and request your export. Google processes the request; the app does not request exports on your behalf.
2. After the ZIP is downloaded, press Enter. The tool detects `takeout-*.zip` in Downloads; if not found, a Windows file picker opens. The user confirms which archive to use.
3. The chosen ZIP is extracted in a **temporary folder**. Source inventory is rebuilt from this archive every run; nothing is reused from earlier Lidarr matching sessions.
4. The progress page opens and redirects to the local dashboard when complete. Results are preserved under Documents/MusicTakeoutToolkit.
5. Optionally connect your own Lidarr instance for **read-only track review**. Album searches require two explicit approvals. Lidarr may download complete albums rather than individual songs.
6. Unmatched songs with known source video IDs are exported as reviewable YouTube Music links, not automatically downloaded.

## Supported export data (v0.4.3)

- `music (library and uploads)/music library songs.csv`: **Song Title**, **Album Title**, **Artist Name 1..N**, **Video ID**. Multiple artists are retained as a collaboration credit; these names may need individual identity resolution before Lidarr matching.
- `music uploads metadata.csv`: artist, song, album metadata; doesn't prove audio was downloaded.
- Uploaded audio files are **counted separately**, never moved, renamed, or imported automatically.
- `clips.csv`, comments, watch history, general playlists, and Watch Later are **not music-library songs** and aren't added to the song total. Playlist video-only CSVs require a separate metadata enrichment pass.

The main `music library songs.csv` in the tested Takeout source inventory contained **2,505 records** (not 25). This version fixes their field recognition but hasn't yet been end-to-end verified on that original ZIP. A selected archive may not contain everything in a user's account, and Takeout ZIP parts can be split.

## Optional artist import into Lidarr (v0.4.3)

After the Takeout inventory completes, choose **option 6** in the PowerShell menu. The importer:

1. Reads existing Lidarr artists, IDs, and aliases before adding anything.
2. Shows installed-name coverage and asks you to select your own Lidarr root folder, quality profile and metadata profile.
3. Requires the exact typed approval `IMPORT N ARTISTS`, where N is the count of unique artist credits.
4. Adds only a **single exact normalized-name MusicBrainz candidate** absent from Lidarr. This is a *name-only* match and **not recording-level verification**; review before granting bulk permission. Multiple identities, collaborations, API failures, and missing results go to exceptions.
5. Opens `lidarr-import-progress.html` as a browser progress meter. The PowerShell process performs the work; the static browser page cannot invoke local scripts.
6. Writes `lidarr-artist-import-results.csv` after each artist. Rerunning against the **same report folder** can reuse those rows; new archive runs create fresh report folders and always check Lidarr again. No album searches or downloads are requested.

Be conservative with the bulk option: an exact artist name can still refer to the wrong person. The project does not yet automate recording-level corroboration, so do not assume every imported artist is proven to match a Takeout track. **Nothing is added without your explicit confirmation.**

## Reports and safety

Generated files include `dashboard.html`, `tracks.csv`, `artists.csv`, `source-files.csv`, `optional-youtube-queue.csv`, and optional Lidarr review CSVs. The API key is not written to reports. **No Lidarr imports, downloads, file moves, or searches are performed during inventory.** Artist imports and album searches are separate opt-in actions. Explicitly approved Lidarr album searches are an optional later step.

## License

See [LICENSE](LICENSE) for the **draft personal and noncommercial use terms**. This is source-available, not an OSI-approved open-source license. A legal review is recommended before relying on custom terms.


## v0.4.3 usability fixes

- **Enter** (or `Y`) approves the selected Takeout ZIP; `N` goes back.
- The initial archive-audit waiting page is centered.
- The read-only dashboard shows a concise library status instead of instructions for buttons it cannot actually run.
- Lidarr API key entry uses a Windows password input dialog that supports pasting the entire key; nothing is saved to reports.
- All artist imports remain optional and require approval.

## Lidarr connection (v0.4.3)

Choose option 5 or 6. The tool first checks `http://127.0.0.1:8686`, opening the URL when reachable. Otherwise it asks for your Lidarr URL. Find the API key in **Settings > General > API Key**. A Windows dialog provides a **Paste** button and an optional **Show key** checkbox. The API key is not saved in reports. Cancelling returns without changing Lidarr. Album searches and artist imports remain separate, opt-in operations.
