using System.Net;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // In-memory for this process only: never reused as a MusicBrainz lookup cache.
    EvidenceAudit? latestAudit;
    sealed class EvidenceAudit
    {
        public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
        public List<SongAudit> Songs { get; } = new();
        public List<ArtistAudit> Artists { get; } = new();
    }
    sealed class SongAudit
    {
        public int Row { get; set; }
        public string Artist { get; set; } = "";
        public string AllArtistCredits { get; set; } = "";
        public string Song { get; set; } = "";
        public string Album { get; set; } = "";
        public string YouTubeUrl { get; set; } = "";
        public string ArtistId { get; set; } = "";
        public string RecordingId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Reason { get; set; } = "";
        public int EvidenceScore { get; set; }
        public bool ExactTitle { get; set; }
        public bool ExactArtist { get; set; }
        public bool AlbumMatch { get; set; }
        public bool CrossSongSupport { get; set; }
        public bool MultipleRecordingVersions { get; set; }
        public bool AlbumIdentifiesVersion { get; set; }
        public bool AlbumSearchConfirmed { get; set; }
        public string SongIdentityStatus { get; set; } = "";
        public string Alternatives { get; set; } = "";
        public string AppleStatus { get; set; } = "not-checked";
        public string DeezerStatus { get; set; } = "not-checked";
        public string ExternalStatus { get; set; } = "not-checked";
        public string AppleTrackId { get; set; } = "";
        public string AppleUrl { get; set; } = "";
        public string DeezerTrackId { get; set; } = "";
        public string DeezerUrl { get; set; } = "";
        public bool AppleAlbumMatch { get; set; }
        public bool DeezerAlbumMatch { get; set; }
        public bool RecoveredMusicBrainzIdentity { get; set; }
        public string Isrc { get; set; } = "";
        public string IsrcStatus { get; set; } = "not-checked";
        public string IsrcCandidateRecordingIds { get; set; } = "";
        public int IsrcMatchingCandidates { get; set; }
        public string RecoveryDecision { get; set; } = "not-checked";
        public string PriorEvidenceRestored { get; set; } = "";
        public string IsrcCandidateValidationDetails { get; set; } = "";
    }
    sealed class ArtistAudit
    {
        public string SourceName { get; set; } = "";
        public string ArtistId { get; set; } = "";
        public string Status { get; set; } = "";
        public string Reason { get; set; } = "";
        public int SourceSongs { get; set; }
        public int VerifiedSongs { get; set; }
        public int UnresolvedSongs { get; set; }
        public int ExactSupportedTitles { get; set; }
        public bool AlreadyInLidarr { get; set; }
        public string ArtistIdRecoveryStatus { get; set; } = "not-checked";
        public string ArtistIdRecoverySource { get; set; } = "";
        public string ArtistIdCandidates { get; set; } = "";
        public string LidarrMbIdCandidate { get; set; } = "";
        public string SourceSongList { get; set; } = "";
        public string CorroboratedSongList { get; set; } = "";
        public string SourceOnlySongList { get; set; } = "";
        // Actual MusicBrainz artist-wide catalog browsing is not performed by this release.
        public string CatalogCoverage { get; set; } = "Partial: MusicBrainz results cover queried Takeout titles, not the entire artist catalog";
        public int MusicBrainzCatalogTotal { get; set; } = -1;
        public string MusicBrainzOnlyTitles { get; set; } = "Not assessed - full MusicBrainz catalog not enumerated";
    }
    EvidenceAudit BuildEvidenceAudit(Track[] source, MatchResult[] results, Dictionary<string, Dictionary<string, int>> evidence)
    {
        var audit = new EvidenceAudit();
        var knownLidarrIds = lidarrArtists.Select(a => a.foreignArtistId).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownLidarrNames = lidarrArtists.Select(a => Normalize(a.artistName)).ToHashSet();
        for (int i = 0; i < source.Length; i++)
        {
            var t = source[i]; var r = results[i];
            // A MusicBrainz search score is a ranking signal, not a calibrated confidence probability.
            var strong = r is not null && r.Verified && r.ExactTitle && r.ExactArtist &&
                (!r.MultipleRecordingVersions || (r.AlbumIdentifiesVersion && r.AlbumSearchConfirmed));
            var why = r is null || !r.RequestSucceeded ? "MusicBrainz request unsuccessful" :
                !r.Verified ? r.Reason : !r.ExactTitle ? "Title differs after normalization" :
                !r.ExactArtist ? "Artist supported only by cross-song evidence" :
                r.MultipleRecordingVersions && !(r.AlbumIdentifiesVersion && r.AlbumSearchConfirmed) ? "Artist and title supported; exact recording version is not independently established" :
                "Exact title and artist; recording version supported";
            audit.Songs.Add(new SongAudit {
                Row = i + 1, Artist = t.Artist, AllArtistCredits = string.Join(" | ", t.ArtistCredits), Song = t.Song, Album = t.Album, YouTubeUrl = t.YouTubeUrl,
                ArtistId = r?.ArtistId ?? "", RecordingId = r?.RecordingId ?? "",
                Status = strong ? "high" : r is not null && r.Verified ? "artist-supported/version-review" :
                    r is null || !r.RequestSucceeded ? "lookup-failed" : r.Ambiguous ? "ambiguous" : "unmatched",
                Reason = why, EvidenceScore = r?.Confidence ?? 0,
                ExactTitle = r?.ExactTitle ?? false, ExactArtist = r?.ExactArtist ?? false,
                AlbumMatch = r?.AlbumMatch ?? false, CrossSongSupport = r?.CrossSongSupport ?? false,
                MultipleRecordingVersions = r?.MultipleRecordingVersions ?? false,
                AlbumIdentifiesVersion = r?.AlbumIdentifiesVersion ?? false,
                AlbumSearchConfirmed = r?.AlbumSearchConfirmed ?? false,
                SongIdentityStatus = r is { Verified: true, ExactTitle: true, ExactArtist: true } ? "artist-and-song-supported" :
                    r is { Verified: true } ? "provisional-match" : r is { RequestSucceeded: false } ? "lookup-failed" : "unresolved",
                Alternatives = string.Join("; ", r?.CandidateArtistIds ?? new List<string>())
            });
        }
        foreach (var group in audit.Songs.GroupBy(s => s.Artist, StringComparer.OrdinalIgnoreCase))
        {
            var sourceSongs = group.Select(s => s.Song).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var verified = group.Where(s => s.Status == "high").ToArray();
            var supportedIds = group.Where(s => s.ArtistId.Length > 0)
                .GroupBy(s => s.ArtistId, StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Count()).ToArray();
            var best = supportedIds.FirstOrDefault();
            int independentSongs = best?.Select(s => Normalize(s.Song)).Distinct().Count() ?? 0;
            int conflicting = supportedIds.Skip(1).Sum(g => g.Count());
            // Distinguish lack of cross-song quantity from a genuine identity contradiction.
            // One song alone is sufficient for high artist confidence only when the exact
            // candidate ID is independently corroborated by the existing Lidarr library.
            // Artist identity can be supported even if an unrelated song version still
            // requires attention. Never accept conflicting MusicBrainz artist IDs.
            int exactSupportingTitles = best?.Where(s => s.ExactArtist && s.ExactTitle)
                .Select(s => Normalize(s.Song)).Distinct().Count() ?? 0;
            bool hasExactArtistEvidence = exactSupportingTitles > 0;
            bool lidarrIdCorroboration = best != null && knownLidarrIds.Contains(best.Key);
            bool high = best != null && conflicting == 0 && hasExactArtistEvidence &&
                (exactSupportingTitles >= 2 || (exactSupportingTitles == 1 && lidarrIdCorroboration));
            var supportedTitles = verified.Select(s => s.Song).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var sourceOnly = sourceSongs.Where(x => !supportedTitles.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            audit.Artists.Add(new ArtistAudit {
                SourceName = group.Key, ArtistId = best?.Key ?? "", Status = high ? "high" : "needs-review",
                Reason = high ? (independentSongs >= 2 ? "Two or more exact, consistent song credits support artist ID; other song versions assessed separately" : "Exact recording credit and existing Lidarr artist MBID corroborate identity") :
                    conflicting > 0 ? "Conflicting MusicBrainz artist IDs across source songs" :
                    best is null ? "No confidently identified artist ID" :
                    !hasExactArtistEvidence ? "No exact artist-credit and title corroboration" :
                    "Single-song artist: exact recording match supports this identity, but independent artist corroboration is still missing",
                SourceSongs = sourceSongs.Length, VerifiedSongs = supportedTitles.Length,
                UnresolvedSongs = sourceOnly.Length, ExactSupportedTitles = supportedTitles.Length,
                AlreadyInLidarr = (best is not null && knownLidarrIds.Contains(best.Key)) || knownLidarrNames.Contains(Normalize(group.Key)),
                SourceSongList = string.Join(" | ", sourceSongs), CorroboratedSongList = string.Join(" | ", supportedTitles),
                SourceOnlySongList = string.Join(" | ", sourceOnly)
            });
        }
        return audit;
    }
    static string CsvCell(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    static string CsvLine(params object?[] cells) => string.Join(",", cells.Select(x => CsvCell(x?.ToString()))) + "\r\n";
    static byte[] BuildAuditCsv(EvidenceAudit audit, string kind)
    {
        var sb = new StringBuilder();
        if (kind == "artists")
        {
            sb.Append(CsvLine("Artist", "Artist MBID", "Confidence", "Explanation", "Unique source songs", "High-confidence song titles", "Unresolved source titles", "Already in Lidarr", "Source songs (names)", "High-confidence overlapping songs (names)", "Remaining source songs (names)", "MusicBrainz catalog completeness", "Total distinct MusicBrainz catalog songs", "MusicBrainz-only songs", "Externally corroborated source song count", "Externally corroborated source song titles",
                "Artist ID recovery status", "Artist ID recovery source", "Artist ID candidates", "Lidarr MBID candidate"));
            foreach (var a in audit.Artists)
            {
                var externalTitles = audit.Songs.Where(s => s.Artist.Equals(a.SourceName, StringComparison.OrdinalIgnoreCase) &&
                    (s.ExternalStatus is "one-catalog-supports-title-and-artist" or "both-catalogs-support-title-and-artist"))
                    .Select(s => s.Song).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();
                sb.Append(CsvLine(a.SourceName,a.ArtistId,a.Status,a.Reason,a.SourceSongs,a.VerifiedSongs,a.UnresolvedSongs,a.AlreadyInLidarr,a.SourceSongList,a.CorroboratedSongList,a.SourceOnlySongList,a.CatalogCoverage,"Unknown",a.MusicBrainzOnlyTitles,externalTitles.Length,string.Join(" | ",externalTitles),a.ArtistIdRecoveryStatus,a.ArtistIdRecoverySource,a.ArtistIdCandidates,a.LidarrMbIdCandidate));
            }
        }
        else
        {
            sb.Append(CsvLine("Source row", "YouTube artist", "All Takeout artist credits", "YouTube song", "YouTube album", "YouTube link", "MusicBrainz artist ID", "MusicBrainz recording ID", "Confidence", "Evidence/remaining issue", "MB search score (NOT confidence probability)", "Exact title", "Artist credit agrees", "Album agrees", "Cross-song support", "Multiple recording versions", "Album uniquely distinguishes version among retrieved candidates", "Alternate artist IDs", "Song identity status (separate from exact recording)", "Targeted album query corroborates recording", "Apple song & artist", "Deezer song & artist", "External catalog evidence", "Apple album agrees", "Deezer album agrees", "Apple track ID", "Apple link", "Deezer track ID", "Deezer link", "New targeted MusicBrainz candidate from external album", "Deezer ISRC", "ISRC status", "ISRC candidate recording IDs", "ISRC matching candidate count", "Identifier recovery decision", "Prior evidence restored", "ISRC candidate validation details"));
            foreach (var a in audit.Songs) sb.Append(CsvLine(a.Row,a.Artist,a.AllArtistCredits,a.Song,a.Album,a.YouTubeUrl,a.ArtistId,a.RecordingId,a.Status,a.Reason,a.EvidenceScore,a.ExactTitle,a.ExactArtist,a.AlbumMatch,a.CrossSongSupport,a.MultipleRecordingVersions,a.AlbumIdentifiesVersion,a.Alternatives,a.SongIdentityStatus,a.AlbumSearchConfirmed,a.AppleStatus,a.DeezerStatus,a.ExternalStatus,a.AppleAlbumMatch,a.DeezerAlbumMatch,a.AppleTrackId,a.AppleUrl,a.DeezerTrackId,a.DeezerUrl,a.RecoveredMusicBrainzIdentity,a.Isrc,a.IsrcStatus,a.IsrcCandidateRecordingIds,a.IsrcMatchingCandidates,a.RecoveryDecision,a.PriorEvidenceRestored,a.IsrcCandidateValidationDetails));
        }
        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }
    async Task SendAuditCsv(HttpListenerContext ctx, string kind)
    {
        EvidenceAudit audit;
        lock (verificationGate) audit = latestAudit ?? throw new InvalidOperationException("Run verification before exporting evidence.");
        var bytes = BuildAuditCsv(audit, kind);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=musicbrainz-{kind}-audit.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
}
