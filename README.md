# YouTube Music Takeout to Lidarr v0.7.1

## Launch (Windows)

Extract the whole ZIP first. **Double-click `Launch.cmd`** (the Windows command launcher). No working-directory setup, admin rights, or typed PowerShell command is required. Keep the launcher window open during the session; it hosts the local service. Do not move `Launch.cmd` outside its extracted application folder.

An optional real `.exe` launcher can be built on Windows with `Build-EXE.cmd` (uses the built-in Windows .NET Framework compiler). This creates a small launcher; it does **not** compile the full application or embed the parser.

## Google account

Google Takeout opens in the standard Edge/browser profile to use the user's existing Google session. An embedded WebView2/browser has a separate profile and generally will not reuse existing Edge sign-in cookies. We do not accept Google passwords and do not automatically create an export.

## Analysis

Every scan uses a unique `Documents\MusicTakeoutToolkit\runs\...` output folder. The app checks `tracks.csv` directly, even when PowerShell returns nonfatal warnings. Errors and diagnostic logs live in that run folder, and startup messages go to `Documents\MusicTakeoutToolkit\startup-errors.log`.

## Safety

This remains a staged build. Artist imports, release searches, audio previews, and download retries are **not yet enabled**. The initial Lidarr comparison is provisional; the subsequent MusicBrainz verification is a separate read-only process. No Lidarr imports/downloads without approval.

## Quiet launch
Double-click **Launch.vbs** to run the local service without a visible PowerShell or command prompt window. Alternatively, use `Build-EXE.cmd` once on Windows to build `YouTubeMusicToLidarr.exe`, which now uses the quiet launcher. `Launch.cmd` remains available when troubleshooting because it shows startup errors. The service runs until you close its app window or end its process; the quiet launcher does not suppress logging to `Documents\MusicTakeoutToolkit\startup-errors.log`.

The API key field uses masked text styling rather than an HTML password input, to reduce browser password-manager prompts. The app does not save the API key to disk.

Comparison counts: artist names requiring review are provisional non-name-matches; MusicBrainz identities have not been verified. Songs requiring review remain `Not checked` until recording-level comparison is implemented.

## v0.6.7 comparison pass

After entering a Lidarr API key, the app displays a comparison-in-progress splash, reads the existing artist list, and queries Lidarr track records for artist names that match Takeout entries. It compares normalized track titles and existing-file flags. This is a **read-only, provisional** check: title/file candidates may be different versions. Recordings cannot be marked verified without MusicBrainz identity checks, so imports and download searches remain disabled. The key is held in the local PowerShell process memory for these calls, and is not saved to disk or included in the HTTP state response. If individual track requests fail, they remain in the review counts and the comparison reports errors.

## v0.6.9 MusicBrainz recording verification (read-only)

After Lidarr connection and its initial inventory comparison, the app now launches `MusicBrainz-Verify.ps1` in a hidden background process. It queries the MusicBrainz recording search service, checking for **exact normalized source song titles and source artist credit identities**, recording distinct MusicBrainz artist and recording IDs. Results and progress are written to `Documents\MusicTakeoutToolkit\musicbrainz-verification\verification-status.json`, and MusicBrainz responses are cached under `cache/`. A restart of a scan will reuse cached responses. No API key is written to those files.

**Expect significant wait times**: MusicBrainz requires courteous rate limiting, and 2,521 songs can take roughly an hour or more on a first pass, depending on connectivity and throttling. The app only reveals final verification counts after the worker completes. Network lookup failures remain **unchecked**, not Needs Review. An artist with ambiguous or missing recording evidence becomes Needs Review after all its tracks have been checked. Strict evidence requirements may leave substantial manual review; no counts are promised.

**Limits:** Exact recording search can miss alternative titles and multi-credit collaboration formatting, and MusicBrainz recording IDs alone do not prove a specific downloadable Lidarr release or local file. Verified missing artists are identification candidates, **not authorization to import**. Song searches, artist additions and downloads are still disabled until additional release validation and explicit approvals are implemented. Never infer that Lidarr having only one artist is a complete/expected library; check your Lidarr URL/API key and actual library.


### v0.6.9
Connection splash, explicit long-verification notice, resumable recording checkpoints, per-record worker errors, and surfaced diagnostics.


### v0.6.10 shutdown and review counts
Closing the Edge app window asks the localhost service to exit. A heartbeat fallback exits after 18 seconds without the app; any active parsing or MusicBrainz child is stopped. Closing during verification may interrupt a single lookup, but the saved checkpoint remains for resumption. A server started without opening the UI must be terminated manually.

Library comparison sections are **YouTube Music Data > Found**, then **YouTube Music vs. Lidarr > Pending to Be Added to Lidarr / Already in Lidarr / Needs Review**. Needs Review numbers represent the current worker's unresolved identifications, **not a guaranteed below-10% matching rate**. In particular, no exact-release verification is available to classify songs for download. A count above 10% cannot be truthfully reduced by relabeling unsafe matches.


## v0.7.1 multi-pass evidence engine (experimental)

The verification worker now performs (1) normalized source artist grouping, (2) MusicBrainz recording searches using both artist + title and a title-only fallback, (3) recorded artist-credit and album/release corroboration, (4) aggregation of distinct recording evidence across songs for each artist, and (5) reuse of verified artist identities for other songs while preserving uncertainty about exact recording versions. Artist decisions require one strong, album-corroborated unambiguous recording or multiple corroborating recordings; conflicting MusicBrainz artist IDs are not silently merged. All results include evidence and candidate IDs in local verification-status.json. Only verified artist candidates can appear under Pending to Be Added; importing still requires the later permission workflow.

MusicBrainz API failures and per-song processing exceptions are classified as **unchecked**, not automatically manual review. Checkpoints and cached responses are reused for the same version; previous algorithm decisions are not trusted without re-verification. The displayed Needs Review song count means *unresolved recording identity*, not a count of music proven missing or ready to download.

**Remaining gaps:** MusicBrainz artist-alias endpoint verification, cross-catalog services (e.g. iTunes/Deezer), precise YouTube content metadata, release-group/version mapping, and end-to-end live tests are not implemented in this build. The goal is lower manual review with sound evidence, not a fabricated <10% figure. The application is read-only throughout verification.


## Fresh MusicBrainz verification (v0.7.1)
Each scan requests fresh MusicBrainz data. No prior response cache or automatic matching decisions are reused. Temporary progress data is cleaned up when the application closes. The worker reports live request counts, temporary lookup errors, and processed records. A restart begins at the first source record. No Lidarr modifications are enabled.
