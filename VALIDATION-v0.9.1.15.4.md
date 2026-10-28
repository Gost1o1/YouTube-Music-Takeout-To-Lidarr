# v0.9.1.15.4 validation

The eight UI tests in `TEST-V091154.md` were executed against the
source tree; offline execution passed.

This environment did not have the native .NET SDK. Windows compilation,
server self-tests, final EXE publishing and actual Live Takeout/Lidarr
integration require a subsequent Windows GitHub Actions run.

The new title tests assert metadata-only auto classification, NOT
independent recording identity verification. No full Takeout rescan,
Lidarr write, media download, or external account mutation was performed.
