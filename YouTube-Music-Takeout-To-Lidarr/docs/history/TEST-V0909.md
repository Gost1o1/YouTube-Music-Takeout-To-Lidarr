# v0.9.0.9 — Verification checklist

### Windows build (mandatory)

Run individually; stop if one fails:

1. `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`
2. `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`
3. `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`

Keep `publish/AppUI/` alongside the executable for this development release.

### Existing evidence, not another MusicBrainz run

Use the original Takeout ZIP and `musicbrainz-songs-audit (9).csv` and `musicbrainz-artists-audit (9).csv` to import previous evidence. No need to repeat MusicBrainz, Apple or Deezer requests. Connect to the existing Lidarr server and click Compare Lidarr Track & File Inventory.

Export:

- `migration-readiness-preview.csv`
- `lidarr-release-candidates.csv`
- `lidarr-album-review-groups.csv`
- `lidarr-proposed-action-plan-NO-WRITES.csv`

The 2,505-row source data may require reading track metadata from Lidarr again (read-only). Confirm that the new proposal groups preserve original MBIDs, show no actual downloads, and that zero downloaded files remains expected for this particular Lidarr library.

### Offline replay for maintainers

```powershell
python tools/offline_v0909_regression.py --readiness "C:\path\migration-readiness-preview (5).csv" --candidates "C:\path\lidarr-release-candidates (2).csv" --plan "C:\path\lidarr-proposed-action-plan-NO-WRITES (1).csv"
```

These private CSVs are regression inputs and must not be bundled into a public GitHub release.
