# v0.9.1.15.7 — Confirm Lidarr catalog before completion

- Importing shows **two progress bars**: Lidarr API requests processed, and selected catalog items independently verified through read-only Lidarr API lookups.
- Newly added verified artists must be visible in Lidarr's artist catalog before they count as confirmed.
- Selected albums must be present and **monitored** in Lidarr before they count as confirmed. Search requests alone never increment this verification count.
- The final-results screen and confetti appear only if **all selected catalog items are confirmed and no API writes/search requests failed**. Lidarr background searches, downloads and file imports can continue independently.
- When catalog verification is delayed, **Recheck Lidarr Catalog** performs GET requests only. It does not repeat artist additions, album monitoring writes or searches.
- The Finalize page collapses its selected-albums list and limits its height when expanded. Changing steps returns the view to the top without disrupting progress updates.
- The legacy local-only plan stage no longer displays a premature congratulations screen.
- Removed an unrelated YouTube tutorial video from this project's README; that video was intended for Jellyfin Franchise Explorer.

## Validation

- Node-only Lidarr-import UI replay, review regressions, and local-staging checks passed.
- A previously failing, stale v0.9.1.14 automatic-review test also fails unchanged against v0.9.1.15.6; it has not been rewritten or counted as passing.
- Native .NET compilation, EXE publication, and live Lidarr catalog-confirmation tests require the Windows CI workflow or a Windows machine with .NET 8; the packaging environment has no .NET SDK.

## Scope

"Confirmed in Lidarr" means the selected artist and album entries exist in Lidarr, with albums marked monitored. It does **not** mean downloaded audio files exist. Songs saved as source-only manual proposals remain separate and are not automatically written into Lidarr.
