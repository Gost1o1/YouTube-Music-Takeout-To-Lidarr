# v0.9.1.15.9 — Background candidate preload and portable runtime cleanup

Review candidates are fetched across the entire active artist/song review queue using a bounded two-bundle / three-request scheduler and browser-memory cache. The server shares Apple catalog lookups for identical artist/title pairs and holds a larger, bounded candidate-details cache. Review classification, previews and evidence remain separate from import authorization.

The .NET program now writes only to `RunData` under `AppContext.BaseDirectory`, retaining an uncertain import checkpoint when needed. Normal clean exit deletes exact application-owned state and log files; an app restart can still inspect an interrupted local import safely. The first launch can copy three legacy state files from Documents read-only so users with previous review decisions do not silently lose their work. Existing Documents, F: reports and user Takeout files are never deleted automatically.

Windows release artifacts now omit `.pdb`. New fixture: `tools/offline_v091159_preload_portability.js`.

Known limitation: candidate details cannot be preloaded until verification has generated a review queue; preview/artwork providers may still be slow or unavailable. Browser-managed storage and operating-system caches are not controlled by the portable EXE. A forced kill may prevent normal cleanup, and a pending Lidarr write intentionally preserves its checkpoint.
