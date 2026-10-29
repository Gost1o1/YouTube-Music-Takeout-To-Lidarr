# v0.9.1.12 — Manual Review reliability and missing metadata

Focused release on top of v0.9.1.11. Existing ten-step setup, automatic MusicBrainz pipeline, Lidarr read-only comparison, persistent planning choices, confetti, and opt-in GitHub device authorization remain.

## Resolved

- Look up the original YouTube watch URL via YouTube's public oEmbed endpoint, displaying available title and thumbnail without browser scraping. When lookup fails, describe it as *inaccessible or unavailable*, not specifically *unlisted*. Unlisted links may still be accessible.
- Candidate metadata is fetched before the user can select **None of these** or choose a match; at most one adjacent review entry is prefetched. **Next** waits until that adjacent entry is prepared. Avoids accidental decisions during slow MusicBrainz metadata loading.
- Collapse candidates offering indistinguishable media previews or the same display name, while labeling shared evidence. Grouping cannot establish which MusicBrainz identity is correct and does not silently promote IDs.
- Suppress raw UUID placeholders and generic candidate labels when neither a name nor usable media evidence is present. Missing thumbnails alone do not disqualify named artists.
- If there are no usable candidates, offer **Add Metadata Manually**. This saves an artist, song, album, and optional YouTube watch link in `Documents/MusicTakeoutToolkit/manual-metadata-v1.json`. Corrected metadata is shown on subsequent review loads but does not modify audited MusicBrainz identities.
- Compare artist names after Unicode accent normalization and treating `&` as equivalent to `and` (e.g., Dick Dale & His Del-Tones / Dick Dale and His Del-Tones). A name alone is not sufficient to auto-approve a unique MBID.
- Source watch links can open a visible YouTube privacy-enhanced embedded player for the first ten seconds **when embedding is allowed**, initiated on user click. A second click closes the player and resets it. Album/track previews from Apple remain separately labeled candidate evidence and are not substituted for the source video.
- Protect the YouTube source URL, thumbnail URL, and manual-entry URL against untrusted hosts. No arbitrary external URLs are fetched via the media-details endpoint.
- Preserve the navigation resume behavior of v0.9.1.11.

## Not implemented / deliberate limitations

- No audio fingerprinting of the original YouTube video; identical Apple preview audio is not proof that two different MB artist IDs are the same identity.
- **No automatic matching solely from exact artist names**. Requires independently supported artist credits and recording/edition information; homonymous artists, duplicated IDs, and same-name songs can otherwise be mislabeled.
- No automatic audio download, no use of private/unavailable YouTube links, and **no Lidarr writes**. The current Import screens prepare only a local plan.
- Missing MusicBrainz releases generally require MusicBrainz entry or supported manual import into an existing album/release; the local manual metadata ledger is not equivalent to a custom Lidarr release.
- YouTube oEmbed can be unavailable for a watch URL; no attempt is made to scrape `yt-formatted-string` DOM from a page that the app cannot access.
- External artwork/previews may remain absent. A video that disallows embedding cannot play through the in-app source player.

## Validation

Existing Node smoke tests and the `tools/offline_v09112_review_prefetch.js` delayed-loading, duplicate-preview and manual-fallback test pass, along with offline lexical/static C# checks. **Native .NET compilation and real YouTube/MusicBrainz preview calls were not possible in this environment**; build/self-test on Windows before starting another full scan.
