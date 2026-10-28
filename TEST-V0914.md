# v0.9.1.4 — Windows testing instructions

Run from the extracted repository directory, in order:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Stop if any command fails. Launch `publish\YouTubeMusicToLidarr.exe`, load the same original Takeout ZIP, connect to Lidarr, and choose Import Previous Evidence with:

- Song-by-Song Evidence: `musicbrainz-songs-audit (14).csv`
- Artist & Song Lists: `musicbrainz-artists-audit (14).csv`
- Optional Original Song Evidence: leave blank.

Choose **Recover Artist IDs and ISRCs** (this also runs the whole-album and new targeted single-song passes). Don't click Fresh Verification. This stage makes MusicBrainz/Deezer read-only requests and can take minutes; the request pacer/retries still apply. When completed export:

1. Song-by-Song Evidence CSV
2. Artist & Song Lists CSV
3. Detailed Recovery Diagnostics CSV
4. All Candidate Scores and Album Evidence CSV (`targeted-song-to-release` rows should be present)

Then run Compare Lidarr Track & File Inventory and export Migration Readiness Preview. **No Lidarr write operations occur.**

Check any automatically recovered identities to ensure the source title and credited artist MBID actually agree; the candidate CSV includes full/partial inventory reasons. The new song identities must remain *provisional*, not high-confidence exact masters.

Offline scope check on a machine with Python 3:

```powershell
python tools/offline_v0914_regression.py --data-dir "C:\path\to\prior\exports" --output offline-replay-v0.9.1.4.json
```

The offline script verifies which source rows are eligible. It cannot predict how many new MusicBrainz identities the live API will corroborate.
