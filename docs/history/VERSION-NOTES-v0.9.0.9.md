# v0.9.0.9 — Album aliases and safe album-only action plans

## Scope

Focused follow-up to the user-observed v0.9.0.8 comparison. **Lidarr remains read-only**; this source does not send artist-add, album-search, import, download, PUT, POST or DELETE operations to Lidarr. Selecting an album is only planning consent, NOT authorization for an eventual Lidarr search.

## Fixes

1. **174 existing album-only proposals were missing from the proposed action plan.** The previous exporter demanded both a Lidarr album ID *and* track ID before it could propose a search. For 174 recording-ID conflicts, the app had deliberately selected only the album. The exporter now deduplicates and proposes an *album-only* search target without selecting or replacing the disputed recording MBID. It records `Album-only target`, `Exact recording ID corroborated` and `Album target evidence` as additional trailing columns.
2. **Album-title alias review** recognizes a small set of explicitly typed relationships: `Vol.` vs `Volume`, suffixes such as `(Deluxe Edition)`, `(Complete Edition)` and `(20th Anniversary Edition)`, specific edition subtitles, and the literal `º` vs `°` symbol variant. Every newly recognized alias still requires an explicit album choice; no recording ID is accepted from title resemblance. Other, unrelated titles stay in Manual Review.
3. **Symbol-title collision protection** rejects comparisons that became equal only because generic normalization removed all characters.
4. **Observability:** New counters for pending album-title aliases and album-only search targets. `lidarr-release-candidates.csv` now has `Album title relationship` and more explicit MBID-disagreement text. The new action-plan trailing fields distinguish album-only from exact-track proposals.
5. Added `tools/offline_v0909_regression.py` to verify expected corrections against the private user CSVs without adding those CSVs to GitHub or repeating a MusicBrainz scan.

## Measured result: offline projection (not a live .NET run)

Using the supplied v0.9.0.8 `migration-readiness-preview (5).csv`, `lidarr-release-candidates (2).csv`, and `lidarr-proposed-action-plan-NO-WRITES (1).csv`:

- Source songs: **2,505**
- Previous Manual Review: **349**
- Additional album-title alias choices: **22 songs**
- Projected Manual Review: **327** (those 22 now pending album choice instead of unresolved release relationship)
- **174** existing album-only targets: fixed exporter behavior, **not** additional identity matches and **not** an additional 174-song review reduction
- Artists requiring review: **71**, unchanged; deeper independent metadata evidence is still required

The offline benchmark is a replay of export fields, not execution of the .NET matcher. Windows builds, .NET self-tests and Lidarr outcomes **have not been verified in this authoring environment**.

## Testing

Build on Windows:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Then launch, scan the **same** Google Takeout ZIP, connect to Lidarr, import **song audit `(9)` and artist audit `(9)`** and run only **Compare Lidarr Track & File Inventory**. Do not run a full verification. Export Migration Readiness, All Lidarr Release Candidates, Album Review Groups, and Proposed Action Plan. Check that:

- The proposed-action-plan exporter no longer categorizes all 174 eligible album-only targets as `hold-for-metadata-or-manual-review`.
- Alias entries such as `The Best of Nickelback, Vol. 1` / `... Volume 1` are reviewable as album groups but not accepted as exact recording IDs.
- Unknown artists, unrelated releases and conflicting existing trackfiles still remain held.
- No API call changes the contents of Lidarr.
