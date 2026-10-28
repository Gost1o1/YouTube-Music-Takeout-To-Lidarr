# v0.9.1.15.5 — Review progress and proposal provenance

- Artist/Song matching bars now reflect source-only saves and approved manual
  review choices as **addressed**, with the remaining count decreasing. The
  baseline matching results and additional review decisions are displayed
  separately; a source-only save is NOT a verified recording identity.
- Review cards are withheld while metadata is loading or automatic source
  classification/saving is pending. Auto-filed songs never flash into view.
- Clicking Next on the last artist advances to Song Review. An empty artist
  queue can also continue directly to songs.
- Migration Preview routes current manually selected artist/recording
  candidates and saved source-only song metadata to **To Be Added** as
  *planning-only* proposals. An unselected or explicitly rejected recording
  remains **Unmatched**. Already-present Lidarr records are not relabeled.
- The preview shows origin tags: **Algorithm**, **Manual Review**, and
  **Auto Manual Review**. Saved-for-manual-import metadata remains editable
  in its own tab; it also appears as a non-selectable source-only proposal.
- The existing album checkbox/staging path is unchanged. These new review
  proposals cannot trigger Lidarr writes or downloads.
- Updated the GitHub Actions Windows build and release workflows to run the
  new offline regression test.

This update preserves the source layout cleanup and release-button README
from the prior GitHub-clean package. No native .NET build was available in the
authoring environment; GitHub Actions must compile this version on Windows.
