# v0.9.1.15.3 — Song Review source fallback

1. Server-owned pre-review classification now auto-files **songs with zero raw
   candidates** when the original source artist, song, album, and YouTube URL
   are complete. The persisted entry is metadata-only and does not assert a
   MusicBrainz ID.
2. A song with raw candidates that resolve to **no usable display choices**
   is saved through the idempotent source-metadata endpoint only after all
   candidate details have loaded successfully. Network failures do not count
   as zero matches; a Retry Candidate Lookup button is offered.
3. Missing artist, song, album, or original YouTube link is explicitly flagged.
   The manual form is prefilled with available source fields and allows an
   unavailable album/link to remain optional. Artist and song remain required.
4. Saving incomplete metadata returns directly to the next Song Review item;
   no selection of "None of these" can bypass the completion form when there
   are no viable candidates.
5. MusicBrainz recording candidate titles now compare to the source **song**
   title, not the source artist name. Distinct MBIDs remain separate and
   alternate recording versions still need real evidence.
6. The temporary status says **song metadata**, not an unverified artist ID.

No Lidarr writes, searches, downloads, or artificial match-confidence
improvements were added. The v0.9.1.15.2 migration preview fixes remain.
