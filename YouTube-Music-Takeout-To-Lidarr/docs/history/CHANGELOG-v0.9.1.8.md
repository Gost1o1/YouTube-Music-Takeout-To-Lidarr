# v0.9.1.8 — Simplified review, import-plan flow, developer completion and GitHub starring

## Implemented

- Preserved all v0.9.1.7 source and imports, MusicBrainz matching, album/artist/recording audit and persistent decisions.
- Added streamlined **Manual Review** with centered artist/song progress cards; `Matched` / `Unmatched` counters; Artist Review / Song Review; navigation `1 of N`; `YouTube Music Source`; candidate cards, `None of these`, evidence chips; clicking top `Next` saves a decision via the existing local review ledger; no notes or bottom Next button. Only actual existing candidate IDs are offered. No invented numeric confidence score or audio clip is presented as real.
- Added **Migration Preview** with `To Be Added`, `Already in Lidarr`, and `Unmatched` tabs, Select All, `X of Y selected`, clean album cards, and Next.
- Added **Finalize Lidarr Import** summary with `Albums` / `Songs` values, selected list, explicit local-planning disclaimer.
- Added real **local staging progress** driven by the existing `/api/lidarr/inventory/stage-albums` API. A failure stops the progress. Does NOT issue Lidarr search, download, or library write calls. Only after successful staging of all selected albums does the success screen appear.
- Added continuous CSS confetti (respects reduced-motion), developer About page for Quinton Burgess / Gost1o1 with official links, support email and GPT-6 disclosure. The Exit button asks the local server to shut down gracefully.
- Added opt-in **GitHub App Device Flow** and verified starred-repository API call with explicit click. Access token is in process memory only. Requires maintainer-configured GitHub App client ID `GOST1O1_GITHUB_CLIENT_ID`. If unconfigured, the feature remains unavailable.
- Added tests for navigation, save-on-next, select-all, local staging progression, About, and GitHub authorization UI; one C# self-test group tests GitHub protocol data and URL.

## Known limitations

- No live Lidarr album importing, monitoring/searching or downloading yet. Current completion screen means local **planning** is finished, not that music is downloaded. Additional music sources and Lidarr write workflows require separately designed and approved APIs.
- The v0.9.1.7 data contains candidates/MBIDs, but not guaranteed direct artist images or licensed 10-second previews. These controls are deliberately unavailable in the real UI until a trustworthy licensed source is wired in. UI does not play fabricated clips, and it does not invent confidence percentages.
- GitHub starring requires creating/configuring a GitHub App; the client ID is **not provided in the ZIP**. End users authenticate on GitHub's device-login page, but the repository itself does not need to open.
- Native Windows .NET build, self-tests and GitHub OAuth response need to be checked on Windows; this environment has no dotnet SDK nor GitHub App credentials.
