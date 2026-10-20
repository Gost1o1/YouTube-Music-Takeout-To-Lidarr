# YouTube Music Takeout to Lidarr — v0.8.5

.NET 8 Windows x64 source. Retains the 10-step v0.8.x web interface and read-only integration with Google Takeout ZIPs and local Lidarr. No PowerShell execution and no automatic Lidarr imports.

## Build

Extract the archive, open PowerShell **inside the extracted folder** and run:

```powershell
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Run `publish\YouTubeMusicToLidarr.exe`; leave its console open while browsing the local application. The HTML lives in `publish\AppUI\index.html` and is packaged as content alongside the executable.

## New evidence audits

After finishing verification, the Step 6 screen offers two CSV downloads:

- **Song-by-song evidence** includes source title, source artist, source album and URL, candidate MusicBrainz recording/artist IDs, evidence flags, unresolved causes, and alternative artist IDs.
- **Artist and song lists** includes separate artist confidence, distinct source song counts, named corroborated titles, named unresolved titles, and overlap with the currently connected Lidarr artist inventory.

Artist **high confidence** requires *at least three distinct source song titles* independently supporting the same MusicBrainz artist ID with no conflicting identified artist IDs. Song **high confidence** requires evidence for exact normalized title, credited artist and either a unique artist recording candidate or corroborating album evidence for multiple versions. **These are deliberately conservative rules**, not calibrated correctness probabilities.

### Important catalog-coverage limitation

This version **does not fetch the entire recording catalog for every MusicBrainz artist**. A recording search for a Takeout title cannot prove which other songs exist in an artist's complete MusicBrainz catalog. The artist report marks catalog coverage as *partial*, and sets MusicBrainz-wide song totals and MusicBrainz-only extra titles to *Unknown / Not assessed*. Source-only titles means *not high-confidence verified in this run*, NOT necessarily nonexistent in MusicBrainz.

The confidence audit can reduce false automatic matches, so high-confidence totals may be **lower** than the previous v0.8.4 verified totals. This is expected and safer than inflating them.

## Fresh scans and privacy

Each verification begins fresh. Data remains in process memory, with no persisted matching decision cache. Audit CSV files are downloaded only on request. No artist/album import or download is enabled. Keep your Lidarr API key private.

## Diagnostics

`Documents\MusicTakeoutToolkit\dotnet-logs\desktop.log`. Do not paste your API key.

## Runtime caveat

This source package has not been compiled or tested on Windows in the generating environment. If the C# build reports an error, share the complete error lines. Never override Microsoft Defender to run a flagged binary.
