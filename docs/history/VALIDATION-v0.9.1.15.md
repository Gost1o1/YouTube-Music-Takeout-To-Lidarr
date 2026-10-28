# v0.9.1.15 Validation

## Checked in this environment

- All 18 `.cs` files have lexically balanced C# braces (Pygments C# token stream).
- The prior C# `CS1009` backslash-literal source regression passed.
- JavaScript syntax check passed (`node --check AppUI/flow918.js`).
- Existing offline tests passed: v0.9.1.12 delayed loading / dedupe; v0.9.1.13 manual save; v0.9.1.14 artist normalization and loading; v0.9.1.18 UI; v0.9.1.19 unified verification; v0.9.1.20 session recovery; v0.9.1.21 navigation restore.
- New v0.9.1.15 offline test passed: server-reported 0→50→100% review classification; exact-artist source filed and hidden from review; Saved for Manual Import entry restored and edited; no Lidarr writes.
- Native `dotnet build`, `--self-test`, Windows EXE publishing, live MusicBrainz requests, and YouTube playback **were not run** because the .NET SDK and those services are not available in this environment.

## Important invariants

- No automatic MusicBrainz identity approval solely from artist name, including one name backed by multiple IDs.
- No recording-master verification, Lidarr write, search, or download.
- An item must have artist, song, album and a valid original YouTube URL to be filed automatically.
- An unknown or different MusicBrainz artist name leaves the item reviewable.
- Restored entries are persistently marked as user-restored and are not automatically filed again.
- A new application process does not inherit the previous process's running verification; local metadata remains in `Documents\\MusicTakeoutToolkit`.
