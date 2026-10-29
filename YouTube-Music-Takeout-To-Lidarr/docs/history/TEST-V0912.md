# v0.9.1.2 regression checklist

## Purpose
Reduce false rejections in targeted MusicBrainz ISRC recovery while never substituting unverified exact recording masters or overwriting conflicting MusicBrainz identifiers.

## Self-test (Windows, before publish)

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

`--self-test` includes the actual C# version-title and ISRC artist-credit fixtures. A source-only consistency check is **not** equivalent to running this test.

## Source regression fixtures

- One More Time with exact artist MBID and title: candidate may be accepted as a **provisional recording identity**, never exact-master confidence.
- Same display name but different known artist MBID: reject.
- A title with a trailing featured performer omitted by MB: hold unless MB's own artist-credit explicitly lists that performer.
- Song (Live) versus Song - Live: compatible qualifiers, allowed for provisional evaluation.
- Song (Live at Berlin) versus Song (Live at Paris): hold; venue/version differs.
- Song (Live) versus Song: hold; potential studio/live substitution.
- Song (Remix) versus Song (Radio Edit): hold.
- Stronger (What Doesn't Kill You) versus Stronger: hold; substantive parenthetical is not a removable version qualifier.
- MB artist response without artist-credit: mark incomplete, not a conflict.
- More than one compatible MB recording for an ISRC: hold; unique version not established.

## Test against the user's existing data

1. Load the original Takeout ZIP and connect to Lidarr.
2. Import latest matching `musicbrainz-songs-audit (12).csv` and `musicbrainz-artists-audit (12).csv`.
3. Do **not** run a fresh full-library MusicBrainz scan.
4. Run **Recover artist IDs and recording identifiers** once. MusicBrainz and Deezer are queried for the targeted cases.
5. Export `musicbrainz-identifier-recovery-debug.csv`, song audit and artist audit.
6. Run read-only Lidarr inventory comparison and export Migration Readiness CSV.
7. Compare 2,505 source rows, 951 primary artist credits, IDs, accepted candidates and conflicts against v0.9.1.1. Never interpret fewer manual-review labels alone as improved true accuracy.

## Operational constraints

- No automatic Lidarr artist writes, search commands, file downloads, or overwrites.
- No reuse of old matching results for the `Begin Fresh Verification` path; audit import must remain explicitly labelled.
- Never upgrade exact-master confidence based only on an ISRC or a formatted title similarity.
- Existing green centered wizard retained.
