# v0.9.1.4 validation

- Based on full v0.9.1.3 source ZIP (not a mockup or standalone script).
- Offline fixture: all 2,505 song rows, 951 artist rows, and 299 previous manual-review rows accounted for.
- 148 eligible artist/album cohorts, spanning 169 unresolved song rows; 148 of those rows have previously saved corroborating Apple/Deezer evidence.
- Source-level checks: C# bracket/handler/CI consistency, JavaScript syntax, safe single-release regression cases, and unchanged Lidarr no-write architecture.
- No external API requests were executed during offline replay. **Newly confirmed MBIDs from this build: unknown.**
- No .NET SDK was available in this environment; native C# compilation, self-tests and a live Lidarr comparison must be performed on Windows.
- No private Takeout data, CSV exports or Lidarr API keys are included in the source ZIP.
