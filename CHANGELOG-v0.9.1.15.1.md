# v0.9.1.15.1 — C# CS0103 Build Hotfix

- Fixes `GuidedReviewV917.cs(279,71): error CS0103: The name 'restored' does not exist in the current context`.
- `SendReviewQueue` now declares `HashSet<string> restored;` outside the `verificationGate` block and assigns it while the lock is held, so the later API projection can read its contents.
- Keeps prior manual-metadata restoring/editing functionality and all matching / Lidarr safety constraints unchanged.
- Adds `tools/check_v091151_restored_scope.py` to prevent this exact scoping mistake from recurring.
- Updates the displayed version to v0.9.1.15.1 and fixes printed newline sequences in the PowerShell build script.
- Full project source included. No requirement to modify original v0.9.1.15 files manually.

**Validation limitation:** The development container has no `dotnet` CLI or C# compiler, so only structural/source checks and offline UI tests can be performed. The first native Windows build is still required.
