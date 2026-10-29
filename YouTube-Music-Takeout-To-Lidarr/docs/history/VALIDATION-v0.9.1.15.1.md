# Validation report — v0.9.1.15.1

The reported CS0103 was caused by `var restored` being scoped inside the `lock (verificationGate)` block but referenced in an anonymous object constructed after the lock. The hotfix declares and assigns `restored` in the outer method scope.

Offline checks completed: `check_v091151_restored_scope.py` (PASS), `check_v091131_csharp_escape.py` (PASS), JavaScript syntax (PASS), review prefetch/metadata/auto-review tests (PASS), migration UI test (PASS), automatic verification workflow test (PASS), navigation and refresh-recovery tests (PASS). The older escape check was made version-agnostic so it tests real escape correctness and UI version consistency rather than a specific historical version string. No native C# compilation was possible in this container (the .NET SDK is unavailable); Windows build results must be verified on the user system. No live MusicBrainz, Lidarr or GitHub operations were run.
