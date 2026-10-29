# v0.9.1.10 Windows validation

Run from the extracted source directory:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\YouTubeMusicToLidarr.exe
```

1. While the verification progress page is running, refresh the browser. It should return to the same ongoing verification without restarting MusicBrainz.
2. Once the pipeline completes, refresh again: it should return to Manual Review.
3. Under Artist Review, inspect `(G)I-DLE`. Its actual YouTube Music track should show `Light My Fire` and its album `Latata (Japanese Version)`, if this same source library is used.
4. Candidate IDs should no longer be used as card headings: actual artist names should be displayed when MusicBrainz responds. If MusicBrainz does not respond, the name should be explicitly unavailable.
5. When there is an exact Apple title+artist catalog match, album art and a playable ten-second audio clip may appear. Press the same play control again to stop and reset to 0:00. Never infer a studio/master identity from Apple preview alone.
6. Confirm "None of these" and Next still save review-only decisions, with no Lidarr writes.
7. Confirm Continue to Migration Preview, selection, local staging, and the GitHub/developer screens still work.

## Recover progress RIGHT NOW in your existing running v0.9.1.9

Do **not** close the .NET process just to apply this fix. On the current app page press F12 > Console and run:

```javascript
state.step = 6;
state.error = "";
render();
if (!unifiedPolling) {
  unifiedPolling = true;
  pollUnifiedPipeline();
}
```

This calls the existing `/api/pipeline/status` and restores the browser's progress view; it does not start a new scan. The automatic refresh recovery is in v0.9.1.10, not the existing executable.
