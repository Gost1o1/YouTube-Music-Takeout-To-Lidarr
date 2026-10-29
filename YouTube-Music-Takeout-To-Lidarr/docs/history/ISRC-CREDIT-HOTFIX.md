# ISRC Artist-Credit Hotfix — v0.9.1.1 (source-only)

The v0.9.1 ISRC lookup used `?fmt=json` while the recovery matcher required `artist-credit`. MusicBrainz does not include those credits by default. This accidentally rejected valid candidate recordings.

## Change

- Use `/ws/2/isrc/{isrc}?inc=artist-credits&fmt=json` for the targeted ISRC recovery.
- Distinguish an incomplete response with missing credits from a confirmed artist mismatch.
- Add offline self-test assertions for the required request parameter and incomplete artist credits.

## Validation

No live MusicBrainz calls or .NET compilation were possible during packaging. This is a narrow, source-only hotfix. Existing album planning, green UI and no-write Lidarr protections remain unchanged. After building on Windows, reuse audit CSVs (11) and rerun **Recover Artist IDs and Recording Identifiers** only, not the full MusicBrainz scan. Export new song, artist and identifier diagnostics. If previously attempted ISRCs were stored in the imported audit, the recovery flow still re-queries their Deezer track metadata and ISRCs.

Not all of the 95 returned recordings are necessarily compatible or unique; any recording with conflicting IDs or ambiguous credits must remain held for review.
