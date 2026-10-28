# Validation v0.9.1.12

- Source: complete v0.9.1.11 ZIP with targeted v0.9.1.12 changes.
- JavaScript syntax: `node --check AppUI/flow918.js` PASS.
- Existing offline smoke tests: UI, pipeline navigation, session recovery, wizard refresh restore PASS.
- New offline `tools/offline_v09112_review_prefetch.js`: delayed-candidate gating, name normalization in evidence, duplicate Apple-preview display consolidation, and no-candidate fallback PASS.
- Structural C# checks: static lexical and route/self-test registrations PASS.
- Backend self-tests registered for safe YouTube URL validation and artist-name normalization; **not executed** because the .NET SDK isn't installed here.
- Native compilation, live YouTube/Apple metadata retrieval, and live Lidarr behavior **not verified** in this environment.
- No real downloads, MusicBrainz writes, Lidarr writes, or automatic promotion of review IDs.

Build and self-test in Windows as documented in `TEST-V09112.md`.
