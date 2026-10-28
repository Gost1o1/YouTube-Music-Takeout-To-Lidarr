# v0.9.1.15.3 — Automatic Song Review fallback

Song Review now follows the same source-metadata preservation workflow as
Artist Review. A song with no usable candidates and complete artist, title,
album, and original YouTube link is automatically saved for later manual import,
never marked as a verified MusicBrainz recording. If a source field is missing,
the review card prompts for completion and the manual metadata form is
prepopulated with whatever real source data is available. After saving, the
next song occupies the review position. Candidate lookup errors offer Retry
instead of silently assuming no match. Song titles are now compared with
recording candidate titles (not the artist's name).

**Build, self-test, publish, run (Windows):**
`powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1`

See `CHANGELOG-v0.9.1.15.3.md`, `TEST-V091153.md`, and
`VALIDATION-v0.9.1.15.3.md`. The environment used to package this source did
not have the .NET SDK, so a Windows compilation check is still required. No
real Lidarr write, download, or new full-library verification was performed.

---

# v0.9.1.15.2 — Migration Preview reconciliation

This full-source stabilization release fixes the empty **Already in Lidarr**
and **Unmatched** tabs, carries current Manual Review selections into the
inventory preview as provisional evidence, and preserves distinct MusicBrainz
IDs even when a catalog provider gives them the same image or audio preview.
Automatic metadata filing now runs before Manual Review opens, not while a
review card is being displayed. No Lidarr writes or downloads were added.

**Build, self-test, publish, run (Windows):**
`powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1`

This build is **not yet Windows-compiled**: the development environment had no
.NET SDK. Finish or preserve any active scan before restarting an older EXE;
its in-memory work will not transfer to another process. Use a small fixture
or existing evidence for integration testing before a full 2,505-song scan.

For the scope and tests, see `CHANGELOG-v0.9.1.15.2.md`, `TEST-V091152.md`,
and `VALIDATION-v0.9.1.15.2.md`. The rest of this README retains earlier
release history.

---

# v0.9.1.15.1 — CS0103 build correction

This full-source hotfix fixes the `restored` variable scope in `GuidedReviewV917.cs` that prevented v0.9.1.15 from compiling on Windows. Matching, review decisions, and Lidarr write protections are unchanged. See `CHANGELOG-v0.9.1.15.1.md`, `TEST-V091151.md`, and `VALIDATION-v0.9.1.15.1.md`.

**One command from the extracted root**: `powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1`

---

## Current release — v0.9.1.15 (automatic metadata preflight and reversible saves)

Manual Review now performs a **bounded artist-name preflight before displaying decisions**. Entries with all source metadata fields (artist, song, album, valid original YouTube link) and no viable competing artist identity are **automatically saved for later manual import**, even if they were previously selected in the older review notebook. Several MusicBrainz IDs collapsing to the same normalized artist name may be filed as **unverified source metadata**, never as a verified MBID. Items with incomplete fields, lookup failures, or genuinely different candidate names remain in review. The preflight progress bar reflects real completed checks and disappears after the preparation finishes.

**Saved for Manual Import → Review / Edit** restores an entry for correction, with a durable override preventing it from being immediately auto-saved again. No Lidarr write, media download, or MusicBrainz identity verification is performed by metadata filing.

See `CHANGELOG-v0.9.1.15.md` and `TEST-V09115.md`. The `BUILD-AND-RUN.ps1` script runs build, self-tests, publish, and launch together.

## Current release — v0.9.1.14 (review auto-classification and loading percentage)

Following v0.9.1.13.1, artist review entries with **one unique MusicBrainz ID** whose fetched artist name exactly matches the source name after safe normalization (capitalization, accents, ampersand/and) are automatically saved as **unverified source metadata for later manual import**, provided source artist and song fields are populated and no user review decision already exists. This is NOT a verified MusicBrainz artist match; no MBID is committed, and Lidarr is never written to or downloaded from. Items with competing artist IDs remain for human review. Candidate art/audio does not establish identity; mismatching artist names with artwork alone are hidden. All review details now display a determinate 0–100% loading bar that disappears when complete; the next item remains gated until its metadata is ready. Source edits and saved manual-import entries persist in Documents\MusicTakeoutToolkit.

See `CHANGELOG-v0.9.1.14.md`, `TEST-V09114.md`, and `VALIDATION-v0.9.1.14.md` for specifics and test results. `BUILD-AND-RUN.ps1` builds, self-tests, publishes, and launches the application in one command.

## Current release — v0.9.1.13.1 (build hotfix)

This hotfix fixes three C# `CS1009` invalid escape sequences in `ManualFallbackV912.cs` and keeps the v0.9.1.13 matching and manual-metadata behavior unchanged. See `CHANGELOG-v0.9.1.13.1.md` and `TEST-V091131.md`. Use `BUILD-AND-RUN.ps1` or the four PowerShell commands below.

## Previous release — v0.9.1.13

A complete-source Windows .NET 8 release. **Automatic manual-import metadata saving** removes no-candidate entries with valid artist and song metadata from the active review queue and preserves them in `Documents\MusicTakeoutToolkit\manual-metadata-v1.json`. Entries with incomplete source information retain the manual metadata form. Saving manually returns immediately to the next review item. Migration Preview includes a **Saved for Manual Import** tab, and Finalize/Completion screens show these saved items separately from albums staged for Lidarr.

These are *metadata-only* records. **No songs are added to Lidarr and no media is downloaded; matching-confidence counts do not increase.** The user still needs an authorized audio source and a compatible metadata/import workflow. See `CHANGELOG-v0.9.1.13.md`, `TEST-V09113.md`, and `VALIDATION-v0.9.1.13.md`.

### Complete Windows PowerShell commands

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
.\publish\YouTubeMusicToLidarr.exe
```

Older notes below are retained for historical development context.

---

## Current release — v0.9.1.11

See `CHANGELOG-v0.9.1.11.md` for wizard-wide refresh recovery, `TEST-V0921.md` for Windows verification, and `VALIDATION-v0.9.1.11.md` for test limitations.

## Current release — v0.9.1.11

See `CHANGELOG-v0.9.1.11.md` for wizard-wide refresh recovery, `TEST-V0921.md` for Windows verification, and `VALIDATION-v0.9.1.11.md` for test limitations.

# Current release: v0.9.1.9 — unified automatic verification

**After Lidarr connects successfully, a fresh MusicBrainz verification begins automatically**, followed by Apple/Deezer corroboration, recording/artist ID reconciliation, and read-only Lidarr comparison. The simplified progress display replaces the earlier manual research screens. Successful MusicBrainz responses are cached only for this run, with rate limits honored.

The app continues to prepare a **local import plan only**; it does not yet perform actual Lidarr additions, searches or audio downloads. GitHub starring remains opt-in and requires GitHub authorization. See [CHANGELOG-v0.9.1.9.md](CHANGELOG-v0.9.1.9.md) and [TEST-V0919.md](TEST-V0919.md). Older workflow descriptions below document earlier releases and may mention controls removed from the current UI.

# YouTube Music Takeout to Lidarr — v0.9.1.8 source

**Current state: experimental, use with caution.** This version preserves the original .NET 8 matching engine and adds the streamlined Manual Review, Migration Preview, local-only album planning progress, developer About/exit celebration and opt-in GitHub App device-authenticated starring.

**IMPORTANT:** Lidarr metadata writes/download searches and actual media downloads **are not implemented** in this source release. The 100% completion celebration marks completed *local album staging*, not a completed import into Lidarr. Candidate art is placeholder initials, and song clips are unavailable until a licensed preview provider is implemented. GitHub App Client ID is not included. See `CHANGELOG-v0.9.1.8.md`, `TEST-V0918.md`, `GITHUB-DEVICE-SETUP-v0.9.1.8.md`.

### Compile / publish on Windows

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

# CURRENT DEVELOPMENT BUILD — v0.9.1.7

## Guided evidence review, durable user notes, and safer album reapproval

**Read-only / no Lidarr writes.** This version adds a guided review notebook to the existing green ten-step wizard. When you reach the verification results, optionally attach your latest `musicbrainz-candidate-scoring-audit (1).csv` alongside the (18) song and artist audits (historical candidates are source-row checked and review-only), then choose **Load Guided Review Queue** to see unresolved artists and songs with the candidate MusicBrainz MBIDs from imported evidence and the current run. Filter by artist/title, select **Needs additional research**, **Defer**, or **Candidate selected for planning only**, and save an optional evidence note. A manually chosen candidate is **NOT** automatically promoted to a verified artist/recording, a Lidarr search target, or an approved download.

Review decisions are stored at **Documents\MusicTakeoutToolkit\review-decisions-v1.json**, independently of the EXE folder, and are exported using **Export Saved Review Decisions — NO WRITES (CSV)**. Saved song decisions are source-row/artist/title/album fingerprinted; saved candidates missing from current evidence display a stale warning. Existing Lidarr album-group selections are now saved in the notebook and can be **explicitly re-applied** on a later run only if the new inventory still fully supports that exact album. No saved choice is automatically applied on import.

For intermittent MusicBrainz 503s in recording-first discovery, a bounded additional retry is performed after the existing recovery backoff. A provider failure remains review-only and is visible in the candidate audit.

**Baseline from latest provided files (18):** 2,505 song rows, 951 artist identities, 2,208 addition proposals, 297 manual-review songs, 68 manual-review artists. This version is chiefly a **review-workflow improvement**: saving decisions does not itself lower those counts. Import the existing `musicbrainz-songs-audit (18).csv` and `musicbrainz-artists-audit (18).csv`; no full verification is required. The old album and candidate-scoring CSV exports remain available. See [`CHANGELOG-v0.9.1.7.md`](CHANGELOG-v0.9.1.7.md), [`TEST-V0917.md`](TEST-V0917.md), and [`VALIDATION-v0.9.1.7.md`](VALIDATION-v0.9.1.7.md).

**Build and test on Windows:**

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The code-generation environment has no .NET SDK; Windows build and .NET self-tests must be run by the user. Python/Node static checks are included, but do not prove compilation.

---

# CURRENT DEVELOPMENT BUILD — v0.9.1.6

## Multi-strategy MusicBrainz release discovery

**Read-only preview: no Lidarr searches, adds, downloads or writes.** This revision replaces the 4-release-only follow-up with album-title, release-group alias, individual-release-title, and bounded artist-catalog discovery. It browses release inventories in pages, using the *actual number of returned releases* as the next offset (MusicBrainz may cap total tracks/page). It examines up to 18 selected release tracklists per related release-group candidate, exporting candidate evidence even if an exact recording cannot be established. A partial catalog, non-exact edition relationship, conflicting artist/recording MBID, or competing release groups can NEVER result in a provisional recording ID.

The browser shows **v0.9.1.6** on every wizard screen, avoiding earlier testing of the wrong EXE. Use the latest existing CSV exports `musicbrainz-songs-audit (16).csv` and `musicbrainz-artists-audit (16).csv` (optional original evidence empty), then click **Recover Artist IDs and ISRCs** and export **All Candidate Scores and Album Evidence**, **Detailed Recovery Diagnostics**, and updated audits. The targeted pass can take longer because of expanded discovery but does not require full verification. See `CHANGELOG-v0.9.1.6.md` and `TEST-V0916.md`.

**Windows native build and self-tests are required.** This environment did not have the .NET SDK; code-level checks are not a substitute for successful compilation. Do not publish or merge before `dotnet build` and `--self-test` succeed.

---

# CURRENT DEVELOPMENT BUILD — v0.9.1.4

## Targeted song-to-release matching

v0.9.1.4 extends v0.9.1.3 with bounded MusicBrainz checks for **single unresolved songs**, not just multi-song albums. It uses independently supported artist MBIDs, matching release-group titles and complete small release track inventories; competing recordings, version conflicts and incomplete catalogs remain in manual review. Read [`CHANGELOG-v0.9.1.4.md`](CHANGELOG-v0.9.1.4.md) and [`TEST-V0914.md`](TEST-V0914.md). It makes **no Lidarr changes**.

**Current imported-evidence baseline:** use Song Audit (14) and Artist Audit (14). No full MusicBrainz verification is needed. The companion scoring CSV shows the new `targeted-song-to-release` stage.

---

# CURRENT DEVELOPMENT BUILD — v0.9.1.3


## v0.9.1.3 — Artist and whole-album corroboration

**No Lidarr writes or downloads.** This release retains the centered green ten-step interface and adds bounded targeted MusicBrainz corroboration after importing previous audit CSVs.

- Re-checks **artist candidate MBIDs** using MusicBrainz canonical names/aliases **and** recordings that actually credit that artist MBID. A single alias or a same-name result never establishes identity. At least two distinct credited song titles are required, or one credited title plus independent matching Lidarr MBID.
- Investigates **whole source albums** for which two or more source songs still lack an accepted song identity. It checks MusicBrainz release groups and tracklists, with an explicit distinction between album-title variants and exact versions.
- Exports **`musicbrainz-candidate-scoring-audit.csv`**, preserving each examined artist/release-group candidate, source rows, candidate IDs, title overlap, score (not a probability), competing result, outcome and reasons.
- A song is given a *provisional* MB recording identity only under strict conditions: validated artist MBID, exact matching release-group name, fully inspected small release inventory, two or more separately supported songs, a unique candidate for that song, and no pre-existing conflicting MBID. This never asserts the same audio master or issues a Lidarr write.
- Search breadth is intentionally bounded: four or fewer distinct candidate artist IDs per unresolved name; up to three source titles each; up to 24 multi-song album groups, at most three releases per eligible group. Skipped cases remain in manual review rather than inventing matches.

Run on Windows in the extracted source folder:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Launch `publish/YouTubeMusicToLidarr.exe` and import the **latest** `musicbrainz-songs-audit (13).csv` and `musicbrainz-artists-audit (13).csv` (no full scan), then click **Recover Artist IDs and ISRCs**. This button now also performs the new targeted artist and album corroboration. Export the **Detailed Recovery Diagnostics**, **All Candidate Scores and Album Evidence**, refreshed **Song Audit**, refreshed **Artist Audit**, and then **Migration Readiness** after the read-only Lidarr comparison.

**Validation limitation:** The native .NET 8 compiler is not installed in this creation environment. The added C# self-tests and Windows GitHub Actions must run before considering this version build-verified. No reduction in manual review is promised without live MusicBrainz replies.

See `TEST-V0913.md`, `CHANGELOG-v0.9.1.3.md`, and `VALIDATION-v0.9.1.3.md`.

---


---

# v0.9.0.9 — Album-title aliases and safe album-only search plans

**Source-only Windows test release.** See [VERSION-NOTES-v0.9.0.9.md](VERSION-NOTES-v0.9.0.9.md), [TEST-V0909.md](TEST-V0909.md) and [BUILD-REPORT-v0.9.0.9.md](BUILD-REPORT-v0.9.0.9.md).

This release repairs 174 existing album-only proposals in the action exporter and identifies 22 more title-alias review candidates among the 349 prior manual songs. Offline replay projects 327 remaining manual-song decisions; no ID conflicts are silently accepted. **No Lidarr modification, searches, or downloads are executed.** The green centered wizard remains unchanged except for informative counters and clearer album-alias labels.

**A native .NET build has NOT been run here.** Please build/test on Windows. Import audit `(9)` rather than running a full MusicBrainz verification.

---

# v0.9.0.8 — Album-search eligibility versus recording identity

**Source-only test release.** See [VERSION-NOTES-v0.9.0.8.md](VERSION-NOTES-v0.9.0.8.md) and [TEST-V0908.md](TEST-V0908.md).

This iteration conservatively reduces repetitive manual review when a validated source artist/title/album is already present in Lidarr, but its MusicBrainz recording ID differs. It supports album-search *proposals*, NEVER claims exact recording equivalence, and never performs Lidarr write/search/download operations. Deluxe/EP/expanded edition aliases must be explicitly reviewed at the album level. The centered green UI remains intact.

A replay of the previous private **2,505-row** CSV exports projects **609 → 349** Manual Review entries and **1,896 → 2,156** planned additions (many require further approval). These counts are offline test projections, not a live .NET run, and are not recovered exact-recording IDs. See notes for safety and limitations.

The source archive contains **no private song audit exports, Lidarr keys, or Takeout archives**. The offline Python script under `tools/` is for maintainers only and is not required to run the Windows application.

**Windows native compile / tests have NOT run in this environment.** Use `dotnet build`, `--self-test` and publish on Windows, or the included Windows GitHub Actions workflow.

---

# v0.9.0.7.1 — album approval compile/runtime hotfix

This is the complete v0.9.0.7 source with a narrowly scoped correction to the album-review approval endpoint in `Program.cs`. It fixes Windows C# error **CS0136 at line 356** and avoids re-reading an already-consumed HTTP request stream when an album group is selected. The interface, matching rules, source-data handling, and read-only Lidarr behavior have not changed. See `BUILD-FIX-v0.9.0.7.1.md`.

**Native .NET compilation not performed in the packaging environment.** Build and run `--self-test` on Windows before launching; GitHub Actions also runs these steps.

---

# v0.9.0.7 — read-only album-cohort review

See [VERSION-NOTES-v0.9.0.7.md](VERSION-NOTES-v0.9.0.7.md) and [TEST-V0907.md](TEST-V0907.md). This version adds group-level release recommendations, manual session-only planning choices, clickable searchable category lists, and two new diagnostic/action-plan CSVs. **It does not initiate Lidarr downloads, searches, additions, or any changes.**

# v0.9.0.6 — Release-aware Lidarr planning

See [VERSION-NOTES-v0.9.0.6.md](VERSION-NOTES-v0.9.0.6.md) and [TEST-V0906.md](TEST-V0906.md) for changes, safety limits, and a testing path that reuses your previous audit CSVs.

# v0.9.0.5 — Bounded artist-scoped album inventory (hotfix)

Lidarr inventory now requests albums with `GET /api/v1/album?artistId=<local numeric ID>` for only the validated and relevant local Lidarr artists. The prior unfiltered album request loaded the full catalog and exceeded the 80 MB safety limit before the artist loop began. The per-artist album query is documented in Lidarr’s AlbumController. Each read remains read-only, limited to 80 MB, logged in the downloadable inventory diagnostics, and guarded against partial commits. Artist, track, trackfile, and album records are fetched as a scoped unit, two artists at a time. The user’s existing green centered UI and CSV import are unchanged.

**No repeat verification needed.** Scan the original Takeout archive, connect Lidarr, import song audit `(6)`, artist audit `(6)`, and optionally original song evidence `(5)`. Click **Compare Lidarr Track & File Inventory** and export the Migration Readiness CSV on success, or Lidarr Inventory Diagnostics CSV if it fails.

**Validation:** Archive/invariant checks were executed on the source. A successful native .NET compilation and Lidarr integration test have not yet been confirmed.

---

# v0.9.0.4 — Resilient Lidarr inventory requests and diagnostics

This source hotfix addresses the ambiguous **"Comparison interrupted"** message in v0.9.0.3. It is a likely request-timeout symptom, but the earlier UI did not record enough information to prove the cause for any particular run. Previous code imposed a 90-second per-request cancellation and then reported any `OperationCanceledException` as an interruption, without identifying the HTTP endpoint.

**Changes:**

- Dedicated read-only inventory HTTP client with a 180-second per-attempt timeout (no shorter global 100-second timeout), up to three attempts for individual timeouts and retryable HTTP/network failures. Nonretryable HTTP errors (such as 400/401/403) still stop immediately.
- Only two local Lidarr artists are queried concurrently, with their track and trackfile requests sequential within each artist to reduce service load.
- Distinct user-facing errors for timeout, HTTP status, network failure and explicit cancellation. More detailed status counts for artist progress, completed requests and automatic retries.
- New **Export Lidarr Inventory Diagnostics (CSV)** button appears after starting the comparison, including on failure. Exported diagnostics contain timestamp, local request path/artist ID, attempt, duration, status, outcome and reason. **No API key, Takeout data or music-file paths are included.**
- Comparisons remain read-only and atomic: if any required inventory request ultimately fails, no partial set of songs is marked "Already in Library" or "To Be Added." The diagnostic log and progress counters remain available.

**Retry behavior:** A slow request can take up to three 180-second attempts. A seriously overloaded or unresponsive Lidarr may still fail; export diagnostics in that case rather than assuming manual interruption. Do not run any fresh MusicBrainz verification to test this fix.

**Test without a full verification:**

1. Extract this archive into a new folder. In PowerShell run `dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64`. Only proceed if that succeeds.
2. Run `dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test`. Only proceed if tests pass.
3. Run `dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish`, then open `publish\YouTubeMusicToLidarr.exe` and keep the console running.
4. Import the original Takeout ZIP. Connect to the same local Lidarr instance.
5. Import **Song-by-Song Evidence:** `musicbrainz-songs-audit (6).csv`, **Artist & Song Lists:** `musicbrainz-artists-audit (6).csv`, optionally **Original Song Evidence:** `musicbrainz-songs-audit (5).csv`.
6. Click **Compare Lidarr Track & File Inventory**, and after completion export the migration-readiness CSV. If the comparison fails, export **Lidarr Inventory Diagnostics (CSV)** and share that report, with personal identifiers redacted if desired.

The source ZIP has been structurally checked and the embedded JavaScript passed `node --check`. A .NET SDK and a real Lidarr instance are unavailable in this environment, so native compilation, offline C# self-tests, and live API validation remain **unverified**.

---

# v0.9.0.3 — Lidarr 400 inventory API hotfix

This is a **source test package** correcting v0.9.0.2's read-only inventory HTTP 400 failure. Lidarr requires query parameters for both `/api/v1/track` and `/api/v1/trackfile`; unfiltered requests are invalid. The updated comparison fetches only matching, validated local artist IDs using `?artistId=<local numeric ID>` in batches of four artists, shows progress in the existing centered UI, and continues to commit **zero** partial results on failures. Albums are now fetched per artist in v0.9.0.5. Error messages identify the failing *API route and status* without exposing an API key.

**You do not need another MusicBrainz scan.** As in v0.9.0.2, load the same Takeout archive; import `musicbrainz-songs-audit (6).csv` and `musicbrainz-artists-audit (6).csv`, plus the optional original `musicbrainz-songs-audit (5).csv` to restore prior audit flags, then click **Compare Lidarr Track & File Inventory**. Export `migration-readiness-preview.csv` *after* the comparison reports success. No Lidarr artist is added and no audio is downloaded.

No native C# compile or real Lidarr integration was possible in this environment. Run build and self-test on Windows before trusting results. Please provide the new error message if any other endpoint fails.

---

# v0.9.0.2 — Evidence-preserving import and Lidarr file comparison

This is a **source-code preview**, not a tested release executable. It retains the original centered green wizard and adds read-only Lidarr inventory comparison and optional restoration of matching evidence lost by the v0.9.0.1 CSV importer.

**Recommended for users with earlier (5) and (6) exports:** Import `musicbrainz-songs-audit (6).csv` as Song-by-Song Evidence, `musicbrainz-artists-audit (6).csv` as Artist & Song Lists, and optionally `musicbrainz-songs-audit (5).csv` as the **Original song evidence (baseline)**. The optional baseline restores matching flags and scores only when the song, source row and MusicBrainz recording/artist IDs are identical. Recovered artist IDs and ISRC investigation remain from (6). **No MusicBrainz requests happen during import.**

After import, select **Compare Lidarr Track & File Inventory**. It reads Lidarr `GET /api/v1/track?artistId=<id>`, `GET /api/v1/album?artistId=<id>` and `GET /api/v1/trackfile?artistId=<id>` using the existing validated local connection; it makes **no writes**. Results appear in the updated **Migration Readiness CSV**, including track and file record IDs, match method, candidate count and review reason. The comparison distinguishes `Already in Library`, `To Be Added (proposal only)`, and `Manual Review` for songs. "Already" reflects Lidarr's database and file record claims; it does not independently inspect file bytes on disk.

Run from the extracted project folder:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
# Only if build succeeds:
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
# Only if self-tests pass:
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

In the published output, run `publish\YouTubeMusicToLidarr.exe` and keep the `AppUI` directory beside it. The console must remain open during this development build.

**Important:** Neither the new comparison nor earlier reports establish full MusicBrainz artist-catalog coverage. No automatic artist addition, album searches, file downloads or Lidarr modifications are implemented. Do not publish private Takeout or audit CSVs to GitHub.

---

# YouTube Music Takeout to Lidarr — v0.9.0.1 (v0.9.0 build hotfix)

**v0.9.0.1 fixes a compile-time quoting error in the CSV-import self-test. The Windows build still requires verification.**

**Unfinished, for testing only. Use with extreme caution.** Source-available .NET 8 Windows application. The program currently analyzes Takeout metadata and connects to Lidarr in **read-only mode**; it does **not** add artists, queue downloads, move files, download audio, or delete anything in Lidarr.

The original centered ten-step wizard, the Gost1o1 green-and-white theme, elapsed-time and ETA panel, and existing read-only workflows remain intact. Only the early wizard stages are functional.

## What v0.9.0 changes

1. **Re-import complete evidence:** Import both matching v0.8.8/v0.8.9 song/artist audit exports. Previous v0.8.9 imports discarded the Apple/Deezer columns, which undermined further targeted research. Imported provider identifiers, URLs, confidence flags, and external evidence now survive. Imported scans are labeled as previously collected evidence; fresh verification still starts from scratch.
2. **Independent artist-ID recovery:** For unresolved source artists, compare the existing **Lidarr artist MusicBrainz ID** with a direct MusicBrainz lookup and its canonical name/aliases. Promote to supported only if there is no conflicting existing MBID and the source has corroborated song evidence. A Lidarr name match alone is **not enough**. Direct MusicBrainz artist-name searches provide *candidates for review*, never automatically select one of several same-name artists.
3. **Deezer track detail and ISRC checks:** On tracks with existing Deezer IDs and unresolved exact-recording evidence, fetch the detailed Deezer entry, validate the returned title/artist, obtain a valid ISRC, and query the MusicBrainz ISRC endpoint. Record all compatible MusicBrainz recording IDs. Only a unique title+artist-MBID-compatible result can produce a provisional song/recording identity; **this never proves a particular master/version**. Conflicting or multi-ID results remain unresolved.
4. **Three CSV exports for diagnostics plus readiness:** Existing per-song and per-artist exports now include recovery status, candidates, ISRC information, and decisions. `musicbrainz-identifier-recovery-debug.csv` includes an event for each attempted artist or ISRC research item, with sanitized endpoint names, status codes, durations, returned candidate IDs, checks, outcome, and conflict reason. `migration-readiness-preview.csv` lists categories without falsely declaring that physical audio is already present in Lidarr.
5. **No extra full scan:** Recovery starts from Step 6 on the already-imported v0.8.9 evidence. The user does not have to spend hours repeating the full 2,505-song verification. Network requests are made only after clicking the respective optional research buttons. MusicBrainz queries are paced at 1.2 seconds between calls.

## Intended completed user workflow (NOT fully implemented yet)

The eventual user-facing overview should contain these counts and expandable lists:

| Group | Artist count | Song count |
|---|---|---|
| YouTube Music | Distinct source artists | Source songs |
| Lidarr → Already in Library | Verified artist MBIDs already in Lidarr | Confirmed **on-disk** tracks, not just tracked catalog titles |
| Lidarr → To Be Added | Approved verified artist MBIDs missing from Lidarr | Approved missing releases/tracks after checking actual files |
| Manual Review | Missing/conflicting artist identities | Missing/conflicting song or release identities |

No individual item belongs to multiple *action* groups. The unimplemented Lidarr trackfile comparison must distinguish 'listed/tracked' from 'downloaded/on disk', handle alternate editions, and mark unverified file presence as **unknown**, never as 'missing'. The present `migration-readiness-preview.csv` therefore lists supported songs as **Pending Lidarr track/file comparison**, not Already or To Be Added.

Only **after** manual review may the user explicitly approve Lidarr changes. A final manual-import path may support user-provided audio files and authorized download sources where use and redistribution are permitted. MusicBrainz IDs and Lidarr's album/release metadata requirements must be satisfied; the app cannot just fabricate artist/album identities from Takeout titles. No third-party site should bypass subscription DRM or copyright restrictions.

## Recommended next test (do NOT rerun full verification)

1. Extract the v0.9.0 source ZIP into a new folder and compile using the commands below.
2. Launch `publish/YouTubeMusicToLidarr.exe`; keep its PowerShell console running. Scan the same Takeout ZIP and connect to Lidarr.
3. At Step 6, **Import Previous Evidence**, selecting your *latest* `musicbrainz-songs-audit (5).csv` and `musicbrainz-artists-audit (5).csv`. You do not need to repeat Apple/Deezer checks because the exported provider IDs are preserved.
4. Choose **Recover Artist IDs and ISRCs**. Wait for the counter to finish. This queries MusicBrainz for unresolved artists, and Deezer detail + MusicBrainz ISRC for selected songs.
5. Export **both updated audit CSVs**, the **detailed recovery diagnostic CSV**, and the **migration readiness preview CSV**. Share all four to inspect which identities were recovered, blocked, or unsupported.

## Build on Windows

Building from source requires .NET 8 SDK. A portable no-install executable is still future work.

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release --no-build -- --self-test
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Launch `publish/YouTubeMusicToLidarr.exe` and keep `publish/AppUI` beside it. GitHub Actions Windows CI is configured to build, run offline JSON/import/CSV fixture tests, and publish the Windows artifact. The provided source ZIP is **not** a tested executable; local Windows verification remains necessary.

## Limitations and privacy

- Song names/artist names and Deezer track IDs are sent to the music providers *only* when the corresponding optional research feature is activated. The Takeout ZIP and Lidarr API key are not sent to those providers.
- Imported CSVs contain personal listening metadata and potentially original video URLs. **Do not commit them or diagnostic exports to GitHub.** Diagnostic URLs contain endpoint names instead of private request arguments.
- ISRCs identify recordings rather than universally identifying a specific audio master; a single ISRC may appear on multiple MusicBrainz IDs. The application preserves version uncertainty and conflicting evidence.
- Catalog providers can be regionally restricted or unavailable. API failures are not counted as absent songs. No MusicBrainz-wide complete catalog enumeration is implemented.
- Neither a successful full scan nor an imported CSV proves every result is accurate. The current implementation has not been compiled or live-tested in the authoring environment.

Repository: https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr


## v0.9.1.0 — release-review repair and local staging

This revision builds on the v0.9.0.9 read-only comparison. A known EP naming regression is repaired using an explicit **EP/E.P./(EP) suffix relationship**. This only proposes a release for user review, never asserts that two MusicBrainz recording IDs are identical.

The Step 6 comparison now shows a **local album-search staging queue** for songs with supported Lidarr track IDs, independent of unknown or disputed exact recording versions. Select up to 100 album proposals and stage/unstage them in the current process. **Staging is not final approval and makes zero Lidarr writes or downloads.** A future Lidarr album search can include all tracks on that album, not just the imported Takeout song(s). The local queue is discarded when the underlying audit/inventory changes or the app exits.

New exports: `lidarr-album-staging-queue-NO-WRITES.csv` and `lidarr-manual-review-triage.csv`. The existing no-writes action plan now includes a local-staged flag, separately from the permanently `NO` final-search and Lidarr-write fields. Both new exports are for diagnostics and planning.

Use your existing v0.9.0.9 MusicBrainz song/artist audits to import prior evidence, then run the *read-only* Lidarr comparison. **No fresh multi-hour MusicBrainz scan is required.** Run the built-in `--self-test` before using the UI. This version does **not** execute Lidarr searches, install anything, or modify your library.

## v0.9.1.12 review enhancements

Manual Review resolves artist-name `&`/`and` and Unicode accent differences when comparing text, waits for candidate metadata before enabling selection/navigation, and coalesces repeated audio-preview or same-name suggestions. These are review aids, **not automatic MusicBrainz identity verification**.

The source link metadata uses YouTube's public oEmbed response where available and supports an optional visible privacy-enhanced YouTube iframe for the first ten seconds. Some videos cannot be embedded, have been removed, are restricted or are private; a failed lookup does **not** establish that a video was unlisted. Candidate audio previews are separate Apple-catalog evidence, not guaranteed copies of YouTube's audio.

When no usable candidates remain, the **Add Metadata Manually** page saves corrected metadata locally at `Documents/MusicTakeoutToolkit/manual-metadata-v1.json`. This is for future user-approved acquisitions/imports only: no protected content is downloaded, MusicBrainz is not edited, and no Lidarr library write is performed. For content you own or have permission to download, the open-source `yt-dlp` project is one possible separately installed tool; the present app does not run it or bypass unavailable/private media. Lidarr generally requires an artist and release recognized through MusicBrainz before regular imports succeed.
