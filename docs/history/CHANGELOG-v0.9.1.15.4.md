# v0.9.1.15.4 — Song title filing and stable Manual Review history

## Fixed

1. Complete songs with exactly matching catalog titles (including punctuation
   and apostrophe variants such as `We Can't Stop` / `We Can’t Stop`) were
   still shown in Song Review solely because they had one usable candidate.
2. A source title and one candidate title differing only by a single terminal
   parenthetical group can now be filed without a manual click.
3. Automatic source filing distinguishes genuine MusicBrainz recording-title
   responses from the source-title fallback and Apple preview metadata. Lookup
   failure offers Retry rather than silently assuming a valid identity.
4. Server-side review preparation includes bounded (up to three candidate)
   recording-title checks, so eligible songs can disappear from the queue
   before review cards appear. Larger raw sets with one viable displayed
   candidate are handled after the already-required media metadata finishes.
5. The Previous button uses stable review keys and a visited-item history
   instead of trusting indices that shift when automatic saves remove songs
   or the server reorders reviewed entries. Refresh keeps the current key.
6. Navigation checkpoint records key and original ordering across refresh.
7. GitHub Actions now triggers for `ReviewAutoClassifyV915.cs` edits and
   executes the new song-title/navigation regression test.

## Safety

Automatic filing remains **Takeout metadata only**, stored locally in the
`Saved for Manual Import` category. It does not verify an artist, recording
or album identity, increment verified-match statistics, start Lidarr searches,
modify Lidarr or download media. Incomplete metadata still prompts for user
correction. An explicit prior review/restore is not overwritten.

## Validation

Run `node tools/offline_v091154_song_titles_history.js` and the existing
offline tests listed in `TEST-V091154.md`. Native Windows compilation still
requires the included GitHub Actions runner or a Windows .NET 8 SDK.
