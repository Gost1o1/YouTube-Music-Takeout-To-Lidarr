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

## Supported export data (v0.4.5)

- `music (library and uploads)/music library songs.csv`: **Song Title**, **Album Title**, **Artist Name 1..N**, **Video ID**. Multiple artists are retained as a collaboration credit; these names may need individual identity resolution before Lidarr matching.
- `music uploads metadata.csv`: artist, song, album metadata; doesn't prove audio was downloaded.
- Uploaded audio files are **counted separately**, never moved, renamed, or imported automatically.
- `clips.csv`, comments, watch history, general playlists, and Watch Later are **not music-library songs** and aren't added to the song total. Playlist video-only CSVs require a separate metadata enrichment pass.

The main `music library songs.csv` in the tested Takeout source inventory contained **2,505 records** (not 25). This version fixes their field recognition but hasn't yet been end-to-end verified on that original ZIP. A selected archive may not contain everything in a user's account, and Takeout ZIP parts can be split.

## Optional artist import into Lidarr (v0.4.5)

After the Takeout inventory completes, choose **option 6** in the PowerShell menu. The importer:

1. Reads existing Lidarr artists, IDs, and aliases before adding anything.
2. Shows installed-name coverage and asks you to select your own Lidarr root folder, quality profile and metadata profile.
3. Press **Enter** or type **Y** to confirm the candidate import after reviewing the warning; **N** cancels.
4. Adds only a **single exact normalized-name MusicBrainz candidate** absent from Lidarr. This is a *name-only* match and **not recording-level verification**; review before granting bulk permission. Multiple identities, collaborations, API failures, and missing results go to exceptions.
5. Opens `lidarr-import-progress.html` as a browser progress meter. The PowerShell process performs the work; the static browser page cannot invoke local scripts.
6. Writes `lidarr-artist-import-results.csv` after each artist. Rerunning against the **same report folder** can reuse those rows; new archive runs create fresh report folders and always check Lidarr again. No album searches or downloads are requested.

Be conservative with the bulk option: an exact artist name can still refer to the wrong person. The project does not yet automate recording-level corroboration, so do not assume every imported artist is proven to match a Takeout track. **Nothing is added without your explicit confirmation.**

## Reports and safety

Generated files include `dashboard.html`, `tracks.csv`, `artists.csv`, `source-files.csv`, `optional-youtube-queue.csv`, and optional Lidarr review CSVs. The API key is not written to reports. **No Lidarr imports, downloads, file moves, or searches are performed during inventory.** Artist imports and album searches are separate opt-in actions. Explicitly approved Lidarr album searches are an optional later step.

## License

See [LICENSE](LICENSE) for the **draft personal and noncommercial use terms**. This is source-available, not an OSI-approved open-source license. A legal review is recommended before relying on custom terms.


## Optional downloads and matching limits (v0.4.6)

After import, choose **option 7 — Download matched albums (review first)** in the *same PowerShell window*. The command reuses the Lidarr connection for this session and does not open Lidarr in the browser automatically. It scans the original Takeout tracks against installed artist names and Lidarr catalog track titles, writes `lidarr-track-review.csv` and `unmatched-youtube-review.csv`, deduplicates the eligible album IDs, and offers **two separate explicit confirmations** before requesting Lidarr `AlbumSearch` commands. Lidarr's configured indexers/download clients determine whether anything is acquired. One search may acquire an entire album, not just the selected track. **A submitted search is not a completed download.** No YouTube Music content is downloaded directly.

Crucially, after the first public import test, only **two artist credits were newly imported**, and over a thousand credits still needed identity review. That means the complete Takeout catalog is *not* yet eligible for reliable Lidarr downloads. Many saved tracks may not exist in Lidarr's metadata catalog. A report containing zero eligible albums is a truthful outcome, not permission to bulk search unrelated albums. Use the unmatched queue for manual review and future matching improvements.

This release **does not automatically download every saved song**, nor does it add albums to Lidarr's metadata. It is a safe starting point for a future recording-verified acquisition pipeline.

## v0.4.6 interface changes

- Only checks if the default Lidarr URL is reachable; does not automatically open the browser. Users can follow the on-screen steps: **Lidarr left sidebar → Settings → General → API Key**.
- Centers both the in-progress and finished Lidarr artist import page.
- Adds the optional **menu item 7** for exact-catalog Lidarr album search review and unmatched YouTube Music link export.
- Caches the API key only in the running PowerShell process. It is not written to reports.

