# v0.9.1.13.1 — C# build escape hotfix

- Fixed **three** `error CS1009: Unrecognized escape sequence` compiler errors in `ManualFallbackV912.cs` (previous source lines 146, 147, 151).
- Cause: the `Axwell /\ Ingrosso` test artist name was included directly in ordinary C# string literals. A backslash in a C# string literal needs to be written as `\\` in the source.
- Corrected all three source literals. The artist name represented at runtime remains **Axwell /\ Ingrosso**.
- Updated the browser-visible version to `v0.9.1.13.1` on the main wizard and streamlined review screens, to make the running build distinguishable from v0.9.1.13.
- Added `tools/check_v091131_csharp_escape.py` to guard against this exact regression.
- Added `BUILD-AND-RUN.ps1` to run build, native self-tests, publish and launch as one command, stopping automatically if any command fails.
- Original v0.9.1.13 workflow retained: no-candidate entries with valid source metadata are saved for manual import; they leave the active review queue, and the saved entries appear separately in Migration Preview.

**No modification to MusicBrainz verification, matching thresholds, Lidarr metadata operations, or saved review data.** This is a build hotfix.

**Validation limits:** The packaging environment does not have the .NET SDK, so a Windows .NET 8 build and self-tests must still be performed by the user. Passing Python/Node source/UI tests alone cannot establish that the project compiles.
