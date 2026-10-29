# v0.9.1.7 validation report — source package

## Source-level checks completed

- 13 C# files inspected with lexical bracket/parenthesis/brace checks; all balanced. These checks do **not** compile C#.
- Full inline browser JavaScript extracted and validated using `node --check`.
- The actual UI script was executed under a mock DOM in Node, including a Step 6 guided-review item, with a candidate picker, visible version label, and HTML escaping check.
- Verified API/UI routes and registration: `/api/review/queue`, `/api/review/decision`, `/api/review/decisions.csv`, `/api/review/reapply-album`, `/api/review/forget-album`.
- Verified candidate historical CSV input field `candidateScoresCsv`, source-row and artist alignment validation, and review-only handling.
- Verified review-decision JSON atomic save path; user choices cannot write a recording/artist MBID into the verification audit or call a Lidarr write API. Reapplying an album uses the existing full-group coverage validator and is an explicit user action.
- Verified Windows CI includes the new `GuidedReviewV917.cs` file, version names, and existing Windows build/self-test/publish steps.
- Existing imported audit baseline replay: **2,505 songs**, **951 artists**, **297 manual-review songs**. Historical candidate CSV has **294 rows**, all 294 aligned with their source artist and source row(s).
- One additional MusicBrainz 503 recovery attempt added after the existing three-attempt client retry for the narrow recording-first review-only pass. No broad rescan is triggered.

## Native/runtime validation limitations

- `dotnet` SDK is not installed in this container. C# compilation and all `--self-test` cases **cannot be executed here**. New tests cover review-key reproducibility, candidate MBID validation, source-row import mismatch rejection, historical review-only flags, JSON round-trip, and album keys. They are unverified until Windows execution.
- No live MusicBrainz HTTP calls or Lidarr comparisons were run in the packaging environment.
- No claims are made about reducing the previous **297 manual-review songs** or **68 manual-review artists**. Selecting review candidates is intentionally non-authoritative.
- Saved decisions live in the user's Documents directory, not the source ZIP. For transfer to a different machine, back up `Documents/MusicTakeoutToolkit/review-decisions-v1.json` manually; the CSV export is informational.

## On Windows

Run sequentially:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Confirm the output includes `PASS: v0.9.1.7 review keys` and no failed self-tests. Launch the new `publish/YouTubeMusicToLidarr.exe` only and confirm the browser says v0.9.1.7.
