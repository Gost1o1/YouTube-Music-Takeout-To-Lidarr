# v0.9.1.9 Windows validation

1. In a **new** extracted source directory, run:

   ```powershell
   dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
   dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
   dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
   .\publish\YouTubeMusicToLidarr.exe
   ```

2. Supply an original, fresh Takeout ZIP, then connect to Lidarr. Ensure verification begins **without a second button click**, old-audit import is absent from the visible wizard, and no separate provider/recovery buttons appear.
3. Watch progress. It should display only overall/songs/artists progress, elapsed time, time remaining, and speed. No Current Stage or candidate audit report appears.
4. If a service fails, the pipeline must show a clear error rather than a successful completion.
5. After the fresh run, verify that Manual Review opens automatically; review candidate approvals and "None of these" without changing exact master certainty.
6. Open Migration Preview. Confirm Select All, counts, confirmation, local-plan progress, celebration, developer info and optional GitHub starring still work. **No Lidarr writes/downloads are expected in v0.9.1.9.**
7. Compare the new results with the v0.9.1.8 baseline: 2,505 songs, 951 artist identities; 2,212 song proposals, 293 songs awaiting review; 882 artist IDs high-confidence and 69 needing review. Record elapsed time, `/api/pipeline/status` metrics (including actual request count, retries and cache hits) and exported audit decisions. A result is an improvement only if independently supported.

## Offline checks already run

- JavaScript syntax for both UI scripts.
- Bracket/structure checks on the 15 C# source files.
- Existing source-level matching/safety checks and JavaScript migration-flow smoke fixture.
- Parser/route and baseline replay checks in `tools/check_v0919_unified.py`.
- ZIP integrity, checksum manifest and source-file verification.

Native .NET compilation and a live provider run **were not available** in the packaging environment.
