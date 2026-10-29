# v0.9.1.13 validation record

## Completed locally (Linux environment)

- JavaScript syntax validation using `node --check`.
- UI regression: no-usable-candidate save to manual-import ledger, server-queue refresh and removal, explicit manual-edit Save advances without requiring Next, and summary counts.
- Existing interactive review regression: candidate preloading, title aliases, repeated previews, manual selection, evidence chips.
- Existing wizard/navigation regression: refresh recovery, migration selection, local staging, completion, GitHub opt-in controls.
- Pygments-based C# lexical balance and route assertions: 17 C# source files. THIS IS NOT A SUBSTITUTE FOR NATIVE .NET BUILD.
- Original matching rules, selection safeguards, and no-Lidarr-write policy remain unchanged.

## Not verified in this environment

- Native Windows .NET 8 compilation/self-tests/publishing; dotnet SDK unavailable here.
- Live Apple/YouTube/MusicBrainz lookups, live Lidarr connection, operating-system Documents permissions.
- Actual Lidarr library write/download, which remains intentionally **not implemented**.

## Scope

- Automatic classification is limited to source artist + song title records without candidate IDs, plus candidate records that show **no usable results after their detailed lookup**. Identifiable competing candidates remain in review.
- Saved metadata is not a verified song, verified artist, a new MusicBrainz entry, or a downloaded audio file.
- No change to prior verified match counts is asserted.
