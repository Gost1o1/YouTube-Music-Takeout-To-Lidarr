# Portable data and cleanup — v0.9.1.15.9

This is an audit of the current .NET application source, including legacy filenames visible in the user's Windows screenshots. A screenshot does not establish which older program originally created a particular folder. The program never automatically deletes pre-existing files from Documents or a music/media drive.

## Current version — all app-managed writes beside the EXE

Let `<EXE>` be the folder containing `YouTubeMusicToLidarr.exe`.

| Path | Contents | Cleanup |
| --- | --- | --- |
| `<EXE>\RunData\logs\desktop.log` | Diagnostic events and errors | Deleted on clean exit |
| `<EXE>\RunData\review-decisions-v1.json` | Manual artist/song review choices | Deleted on clean exit |
| `<EXE>\RunData\manual-metadata-v1.json` | Saved source-only/manual music metadata | Deleted on clean exit |
| `<EXE>\RunData\lidarr-import-checkpoint-v1.json` | Approved Lidarr writes and confirmation state, **no API key** | Deleted on clean exit if no write/check is pending; retained for an uncertain interrupted import |
| `<EXE>\RunData\*.json.tmp` | Temporary atomic-write companions of the three JSON files above | Deleted on clean exit |
| `<EXE>\AppUI\...` | Required packaged browser interface (static files) | Shipped with the EXE, intentionally retained |

If it is empty after cleanup, the program removes `RunData` and `RunData\logs`. It only deletes exact known names, never recursively removes arbitrary folders or files. **On a forced kill, system crash, or power loss, cleanup is not guaranteed.** An interrupted import checkpoint is retained to prevent unsafe blind resubmission of Lidarr operations. Reviews are otherwise intentionally ephemeral: after clean shutdown, there is no local resume history unless you kept another export or the old legacy originals.

`YouTubeMusicToLidarr.pdb` is a publish-time debug-symbol file, **not** a runtime file. Release builds omit debug symbols and only package the EXE and `AppUI` directory. If an earlier package left a `.pdb` alongside your EXE, it is not required at runtime.

## Legacy files shown in screenshots — preserved, not newly written

| Observed path | What was visible / source conclusion |
| --- | --- |
| `%USERPROFILE%\Documents\MusicTakeoutToolkit\` | Historical timestamped folders, `runs`, `musicbrainz-verification`, `dotnet-logs`, `engine-*.out/.err`, `startup-errors`, `last-takeout-path.json`. Many came from **older workflows**; v0.9.1.15.8 also wrote its own diagnostic log in the `dotnet-logs` subfolder. |
| `%USERPROFILE%\Documents\MusicTakeoutToolkit\review-decisions-v1.json` | v0.9.1.15.8 review selections. New version reads and copies this to `RunData` if needed; **does not delete the original**. |
| `%USERPROFILE%\Documents\MusicTakeoutToolkit\manual-metadata-v1.json` | v0.9.1.15.8 manual metadata. New version imports a copy as above. |
| `%USERPROFILE%\Documents\MusicTakeoutToolkit\lidarr-import-checkpoint-v1.json` | v0.9.1.15.8 approved-import checkpoint, if present. New version imports a copy as above. |
| `F:\MediaStaging\Music\Lidarr-Import\` | Older bulk matching, retry, alias and progress reports shown by Explorer; the current .NET app does **not** write this tree. |
| `F:\MediaStaging\Music\Lidarr-Artists-To-Import.csv`, `F:\MediaStaging\Music\YouTube-Music-Library.csv` | Historical CSVs, not new app outputs. |
| `F:\takeout-*.zip` | User-provided Takeout archive; read as input if explicitly selected. **Never delete it as app cleanup.** |
| `%USERPROFILE%\Downloads\takeout*.zip` | Searched **read-only** for a Takeout archive. Not created by the current app. |

Before deleting historical state, **close all old EXEs, verify the new version has successfully brought over your saved reviews, and copy any reports you wish to preserve.** You can then move the specific `Documents\MusicTakeoutToolkit` folder to the Windows Recycle Bin using Explorer. Review every `F:\MediaStaging\Music` item separately before removing it; these could be work products of other scripts. Do not delete the whole `F:\MediaStaging`, `F:\Downloads`, `F:\Content`, `F:\Transfer`, `F:\Ready to Index`, or your Takeout ZIP.

## Outside the application's filesystem control

- A browser uses `sessionStorage` to remember the current step and choices **across refreshes**. Those entries belong to the browser profile and may be backed by browser-managed files outside the EXE directory. The app removes its current tab's two keys on the explicit Exit button and prunes its own stale keys at the next launch. It never clears other websites' storage; browser-managed caches and history cannot be guaranteed EXE-local.
- A self-contained .NET app or Windows may use operating-system-managed caches, process data, memory dumps, or temporary extraction areas. This code does **not** intentionally create a dedicated roaming-profile, registry, service, scheduled task, or system-wide installation entry, but cannot promise that Windows and the browser create no incidental system files.
- **CSV downloads you explicitly request** are saved to the location selected by your browser (often Downloads). The app streams them as a response; it does not write those CSVs directly to disk.
- If you **build the source with .NET** instead of downloading the finished EXE, the SDK creates `bin\`, `obj\` and the specified `publish\` folder under the source checkout, plus any SDK-managed caches. These are build artifacts, not files created by the finished EXE. GitHub Actions builds on GitHub's runner, not on your PC.
- Lidarr itself has its own database, download-client queue, music root, metadata and logs outside the EXE folder. Those are Lidarr's files, created when you explicitly approve Lidarr changes, and are not removed when the migration app exits.
