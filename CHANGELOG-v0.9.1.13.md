# v0.9.1.13 — Automatic Missing-Metadata Queue Repair

- When the current audit already supplies an artist and a song title and there are **no MusicBrainz candidate IDs**, the backend automatically saves a review-only entry in the existing user Documents ledger. Only complete source metadata is auto-saved.
- If there are candidate IDs but, after the bounded details lookup, **none is usable**, the browser makes one idempotent automatic metadata save and reloads the review queue. This is not proof of a match.
- Both kinds of saved items disappear from the active review queue, not from audit files. Manual saving also returns to the next item rather than leaving Next disabled.
- Migration Preview has a dedicated **Saved for Manual Import** tab showing Automatically Saved / Manually Saved statuses. Final confirmation and completion show these entries in a separate, read-only summary.
- Saved metadata survives refresh and application upgrades (`Documents/MusicTakeoutToolkit/manual-metadata-v1.json`). Prior entries remain readable.
- Race guards prevent duplicate async automatic saves, prevent overwriting a user's manual corrections, and provide the manual form when an automatic request fails.
- Invalid / placeholder artist or song titles are not auto-saved. Each saved key is validated against the current audit.
- No MusicBrainz ID is verified, no Lidarr searches or writes occur, and no music is downloaded in this path.

## Known limitations

- MusicBrainz results whose full names have not yet been loaded will only be classified when their details lookup completes; preloading remains bounded to keep resource usage reasonable.
- Automatically saved items are not necessarily importable through Lidarr until a supported metadata source and legally acquired audio are available.
- New EXEs cannot inherit an unfinished scan from a different EXE process.
