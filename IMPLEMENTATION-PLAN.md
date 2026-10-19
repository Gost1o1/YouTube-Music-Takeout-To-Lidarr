# v0.7.0 verification implementation and remaining work

Implemented in MusicBrainz-Verify.ps1: normalized source grouping; cached exact and title-only MusicBrainz recording queries; artist-credit validation; album evidence weighting; distinct-recording artist evidence aggregation with competitor margin; propagation of artist identity to unresolved song versions; versioned checkpoints; read-only operation.

Critical future work: MusicBrainz aliases/area disambiguation without falsely accepting name-only matches; independent iTunes/Deezer metadata corroboration where legal and available; exact release/version and local-file correlation; full Lidarr inventory accuracy checks; authorization-gated artist import and separately gated release searches; real Windows + MusicBrainz tests.

No release searches or imports are enabled.
