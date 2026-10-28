using System.Net;
using System.Text;

sealed partial class DesktopServer
{
    // This is an evidence-based planning export, NOT a completed comparison
    // against Lidarr's tracks and physical files. Unknown means unknown.
    static byte[] BuildMigrationReadinessCsv(EvidenceAudit audit, List<LidarrArtist> lidarrArtists, InventoryState? inventoryResult = null)
    {
        var ids = lidarrArtists.Select(x => x.foreignArtistId)
            .Where(ValidMbid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lookup = audit.Artists.ToDictionary(x => x.SourceName, x => x, StringComparer.OrdinalIgnoreCase);
        var perRow = inventoryResult is { Done: true } ? inventoryResult.Decisions.ToDictionary(x => x.Row) :
            new Dictionary<int, InventoryDecision>();
        var sb = new StringBuilder();
        sb.Append(CsvLine("Type", "YouTube source row", "YouTube artist", "YouTube song", "YouTube album",
            "Artist MBID", "Recording MBID", "Planned category", "Evidence confidence",
            "Lidarr artist MBID present", "Lidarr song file present", "Reason", "Write action performed",
            "Lidarr track ID", "Lidarr trackfile ID", "Lidarr album ID", "Lidarr album title",
            "Lidarr track title", "Lidarr foreign recording MBID", "Comparison method", "Candidate track count",
            "Lidarr metadata status", "Release selection status", "Action eligibility (proposal only)",
            "Album target evidence", "Recording MBID disputed"));
        foreach (var artist in audit.Artists)
        {
            bool ready = artist.Status == "high" && ValidMbid(artist.ArtistId);
            bool inLidarr = ready && ids.Contains(artist.ArtistId);
            var category = !ready ? "Manual Review / Artists" : inLidarr ?
                "Already in Library / Artists" : "To Be Added / Artists (proposal only)";
            sb.Append(CsvLine("Artist", "", artist.SourceName, "", "", artist.ArtistId, "", category,
                artist.Status, inLidarr ? "yes-by-MBID" : ready ? "no-by-MBID" : "unknown",
                "not-applicable", artist.Reason, "none", "", "", "", "", "", "", "artist-mbid", "",
                inLidarr ? "artist-metadata-present" : ready ? "artist-metadata-absent" : "unknown",
                "not-applicable", ready ? inLidarr ? "no-artist-addition-needed" : "requires-artist-addition-and-approval" : "manual-review-required", "", false));
        }
        foreach (var song in audit.Songs)
        {
            lookup.TryGetValue(song.Artist, out var artist);
            bool artistSupported = artist is { Status: "high" } && ValidMbid(artist.ArtistId);
            bool songSupported = song.SongIdentityStatus == "artist-and-song-supported" &&
                (song.Status is "high" or "artist-supported/version-review");
            bool ready = artistSupported && songSupported;
            var category = ready ? "Pending Lidarr track/file comparison / Songs" : "Manual Review / Songs";
            perRow.TryGetValue(song.Row, out var decision);
            if (decision != null) category = decision.Category;
            string songFile = decision == null ? "NOT CHECKED" :
                decision.Category.StartsWith("Already in Library", StringComparison.Ordinal) ? "reported-by-Lidarr-trackfile-inventory" :
                decision.Category.StartsWith("To Be Added", StringComparison.Ordinal) ? "no-Lidarr-file-match" : "unknown-or-needs-review";
            sb.Append(CsvLine("Song", song.Row, song.Artist, song.Song, song.Album,
                artist?.ArtistId ?? song.ArtistId, song.RecordingId, category, song.Status,
                artistSupported && ids.Contains(artist!.ArtistId) ? "yes-by-MBID" :
                    artistSupported ? "no-by-MBID" : "unknown",
                songFile, decision?.Reason ?? (ready ? "Artist and song supported; actual Lidarr track and media file NOT checked" : song.Reason),
                "none", decision?.LidarrTrackId ?? "", decision?.LidarrTrackFileId ?? "",
                decision?.LidarrAlbumId ?? "", decision?.LidarrAlbum ?? "", decision?.LidarrTitle ?? "",
                decision?.LidarrForeignRecordingId ?? "", decision?.MatchMethod ?? "not-checked",
                decision?.CandidateCount.ToString() ?? "",
                decision?.MetadataStatus ?? "not-checked", decision?.ReleaseSelection ?? "not-assessed",
                decision?.ActionEligibility ?? "not-checked", decision?.AlbumTargetEvidence ?? "not-checked",
                decision?.RecordingIdentityDisputed ?? false));
        }
        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }
    async Task SendMigrationReadinessCsv(HttpListenerContext ctx)
    {
        byte[] data;
        lock (verificationGate)
        {
            if (latestAudit == null) throw new InvalidOperationException("Verification or audit import required.");
            data = BuildMigrationReadinessCsv(latestAudit, lidarrArtists, inventory);
        }
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=migration-readiness-preview.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = data.Length;
        await ctx.Response.OutputStream.WriteAsync(data);
        ctx.Response.Close();
    }
    static void MigrationReadinessSelfTest()
    {
        var audit = new EvidenceAudit();
        audit.Artists.Add(new ArtistAudit { SourceName="Example", ArtistId="056e4f3e-d505-4dad-8ec1-d04f521cbb56", Status="high" });
        audit.Songs.Add(new SongAudit { Row=1, Artist="Example", Song="Test Song", SongIdentityStatus="artist-and-song-supported",
            Status="artist-supported/version-review" });
        string data = Encoding.UTF8.GetString(BuildMigrationReadinessCsv(audit, new List<LidarrArtist> {
            new("Example", "056e4f3e-d505-4dad-8ec1-d04f521cbb56") }));
        if (!data.Contains("Already in Library / Artists") ||
            !data.Contains("Pending Lidarr track/file comparison / Songs") || !data.Contains("NOT CHECKED"))
            throw new Exception("Migration readiness CSV self-test failed");
        Console.WriteLine("PASS: Migration preview does not infer audio-file presence from artist presence");
    }
}
