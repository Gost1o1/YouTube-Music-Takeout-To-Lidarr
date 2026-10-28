# v0.9.1.7 — Windows testing checklist

Run commands separately from the extracted ZIP folder:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Check the self-test output contains `PASS: v0.9.1.7 review keys` and prior eight pass groups. Stop if there is any build failure. Launch **publish\YouTubeMusicToLidarr.exe**, not a previous executable; the browser must show **v0.9.1.7**.

1. Choose the previously downloaded Takeout and connect to your existing Lidarr, then import `musicbrainz-songs-audit (18).csv` and `musicbrainz-artists-audit (18).csv` as previous evidence. Optionally import `musicbrainz-candidate-scoring-audit (1).csv` to restore previous candidate findings for the guided queue without running any searches. Optional original song baseline may be left blank. **Do not start a full verification**.
2. Click **Load Guided Review Queue**. Before Lidarr comparison this uses unresolved song identities and non-high-confidence artists. After comparison click **Refresh Guided Review Queue** to focus on the actual manual-review bucket. For the provided baseline there should be **68 unresolved artists and 297 manual-review songs** after comparison, subject to live Lidarr changes.
3. Filter for an unresolved artist, save **Needs additional research** with a brief note. Refresh and confirm that it persists. Select one of the displayed MBIDs for **Candidate selected for planning only**, save, and export **Saved Review Decisions — NO WRITES CSV**. Confirm the audit and Lidarr proposal counts do not change merely because of this selection.
4. Reopen the EXE from the same or a new folder. Re-import the same audits, load the review queue and confirm the saved note/selection survives. The actual JSON is stored under `Documents\MusicTakeoutToolkit\review-decisions-v1.json`.
5. Change a saved song's source title/album in *test fixtures only*, or use a different evidence set: ensure no saved decision is silently transferred to a changed source row. If a candidate disappears from the current evidence, the view should flag it stale.
6. Run the read-only Lidarr comparison. Choose an album group with complete evidence and approve it for **planning only**. Refresh the app inventory later; its saved album ID should appear with a **Reapply saved choice** button only if the new group still supports that exact album. Clicking reapply must not write to Lidarr. A changed or removed album must not reapply.
7. Run optional **Recover Artist IDs and ISRCs** only if you want fresh targeted research. The MusicBrainz candidate report retains provider availability reasons, including `503-retry` where applicable. This step is *optional* for testing the review notebook.
8. Export Song Audit, Artist Audit, Candidate Scores, Manual Review, Migration Readiness and Review Decisions. Confirm that review notes do not mutate MusicBrainz confidence or Lidarr search/download permissions.

If you encounter a build or self-test error, provide the exact console output rather than attempting to run an older version.
