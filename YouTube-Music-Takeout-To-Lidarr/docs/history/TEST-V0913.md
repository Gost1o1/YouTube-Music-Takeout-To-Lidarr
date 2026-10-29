# v0.9.1.3 Windows test plan

Run one command at a time and stop on any error:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

1. Open `publish\YouTubeMusicToLidarr.exe`. Keep the terminal open.
2. Use the same original Takeout archive and the connected Lidarr instance.
3. Import `musicbrainz-songs-audit (13).csv` and `musicbrainz-artists-audit (13).csv`, **not a fresh full scan**.
4. Select **Recover Artist IDs and ISRCs** (now runs the new artist candidate and album group checks too).
5. On completion export:
   - Song-by-Song Evidence CSV
   - Artist & Song Lists CSV
   - Detailed Recovery Diagnostics CSV
   - **All Candidate Scores and Album Evidence CSV**
6. Run **Compare Lidarr Track & File Inventory** and export Migration Readiness Preview.
7. Check especially `SIAMES`, `Ke$ha`, `Marina and The Diamonds`, and `Katy Perry — MTV Unplugged (Live)`. Only credit-supported artists and compatible album/recording versions can improve automatically.
8. Verify that existing MBIDs never change when they conflict with new candidate IDs. No Lidarr searches or file writes should occur.

## Offline source-data replay

```bash
python tools/offline_v0913_regression.py --data-dir <directory-containing-latest-CSV-exports> --output offline-replay-v0.9.1.3.json
```

This checks source row counts and selects bounded candidate cases; it does **not** replace live API or .NET self-tests.

## Expected reporting

Please return the refreshed song audit, artist audit, candidate-scoring audit, identifier-recovery debug log, and migration-readiness CSV. The next version should be driven by the actual top-scoring rejected candidates, not by arbitrary thresholds.
