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
        public string Alternatives { get; set; } = "";
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
                (!r.MultipleRecordingVersions || r.AlbumMatch);
            var why = r is null || !r.RequestSucceeded ? "MusicBrainz request unsuccessful" :
                !r.Verified ? r.Reason : !r.ExactTitle ? "Title differs after normalization" :
                !r.ExactArtist ? "Artist supported only by cross-song evidence" :
                r.MultipleRecordingVersions && !r.AlbumMatch ? "Multiple recordings for same artist; exact version unproven" :
                "Exact title and artist; recording version supported";
            audit.Songs.Add(new SongAudit {
                Row = i + 1, Artist = t.Artist, Song = t.Song, Album = t.Album, YouTubeUrl = t.YouTubeUrl,
                ArtistId = r?.ArtistId ?? "", RecordingId = r?.RecordingId ?? "",
                Status = strong ? "high" : r is not null && r.Verified ? "artist-supported/version-review" :
                    r is null || !r.RequestSucceeded ? "lookup-failed" : r.Ambiguous ? "ambiguous" : "unmatched",
                Reason = why, EvidenceScore = r?.Confidence ?? 0,
                ExactTitle = r?.ExactTitle ?? false, ExactArtist = r?.ExactArtist ?? false,
                AlbumMatch = r?.AlbumMatch ?? false, CrossSongSupport = r?.CrossSongSupport ?? false,
                MultipleRecordingVersions = r?.MultipleRecordingVersions ?? false,
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
            // Three distinct corroborating songs are strong artist evidence; one overlapping title alone is not.
            bool high = best != null && independentSongs >= 3 && conflicting == 0;
            var supportedTitles = verified.Select(s => s.Song).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var sourceOnly = sourceSongs.Where(x => !supportedTitles.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            audit.Artists.Add(new ArtistAudit {
                SourceName = group.Key, ArtistId = best?.Key ?? "", Status = high ? "high" : "needs-review",
                Reason = high ? "At least three distinct source song titles independently support one MusicBrainz artist ID" :
                    conflicting > 0 ? "Conflicting MusicBrainz artist IDs across source songs" :
                    best is null ? "No confidently identified artist ID" :
                    $"Only {independentSongs} distinct song title(s) support an artist ID; need at least three for high confidence",
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
            sb.Append(CsvLine("Artist", "Artist MBID", "Confidence", "Explanation", "Unique source songs", "High-confidence song titles", "Unresolved source titles", "Already in Lidarr", "Source songs (names)", "High-confidence overlapping songs (names)", "Remaining source songs (names)", "MusicBrainz catalog completeness", "Total distinct MusicBrainz catalog songs", "MusicBrainz-only songs"));
            foreach (var a in audit.Artists) sb.Append(CsvLine(a.SourceName,a.ArtistId,a.Status,a.Reason,a.SourceSongs,a.VerifiedSongs,a.UnresolvedSongs,a.AlreadyInLidarr,a.SourceSongList,a.CorroboratedSongList,a.SourceOnlySongList,a.CatalogCoverage,"Unknown",a.MusicBrainzOnlyTitles));
        }
        else
        {
            sb.Append(CsvLine("Source row", "YouTube artist", "YouTube song", "YouTube album", "YouTube link", "MusicBrainz artist ID", "MusicBrainz recording ID", "Confidence", "Evidence/remaining issue", "MB search score (NOT confidence probability)", "Exact title", "Artist credit agrees", "Album agrees", "Cross-song support", "Multiple recording versions", "Alternate artist IDs"));
            foreach (var a in audit.Songs) sb.Append(CsvLine(a.Row,a.Artist,a.Song,a.Album,a.YouTubeUrl,a.ArtistId,a.RecordingId,a.Status,a.Reason,a.EvidenceScore,a.ExactTitle,a.ExactArtist,a.AlbumMatch,a.CrossSongSupport,a.MultipleRecordingVersions,a.Alternatives));
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
