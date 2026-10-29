# v0.9.1.0 test instructions

## Windows build and self-test (run separately, stop on any error)

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

1. Start `publish\YouTubeMusicToLidarr.exe`; leave terminal open in this development build.
2. Scan the original Takeout archive and connect to Lidarr.
3. Import `musicbrainz-songs-audit (10).csv` and `musicbrainz-artists-audit (10).csv`, if those are your latest aligned exports. Do not run a fresh MusicBrainz verification.
4. Run the read-only **Compare Lidarr Track & File Inventory**. The count of manual-review songs should fall by up to five versus 332, moving only the known EP cases to explicit release choice. Exact result depends on Lidarr metadata.
5. Review the EP album groups for Skrillex / Bangarang EP, Green Day / 21 Guns EP and Krewella / Play Hard EP. Do not assume the standard release is automatically correct.
6. In the local staging queue, select ONE safe album, stage it, export the no-writes queue CSV, unstage it and confirm the CSV changes. Refreshing/reimporting the inventory must clear staged selections. Do not expect a Lidarr search or download.
7. Export `lidarr-manual-review-triage.csv` and `lidarr-proposed-action-plan-NO-WRITES.csv`; ensure every row has `Final search approved = NO` and `Lidarr write executed = NO`.

Offline replay: `python tools/offline_v0910_regression.py --readiness <v0.9.0.9 readiness csv> --candidates <v0.9.0.9 candidates csv> --plan <v0.9.0.9 no-writes plan csv>`.

## Safety

Never manually substitute a differing MusicBrainz recording MBID simply because album titles resemble each other. Staging happens locally in memory; it is not a download or authorization to modify Lidarr.
