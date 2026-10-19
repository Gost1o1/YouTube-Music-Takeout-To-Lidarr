# Approved workflow and implementation plan — 2026-10-19

This is the agreed product behavior, **not** a claim that all functionality already exists.

## Confirmed choices

- **Background operations = approved:** Users may approve verified artist imports and, independently, specific release searches. Both authorized queues may run in the background while manual review continues. **Neither queue starts without explicit authorization.** An artist import is not authorization for album searches/downloads.
- **Comparison artwork = auto:** Display source artist photos and album art where reliably retrievable and permitted; use a neutral placeholder otherwise. Each candidate and alternative should have an identifying image, if one is available. Images are hints, never sufficient evidence for a match.
- **Retry policy = automatic:** Retry an actual, transient error one time after a failed first attempt, with backoff. Do not retry an in-progress search or download, a not-found music release, an invalid API key, or a rejected/unsafe request as though it were a transient failure. If the second genuine attempt fails, show the reason plus manual-add instructions and configured Lidarr link.

## User experience

1. Choose Your Data → Choose Google Account / Downloaded Data File.
2. Export instructions → Continue to Google Takeout opens official preselected YouTube page → Scan for Takeout File. Search Downloads; offer a file picker when no valid archive is identified. Validate archive contents. Process the ZIP without modifying it.
3. Analyze CSVs; list numbered songs with artist, song title, and exact YouTube source link. Count **unique artists** distinctly from artist credits.
4. Auto-check local Lidarr address (127.0.0.1:8686). If unavailable, show editable URL. Once found, display Settings → General → API Key instructions and **Get API Key** button, never automatically opening the browser. Verify key and connection before scanning existing artist IDs, tracked albums, and imported files.
5. Comparison summary cards, with detail tables: YouTube Music Data → Found (Artists, Songs), Needs Review (Artists, Songs); YouTube Music vs. Lidarr → Pending to Be Added (Artists, Songs), Already in Lidarr (Artists, Songs). Do not equate repeated artist credits with unique artists; do not count exact-name matches as verified identities.
6. Ask: Add Verified Matches Now vs. Review Everything First. Show independent progress bars: Being Added to Your Lidarr Library (confirmed added / planned) and Manual Review Progress (reviewed / total). Artist counts include matched, unmatched, and needs second review.
7. Side-by-side artist matching, source and candidate links, images, MusicBrainz IDs, supporting song evidence, and alternative candidates with images. Users may confirm, select another candidate, or flag for second review. A name or photo alone must not justify identity confirmation.
8. Side-by-side **song** matching after artist work: identify recording version, artist credits, associated release, metadata and source URL. Offer Play/Stop previews independently on each panel, max 10 seconds, one source at a time; reset and replay indefinitely. Play only authorized/licensed previews or local user-owned audio, never extract arbitrary YouTube Music streams. Disable preview when no authorized source is available.
9. Review Songs for Download includes Ready to Search / Already in Lidarr / Needs Review, with Select All ↔ Deselect All for **only eligible verified songs**. Review each exact Lidarr album/release and track count; require separate approval for searches that may trigger whole-release downloads.
10. Download activity must distinguish queued searches, searches accepted, active downloads, imported files, confirmed song files, failures and manual-add items. Retry transient errors one time; avoid double-search or duplicate downloads. Persist state for reopen, cancellation, and resumable processing.
11. Final Summary: clickable Artists Added, Songs Downloaded, Already in Lidarr, Still Processing, Needs Second Review and Needs Manual Add; export actual CSV/HTML results, including MusicBrainz and YouTube IDs/URLs and failure reasons. A successful search is **not** a successful download.

## Engineering delivery order

- **v0.5.7 (this ZIP):** Correct opening wizard flow, export guidance, ZIP discovery and fallback, roadmap documentation. Existing read-only Lidarr UI is retained.
- **Next:** Replace the current non-wizard workspace with unified analyze and Lidarr setup views; implement real counts and clickable read-only result tables; persist UI state without API keys.
- **Then:** Verified MusicBrainz identity and track/release matching with auditable evidence and alternative candidates; artwork caching; allowed audio preview providers.
- **Then:** Explicitly approved, resumable artist import and release-search queues; independent progress, transient-only retries, download/file verification, manual-add report.
- **Finally:** Unified summary, result exports, status refresh, regression tests and Windows packaging.

## Privacy and safety

- Do not hardcode API keys, local disk locations, root-folder IDs or user music records into a public repository. Keep secrets out of log files and saved progress.
- Handle Google account choice only through Google's official browser page. Do not read Gmail or browser cookies; do not submit a Takeout export automatically.
- Validate archive entries and handle malicious or corrupt ZIPs before extraction. Avoid path traversal, resource exhaustion and ZIP bombs.
- Treat online metadata as untrusted input. Respect image licensing, API terms, provider rate limits and audio preview rights.
- Keep all network write operations separately authorized and idempotent; store operation IDs and outcomes for resume/retry.
