# v0.9.1.15.2 — Migration Preview reconciliation

## Corrected

1. **Missing preview categories.** The new flow fetched only
   `/api/lidarr/inventory/staging-queue`. It now also fetches
   `/api/lidarr/inventory/items`, validates the expected number of source songs,
   and renders unmatched source recordings individually.
2. **Lidarr metadata versus downloaded audio.** Inventory responses now expose
   `metadataStatus`. The Already in Lidarr tab can show a uniquely selected
   catalog track without claiming its media file is downloaded. The To Be Added
   tab continues to show album-search proposals.
3. **Invisible saved review selections.** Inventory rows expose the original
   user-selected artist/recording candidate when its review fingerprint still
   matches the current source. This is display-only provisional evidence;
   independent confidence and Lidarr write permissions remain unchanged.
4. **Distinct MBIDs collapsed into one card.** Cards now deduplicate by entity
   ID rather than shared preview/name. Same-name identities display a short
   ID suffix and remain separately selectable if review is genuinely needed.
5. **Queue race/flicker.** Browser rendering no longer auto-files current
   entries or mutates the review queue. A preparation page appears immediately
   while server-owned preflight finishes.
6. **Preflight coverage.** Handles up to 12 raw candidates rather than eight,
   includes their IDs in its input signature, and preserves saved user choices.

## Not changed

- No new MusicBrainz confidence thresholds or arbitrary name-only acceptance.
- No conversion of a selected candidate into a verified song recording.
- No Lidarr writes, searches, or media acquisition.
- No changes to stored Lidarr/Prowlarr keys or original Takeout files.

## Follow-up

- Apply an explicitly chosen identity to an audited read-only comparison only
  after recording/release evidence supports it.
- Validate the whole library against preserved audit evidence without repeated
  live MusicBrainz calls.
- Investigate edition-specific cases such as Katy Perry's live recording.
