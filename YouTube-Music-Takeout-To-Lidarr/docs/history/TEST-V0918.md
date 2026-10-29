# v0.9.1.8 Windows test plan

## Compile, self-test, publish

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\YouTubeMusicToLidarr.exe
```

Do not proceed past a failed build or self-test. `AppUI` files must remain beside the published executable.

## Reuse saved evidence (no multi-hour scan)

Choose your original Takeout ZIP and connect to your existing local Lidarr instance. Import `musicbrainz-songs-audit (18).csv`, `musicbrainz-artists-audit (18).csv`, and optionally `musicbrainz-candidate-scoring-audit (1).csv` (or your newer compatible exports). After import, click **Open Streamlined Manual Review** and test `None of these` and one candidate; pressing only the top **Next** saves the decision. Reopen the review to confirm the saved choice is restored.

Run Lidarr inventory comparison once before the new Migration Preview; stageable albums require actual confirmed track-backed Lidarr relationships. Click **Open New Migration Preview**, choose Select All or specific albums, and use Next to inspect `Albums` and `Songs` totals. Read the local-only warning. Confirm to exercise **local-only staging** and watch progress. At 100%, ensure confetti and the developer page appear. Verify that Lidarr has not been modified, searched or asked to download anything.

## GitHub star test

See `GITHUB-DEVICE-SETUP-v0.9.1.8.md`. With no `GOST1O1_GITHUB_CLIENT_ID` set, the star setup should be unavailable without blocking exit. With a registered App client ID, start device authorization, complete it at GitHub's official URL, and **separately** press Star. Confirm the app displays success ONLY if GitHub accepts the request (HTTP 204). Never submit private credentials to the app's UI.

## Regression / limits

- Run `node tools/offline_v0918_ui_smoke.js` on a computer with Node available.
- Candidate scores are not calibrated probabilities and remain unpublished until grounded.
- 10-second preview clips and artist thumbnails require a suitable licensed provider and identity validation; fallback artwork is initials.
- Do not confuse the local completion celebration with an actual Lidarr metadata import, download or audio file transfer.
