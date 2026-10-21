# YouTube Music Takeout to Lidarr — v0.8.7 source

Windows x64, .NET 8. This package is complete **source**, not a ready-to-run executable. The original ten-step wizard remains, now with a Gost1o1-inspired forest/neon-green palette and centered progress/ETA details. MusicBrainz verification is fresh per run. No Lidarr write operations are enabled yet.

## Build on Windows

Open PowerShell in the extracted source folder:

```powershell
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Run `publish\YouTubeMusicToLidarr.exe` and leave the terminal running. The `publish\AppUI\index.html` file must remain next to the executable in its `AppUI` folder. No one-click portable release has been built yet.

## Changes from v0.8.6

- Brand colors changed to dark green, neon green, and white while preserving the centered wizard layout; logo asset included but no logo embedding or final creator page yet.
- Live elapsed time, observed first-pass throughput, approximate remaining time and current phase are shown on Step 6. During second pass the estimate uses the measured second-pass rate and recalculates after 5 tracks. Retries, varying query counts and server rate limits make the ETA approximate.
- Song audit distinguishes an exact album that uniquely identifies **one of the retrieved matching recording candidates** from album overlap where multiple recording versions remain. This is not a full release catalog lookup, and no complete MusicBrainz catalog is claimed.
- Artist confidence considers independently corroborating exact artist+song evidence without treating unrelated version-uncertain tracks as identity conflicts. Multiple distinct confirmed artist IDs still block high confidence.
- New song-audit CSV field indicates when the source album uniquely selects a candidate among the retrieved set. Previous fields remain in their earlier positions until the final new field.

## Current limitations

The application has **not been compiled or live-tested in this environment**. It has not been tested against current MusicBrainz API responses. The release does not implement full paginated catalogs, additional release-detail requests, manual review, Lidarr imports, completed downloads or portable one-click EXE behavior. An artist ID in Lidarr is corroboration but may reflect an earlier manual identification; do not regard the audit heuristics as measured accuracy probabilities.

After a Windows test, export both audit CSVs and compare against v0.8.6; do not assume unresolved counts improve without measurement. Keep API keys and personal Takeout exports out of Git history.
