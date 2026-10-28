# v0.9.1.14 — Single-name review fallback and determinate review loading

## What changed

- Fixes repeated Manual Review prompts for source-complete, **unreviewed artist** entries with one and only one MusicBrainz candidate whose fetched **artist name** matches the original artist after normalized case, diacritics, punctuation, and `&`/`and` differences. Example: `HOT SPOT` / `Hot Spot`, `F4ST` / `F4ST`, and `Eiden XII` / `Eiden XII`. The application saves the original artist/song/album/link metadata to the existing *Saved for Manual Import* collection and removes the item from the active queue. This **does not confirm an artist identity or claim an MBID match**; it is not a Lidarr write, search, or download. It is intentionally conservative on competing MBIDs, different-name candidates, and any prior user review choice.
- If a user selects a candidate explicitly, the user's decision is still handled through the existing persistent review notebook; this hotfix does not overwrite it.
- Makes artist candidate filtering stricter: a mismatched artist cannot qualify merely by having an Apple album cover or audio clip. Tribute artists, collaborations, and partial-name lookalikes require separate credited-song evidence.
- Introduces a determinate review loading bar for candidate name, song, and preview lookup. It shows actual completed lookup count and percentage. It disappears when all checks are done; **Next** remains blocked until the subsequent item's data is ready. These percentages describe **metadata-check completion**, not confidence in a match.
- Adds an origin reason to newly auto-saved manual metadata (`no-usable-candidate` or `sole-exact-artist-name-unverified`), retained in the local JSON file and displayed in the Manual Import category.
- Shows all saved manual metadata entries in the Migration Preview Manual Import tab instead of limiting to 100 visible rows.
- Preserves browser refresh recovery, GitHub starring, manual correction, local album staging and existing audit data.

## Safety distinctions

A single matching artist name does not confirm the MusicBrainz ID or recording master. This change intentionally **sets source metadata aside** rather than assigning the single candidate ID. Multiple candidates with competing MusicBrainz IDs remain for Manual Review even if their artwork or preview happens to be identical. Song-level recording versions are not promoted based on a title match. Existing saved review decisions are not overwritten.

An entry labeled *Saved for Manual Import* is not in Lidarr and has not been downloaded. The app has not implemented a real Lidarr library import/search workflow.

## Validation and limitations

The current environment did not provide the .NET SDK. JavaScript syntax, offline end-to-end UI tests, HTML/route source tests, C# lexical structural checks, and the prior build-escape regression check passed. A Windows `dotnet build`, `--self-test`, real MusicBrainz lookups and actual YouTube preview still need to be tested on the user's machine. Results cannot be predicted from the prior screenshots.
