# v0.9.1.11 — Windows Test Plan

1. `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`
2. `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`
3. `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`
4. Run `publish\YouTubeMusicToLidarr.exe`.
5. At **Step 2**, press F5. You should stay on Step 2. Repeat on Steps 3, 4 and 5; Step 5 may request the Lidarr API key again, since it isn't stored in the browser.
6. Connect to Lidarr and let the automatic verification start. Press F5. It must rejoin the existing progress page without starting a second scan or displaying the Takeout data-in-use error.
7. When complete, open Artist Review and move a few rows forward, then F5. Confirm the same tab and row are selected; repeat for Song Review.
8. Open Migration Preview, select a few albums and switch tabs, then F5. Confirm the same tab and selected album checkboxes. Continue to Final Confirmation and F5.
9. In local import-plan progress, F5 should show the same progress page with `Resume Preparation` if incomplete. Before pressing Resume, no local staging request is issued by the refresh itself. `Resume Preparation` should skip already-staged albums.
10. After completion, refresh both the Congratulations page and About page. Both should restore appropriately (the finished local plan must still match server state).

## Offline regression commands
- `node tools/offline_v0921_navigation_resume.js`
- `node tools/offline_v0918_ui_smoke.js`
- `node tools/offline_v0919_progress_smoke.js`
- `python tools/check_v0919_unified.py`

The tests don't compile native C# or call real GitHub/MusicBrainz/Lidarr services in this environment.
