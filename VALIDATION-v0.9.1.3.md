# v0.9.1.3 validation report

## Source and offline checks that actually ran in this environment

- Confirmed latest saved source audits contain **951 source artists** and **2,505 source song rows**, and the migration readiness report contains **303 manual-review songs**.
- Replayed candidate eligibility from the uploaded v0.9.1.2 files. **27 unresolved artist names** have one to four candidate MBIDs worth targeted testing; eight with wider ambiguous lists are deliberately excluded from automatic confirmation. The source data has **13 unresolved multi-song artist/album groups** eligible for review. Some require a verified artist first.
- Verified all newly changed C# source files have balanced code brackets after removing comments/string literal tokens (a structural check, **not a compiler**).
- Verified the browser's JavaScript parses with Node.js, and the candidate-scoring button is connected to a server export route.
- Verified ZIP file readability and SHA-256 file hashes after packaging.
- Added C# `CatalogCorroborationSelfTests()` covering positive and wrong artist credits, exact version checks, album edition variants, tracklist evidence and a NO-WRITES CSV export. These C# tests **have not run here**.

## Not verified here

- Native .NET compilation and Windows execution: the .NET 8 SDK is unavailable and the execution environment cannot reach Microsoft's distribution site.
- Live MusicBrainz responses and actual Lidarr comparisons: no direct access to the user's Lidarr or live music-catalog services from the build environment.
- Net reduction in unresolved counts: **unknown** until the Windows recovery workflow runs against live candidate records. Candidate scores are heuristics, not calibrated match probabilities.

## Safety invariants

- No addition/search/download request sent to Lidarr; all Lidarr operations continue to be read-only.
- No artist alias alone, name alone or release-group alias alone can justify a confirmed recording identity.
- Existing conflicting artist or recording MBIDs are never overwritten.
- Incompatible version descriptors are not silently normalized away.
- Incomplete release inventories remain diagnostic/review-only.
- No external provider secrets or previously uploaded user CSV files are bundled in the ZIP.

## Required Windows smoke test

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Use latest `(13)` audit CSVs and the existing Takeout ZIP to re-run *targeted* identifier recovery only. Export all four diagnostic/audit CSVs plus a fresh migration readiness CSV. No full MusicBrainz library scan is required.
