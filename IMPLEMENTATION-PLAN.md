# v0.7.1 fresh-run verification

Verification runs in a new uniquely named temporary directory on every Start/Retry. No MusicBrainz response cache, previous automatic matching decisions, or resume checkpoints are read. Evidence across source songs may be aggregated in RAM during the current run. A transient JSON progress/result file is used only for communication between the PowerShell worker and app; it is removed at application shutdown. Diagnostic errors are shown to the user during the run and server startup logs remain separate.

Verification is READ-ONLY. Artist import, release search and downloading remain disabled. MusicBrainz rate limiting can make a full run take over an hour. Completion is accepted only with exit code 0 and all source rows processed.

Future work: exact release/song verification, stronger alternate-catalog corroboration, and Windows integration testing.
