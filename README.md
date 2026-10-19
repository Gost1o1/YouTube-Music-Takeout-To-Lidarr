# YouTube Music Takeout to Lidarr — v0.5.9 guided export and ZIP detection update

A Windows desktop wizard that guides users one step at a time through choosing their Google account, requesting a YouTube Music Takeout archive, locating the ZIP, and generating a local music inventory.

## Launch (regular Windows PowerShell, no Administrator required)

Review the repository before executing remote code.

```powershell
$u='https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/archive/refs/heads/main.zip';$d=Join-Path $env:LOCALAPPDATA 'YouTubeMusicTakeoutToLidarr';New-Item -ItemType Directory -Path $d -Force|Out-Null;Invoke-WebRequest $u -OutFile "$d\repo.zip" -UseBasicParsing -ErrorAction Stop;Expand-Archive "$d\repo.zip" "$d\source" -Force;$s=Get-ChildItem "$d\source" -Filter Start.ps1 -Recurse|Select-Object -First 1;powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $s.FullName
```

**Startup behavior:** The native desktop window now runs in the visible, attached PowerShell process. This prevents the older hidden-child startup from silently failing. Keep the PowerShell window open while the app is running; closing the GUI returns you to the prompt. An automatic console-close feature can be considered after Windows validation. No Administrator privileges are needed.

## Implemented desktop flow in v0.5.9

1. **Choose Your Data** offers **Choose Google Account** or **Downloaded Data File**. Clicking Choose Google Account first shows export instructions; it does **not** open the browser prematurely.
2. **Prepare Your YouTube Music Export** explains Google's **Next step → File size: 50 GB → Create export** flow; file size is a maximum ZIP-part size, not an expected download size. The app explains that Google may take minutes to days and usually emails both a request confirmation and a ready-to-download notification.
3. **Continue to Google Takeout** opens [the YouTube-specific Takeout link](https://takeout.google.com/settings/takeout/custom/youtube), then displays **Find Your Downloaded Music Data**.
4. **Scan for Takeout File** searches the user's Downloads folder for newest `takeout*.zip` files containing recognizable YouTube Music CSV records; the user confirms before use. If no recognized ZIP exists, or the user declines it, a Windows file picker opens. The original archive stays untouched.
5. After ZIP selection, click **Analyze Takeout** in the existing desktop workspace. The existing Takeout parser and read-only Lidarr comparison remain available.

**Scope notice:** The extended artist/recording verification, comparison artwork, 10-second licensed audio clips, background artist imports, release-search approvals, retries, and final migration report dashboard remain **planned work**, not working features in this desktop build. Connecting to Lidarr does not add music. See [`IMPLEMENTATION-PLAN.md`](IMPLEMENTATION-PLAN.md) for the approved behavior and delivery stages.

## Launch

Run the single PowerShell launch command above. Keep the PowerShell console open while the app runs to preserve error messages. No Administrator privileges or Gmail access are required.

## Privacy and license

The app does not collect Google credentials or browser cookies, and the Lidarr API key remains in memory. See [`LICENSE`](LICENSE) for the proposed personal/noncommercial source-available terms (draft; legal review recommended).


## v0.5.5 Takeout account routing
The **Choose Google Account** button opens the official YouTube-specific Google Takeout URL directly and advances the desktop wizard to the export guidance step. If Google has an active session for a different account, use the account avatar at the top right of the Takeout page to switch accounts and verify YouTube and YouTube Music are the only selected product. The program does not access Google credentials or automate export submission.


### v0.5.9 wizard correction
A valid Takeout ZIP now starts analysis without exposing the legacy workspace. Analysis results appear in a numbered song-preview screen and proceed to Lidarr setup. The legacy read-only workspace remains accessible after connection, while later matching/import stages remain planned rather than implemented.

## v0.5.9 analysis completion fix

A successful Takeout analysis transitions to the library summary even if PowerShell wrote nonfatal warnings to stderr. Actual failures still show diagnostics. No modifications to Lidarr or download operations are performed by this fix.
