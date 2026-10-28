# v0.9.1.15 — Pre-review automatic metadata classification & reversible manual saves

## Fixed

- **Classify before displaying Manual Review.** The app now starts a bounded server-side artist-queue preflight. When a source has an artist name, song title, album name, and valid YouTube watch URL, the app can save source metadata automatically if there are no candidate IDs or if all returned MusicBrainz artist names agree exactly with the source artist after capitalization, accent, `and`/`&`, and punctuation normalization.
- **Previously chosen source entries can be filed.** Earlier user selections are preserved in the separate review-decision notebook. Automatic filing never claims to verify a particular MBID or recording master; it does not write to Lidarr or download audio.
- **Repeated candidate names:** A group of several MusicBrainz IDs all resolving to the same artist name (for example Daniel Murphy) can be filed as *unverified metadata* rather than repeatedly asking the user to choose between indistinguishable names. If any name differs, lookup fails, or details are incomplete, the item remains for review.
- **Actual progress:** The review-preparation bar is based on completed artist checks returned by the server. It disappears when preflight finishes; a failed check leaves an entry in Manual Review.
- **Revisit saved entries:** *Saved for Manual Import* now has **Review / Edit** on each item. It restores the saved entry to the review/editing workflow and carries a durable override so it won't immediately disappear again. Saving changes returns it to the saved list.
- **Incomplete fields:** Automatic filing is not performed unless artist, song, album, and original YouTube URL are all present. The manual form retains optional album and link fields.
- **Refresh:** Starting or resuming the Manual Review page checks the server-side preflight; it never triggers another full 2,505-song verification.

## Important limits

- Filing source metadata is **not** verifying a MusicBrainz artist ID, identifying an exact recording, importing a track to Lidarr, or downloading audio.
- Songs with unresolved recording versions remain in Song Review unless already handled by existing safe rules.
- The app intentionally doesn't auto-file a source when distinct MusicBrainz artist names compete or candidate names are unavailable.
- The preflight can require bounded MusicBrainz artist-ID lookups; MusicBrainz rate limits are respected and completion time depends on their response.
- This code has been source checked and tested with offline fixtures. The native Windows .NET build and real-service test are pending.

## Files

- `ReviewAutoClassifyV915.cs`: server-side preflight status, bounded exact-name checks, safety tests.
- `ManualFallbackV912.cs`: fully-populated automatic rule, reversible saved entry.
- `GuidedReviewV917.cs`: manual override and restored-queue visibility.
- `Program.cs`: API routes.
- `AppUI/flow918.js`: progress, queue restoration, Review/Edit control.
- `tools/offline_v09115_preflight_restore.js`: new offline interactive regression test.

## One-command Windows test

```powershell
powershell -ExecutionPolicy Bypass -File .\BUILD-AND-RUN.ps1
```
