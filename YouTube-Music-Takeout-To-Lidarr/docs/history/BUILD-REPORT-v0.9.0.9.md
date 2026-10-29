# v0.9.0.9 validation report

- Complete .NET 8 Windows source repository package: **yes**.
- Native C# compilation in this environment: **not available** (`dotnet` SDK is not installed); no successful compile is claimed.
- Built-in C# self-tests added/updated: album-title alias negatives, volume/glyph cases, release choice and album-only action plan deduplication; **need Windows execution**.
- Offline Python replay: **passed** against all 2,505 source rows; 174 existing album-only plan holds identified and 22 title-alias review candidates identified (349 -> projected 327 manual).
- Browser JavaScript syntax: **passed** using Node.js syntax checking.
- Archive checksums: regenerated for all shipped files.
- Lidarr write/search/download actions: **none** in the v0.9.0.9 changes.

Numbers are source CSV replay projections. They are not proof of successful live Lidarr matching or exact recording verification.
