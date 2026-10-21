# v0.8.7 — branded progress and evidence refinement (source-only)

- Gost1o1-inspired green palette applied through CSS overrides without replacing the wizard UI.
- Elapsed time, throughput, estimated remaining time, and active verification phase displayed below progress bars.
- More explicit evidence for whether an exact album distinguishes one retrieved recording version.
- Artist-identity audit no longer requires every recording version for that artist to be high-confidence before independent exact song corroboration can count.
- Expanded CSV evidence and CI build triggers for Audit.cs/Verification.cs.

**Not compiled or live-tested here.** ETA is approximate, especially during second pass and retries. The source still needs a Windows build and MusicBrainz regression verification; no Lidarr writes or automatic deletion.
