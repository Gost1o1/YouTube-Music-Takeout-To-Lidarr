# Validation — v0.9.1.13.1 source package

Root cause reproduced from the original v0.9.1.13 ZIP: `ManualFallbackV912.cs` contained three ordinary C# string literals with an invalid `\ ` escape (previous lines 146, 147 and 151).

Fix: each original artist string literal contains a valid C# `\\` sequence, which produces the intended single literal backslash in the runtime artist name. The source regression asserts three exact corrected literals.

Checks include `node --check` and existing JavaScript UI smoke tests for automatic metadata queue removal, delayed review loading, refresh/navigation recovery, migration selection, and other interactive states; and legacy structural source checks not requiring the .NET runtime. See the changelog for scope.

Known limitations:
- No local .NET 8 SDK / Windows compiler is available in the packaging environment. **Native compilation and application self-test execution have not been verified here.**
- Old `check_v0912_static.py` and `check_v0919_unified.py` assert the presence of screens/version labels intentionally removed in later releases and are not applicable to this hotfix. Run the focused test and actual .NET build instead.
- This update does not alter underlying MusicBrainz matching, review confidence, or Lidarr write permissions.
