# v0.9.0.5 regression — large album libraries

1. `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`, then `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`.
2. Open with the original Takeout ZIP, connect Lidarr, import prior song audit `(6)`, artist audit `(6)`, optionally song audit `(5)` to repair older evidence. **Do not run new MusicBrainz verification.**
3. Start the read-only Lidarr inventory comparison. Its diagnostics must show **no request** to bare `/api/v1/album`; every album request is `/api/v1/album?artistId=<positive local ID>`.
4. Verify artists checked advances above 0, three successful API requests per artist, and expected progress. If any scoped artist response exceeds 80MB, stop and export diagnostics (never raise/remove the safety cap without explicit investigation).
5. If complete, export `migration-readiness-preview.csv` and inspect representative Lidarr tracks/file records against your Lidarr UI. Otherwise export `lidarr-inventory-diagnostics.csv` for the endpoint and outcome.
6. No API keys or private Takeout paths should appear in the diagnostic export. No Lidarr writes allowed.

---

# v0.9.0.4 resilience test checklist

1. Windows: `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64` and `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test` must succeed before proceeding.
2. Import the previous (6) audit exports and optional (5) baseline. Do not run MusicBrainz verification again.
3. Start the inventory comparison. Watch **Artists checked**, **Requests completed** and **Automatic retries**; verify they update while running. Confirm the UI is still centered and green.
4. After success, export `migration-readiness-preview.csv` and ensure song rows have evidence-backed track/file classifications, not all NOT CHECKED.
5. If the process fails, export `lidarr-inventory-diagnostics.csv` and inspect final error row and the endpoint/attempt preceding it. Timeout must be described as timeout, HTTP 400 as HTTP 400, not merely "interrupted".
6. Never expose the Lidarr API key. No modification or download actions should occur.

**Validation limitations:** the project has not been compiled with .NET in the build environment used to prepare this ZIP, and no Lidarr instance was available for an integration test.

---

# v0.9.0.3 inventory API regression

Lidarr's `/api/v1/track` **requires** at least one of `artistId`, `albumId`, `albumReleaseId`, `trackIds`. `/api/v1/trackfile` likewise requires an artist, album, trackfile ID list, or `unmapped` flag. Both endpoints intentionally respond with 400 to a no-filter GET. This version fetches tracks/files by verified *local Lidarr* `artistId`; artist MusicBrainz GUIDs cannot be passed as this numeric parameter.

1. Extract into a **new** folder (do not overlay the old version). Build: `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`.
2. Only after build succeeds, run: `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`.
3. Only after tests pass, publish: `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`.
4. Use the same original Takeout ZIP and imported `(6)` artist/song audit CSVs, and optional `(5)` song baseline. No fresh MusicBrainz verification.
5. Click **Compare Lidarr Track & File Inventory**. Expect the status to count *N of N validated artists checked*; each queried Lidarr path includes `?artistId=N`.
6. The comparison must either finish fully or show the specific HTTP request route and status; *no partially finished result should be exported as completed*.
7. Export migration readiness after a successful comparison and inspect representative `Already in Library` decisions against the actual Lidarr UI. This is a read-only database comparison, not a disk scan.
8. Save any failure status or sanitized error text. Never share an API key.

# v0.9.0.2 validation

1. Run `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64` and correct any errors before running other commands.
2. Run `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`. Tests include CSV evidence import/repair, provider matching, ISRC parsing, and Lidarr file comparison cases.
3. Open the app; load the original Takeout ZIP and connect the existing local Lidarr instance.
4. Under **Already completed a previous scan?**, choose song audit (6), artist audit (6), and optionally earlier song audit (5) to restore lost evidence. Import. The user should see how many rows were repaired. This import makes zero online catalog requests.
5. Export both audits and verify the fields `Album agrees`, `Multiple recording versions`, `Unique source songs`, `Deezer ISRC`, `Artist ID recovery status` and `Prior evidence restored` are retained.
6. Click **Compare Lidarr Track & File Inventory**, wait for completion, and export **Migration Readiness Preview**. Confirm all 2,505 song rows have one of the three decisions; already-in-library means Lidarr reported a matching file record. Validate representative rows directly against Lidarr UI.
7. In the event of a failed Lidarr API response, the app must not mark missing file records as absent—it must keep song comparison **NOT CHECKED**.
8. Do not use manual Lidarr write functions; the release does not implement them.

If tests fail, send the complete build/self-test error output, plus sanitized CSV exports. Never include Lidarr API keys in reports.

---

# v0.9.0.1 hotfix — first check

Run just this command from the extracted source folder:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
```

Only if it reports `Build succeeded`, run:

```powershell
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The two previously malformed C# strings are fixed. No claim of a successful native build is made until these Windows checks pass. Do **not** run the multi-hour MusicBrainz verification again just to test this source fix.

---

# v0.9.0 — Test checklist

1. `dotnet build ...`, then `dotnet run ... -- --self-test`. Confirm all PASS messages. Do not proceed to multi-hour scans after a build failure.
2. Import the exact matching v0.8.9 song and artist CSV exports plus the original Takeout ZIP. Confirm 2,505 song rows and 951 primary artist names. Imported, not fresh, must be visible.
3. Export both audit CSVs immediately. Confirm Deezer track IDs and Apple/Deezer status still match the imported exports; v0.8.9 previously dropped these.
4. Run identifier recovery once. Record artist lookups completed, candidates, ISRCs found, validated artists, songs recovered, and conflicts.
5. Inspect `musicbrainz-identifier-recovery-debug.csv` for one row per attempted unresolved artist and track-ISRC research event. Ensure no Lidarr API key, local Takeout ZIP path or user secrets appear.
6. Inspect the updated artist/song CSVs. Reject cases where a disputed artist or a version conflict is incorrectly labeled high-confidence.
7. Inspect `migration-readiness-preview.csv`. All songs not in manual review must say `Pending Lidarr track/file comparison / Songs` with `NOT CHECKED` for actual file presence. A Lidarr artist name does not mean a downloaded track exists.
8. Verify a *new* fresh scan starts without reusing this imported evidence. No Lidarr writes or downloads should occur in this version.

Expected safety invariants: missing ISRC and network errors stay unresolved; repeated/same ISRC recordings don't establish an exact audio version; a conflicting existing artist MBID blocks replacement; alias matching is exact normalized name/alias and requires source song corroboration.
