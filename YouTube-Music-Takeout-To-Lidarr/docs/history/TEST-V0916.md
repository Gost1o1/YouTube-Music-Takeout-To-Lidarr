# v0.9.1.6 Windows test plan

Extract this repository into a new folder and open PowerShell there. Run **one command at a time**:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Stop if a command fails. Launch **this folder's** `publish\YouTubeMusicToLidarr.exe`. Confirm **v0.9.1.6** is visible in the web UI.

1. Load the same YouTube Takeout archive and connect to your existing Lidarr.
2. Under Previous Evidence, import `musicbrainz-songs-audit (17).csv` and `musicbrainz-artists-audit (17).csv`; leave the optional original evidence blank.
3. Run **Recover Artist IDs and ISRCs** (the targeted recovery that also does release discovery). **Do not perform a full verification.**
4. Export Song-by-Song Evidence, Artist Audit, Detailed Recovery Diagnostics, and **All Candidate Scores and Album Evidence (CSV)** using the visible export button.
5. Run read-only **Compare Lidarr Track & File Inventory** and export Migration Readiness.
6. Compare new candidate scoring output with v0.9.1.5 for `Closure — I'm Gone` and `Sam Tinnesz — Ready Set Let's Go`. Check for `count=`, `read=`, and absence of `invalid-search-count` in status details.

Expected baseline before targeted lookup: 2,505 songs; 951 artist names; 2,206 addition proposals; 299 manual-review songs; 68 artist reviews. **These are prior observed results, not promised v0.9.1.6 results.**

The new recording-first results are review-only, and no source row will be changed by that fallback. A successful search is not proof of an exact audio master.
