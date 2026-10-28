using System.Net;
using System.Text;

sealed partial class DesktopServer
{
    // PREPARATION ONLY. Staged albums exist in memory until the application exits,
    // a new inventory comparison runs, or audit/source data changes.
    // No function in this file calls the Lidarr API with a write operation.
    readonly HashSet<int> stagedAlbumSearches = new();

    sealed record AlbumSearchStageItem(int AlbumId, string Artist, string Album,
        int SafeSongs, int OtherUnverifiedSongs, string SourceRows, bool Staged);

    static bool CanStageSafeTrack(InventoryDecision d) =>
        d.Category.StartsWith("To Be Added", StringComparison.Ordinal) &&
        !d.RecordingIdentityDisputed &&
        (d.ActionEligibility is "eligible-after-user-approval" or "requires-final-search-approval") &&
        int.TryParse(d.LidarrAlbumId, out var albumId) && albumId > 0 &&
        int.TryParse(d.LidarrTrackId, out var trackId) && trackId > 0 &&
        string.IsNullOrEmpty(d.LidarrTrackFileId);

    static List<AlbumSearchStageItem> BuildStageQueue(EvidenceAudit audit,
        IEnumerable<InventoryDecision> allDecisions, ISet<int> staged)
    {
        var decisions = allDecisions.ToArray();
        var songs = audit.Songs.ToDictionary(s => s.Row);
        return decisions.Where(CanStageSafeTrack)
            .GroupBy(d => int.Parse(d.LidarrAlbumId))
            .Select(group => {
                var safe = group.Where(d => songs.ContainsKey(d.Row)).ToArray();
                var artists = safe.Select(d => songs[d.Row].Artist).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var others = decisions.Count(d => d.LidarrAlbumId == group.Key.ToString() &&
                    !CanStageSafeTrack(d));
                return new AlbumSearchStageItem(group.Key,
                    artists.Length == 1 ? artists[0] : string.Join(" / ", artists.Take(3)) + (artists.Length > 3 ? "…" : ""),
                    safe.First().LidarrAlbum, safe.Length, others,
                    string.Join(" | ", safe.Select(d => d.Row).OrderBy(x => x)), staged.Contains(group.Key));
            })
            .OrderByDescending(x => x.SafeSongs).ThenBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Album, StringComparer.OrdinalIgnoreCase).ToList();
    }

    List<AlbumSearchStageItem> GetStagingQueue()
    {
        if (!inventory.Done || inventory.Running || latestAudit == null)
            throw new InvalidOperationException("Finish the Lidarr comparison before staging proposals.");
        return BuildStageQueue(latestAudit, inventory.Decisions, stagedAlbumSearches);
    }

    int SetStagedAlbums(IReadOnlyList<int> requestedAlbumIds, bool stage)
    {
        lock (verificationGate)
        {
            var eligible = GetStagingQueue().Select(x => x.AlbumId).ToHashSet();
            if (requestedAlbumIds.Count is < 1 or > 100 || requestedAlbumIds.Any(id => id <= 0 || !eligible.Contains(id)))
                throw new InvalidOperationException("One or more selected albums is not eligible for track-backed staging. No changes were made.");
            // Validate the ENTIRE batch before changing anything.
            foreach (var id in requestedAlbumIds.Distinct())
            {
                if (stage) stagedAlbumSearches.Add(id);
                else stagedAlbumSearches.Remove(id);
            }
            return stagedAlbumSearches.Count;
        }
    }

    async Task SendStagingQueueCsv(HttpListenerContext ctx)
    {
        List<AlbumSearchStageItem> items;
        lock (verificationGate) items = GetStagingQueue();
        var sb = new StringBuilder();
        sb.Append(CsvLine("Lidarr album ID", "Source artist", "Lidarr album", "Supported source songs",
            "Other unverified rows with this album ID", "Source song rows", "Staged locally",
            "Final search authorized", "Lidarr writes", "Important scope warning"));
        foreach (var a in items)
            sb.Append(CsvLine(a.AlbumId, a.Artist, a.Album, a.SafeSongs, a.OtherUnverifiedSongs,
                a.SourceRows, a.Staged ? "YES-LOCAL-ONLY" : "NO", "NO", "NONE",
                "A future Lidarr album search may acquire ALL tracks on the album, not only songs listed here"));
        await WriteCsvFile(ctx, sb.ToString(), "lidarr-album-staging-queue-NO-WRITES.csv");
    }

    static string ManualReviewNextStep(InventoryDecision d, SongAudit song)
    {
        if (d.Reason.Contains("Artist MBID", StringComparison.OrdinalIgnoreCase))
            return "Confirm artist identity using names, aliases, credits and existing Lidarr artist ID";
        if (d.Reason.Contains("Artist/song identity", StringComparison.OrdinalIgnoreCase))
            return "Compare song and artist metadata from source and external catalogs; never invent a MusicBrainz ID";
        if (d.Candidates.Any(x => x.TitleAgrees))
            return "Inspect album, EP/edition, and candidate recording IDs; select a release only with supporting evidence";
        return song.ExternalStatus is not "not-checked" and not "" ?
            "Review external provider evidence and available source metadata" :
            "Investigate artist/song spelling, aliases and original source metadata";
    }

    async Task SendManualReviewTriageCsv(HttpListenerContext ctx)
    {
        EvidenceAudit audit;
        InventoryDecision[] decisions;
        lock (verificationGate)
        {
            if (!inventory.Done || latestAudit == null)
                throw new InvalidOperationException("Complete the Lidarr comparison before exporting the manual-review report.");
            audit = latestAudit;
            decisions = inventory.Decisions.Where(d => d.Category.StartsWith("Manual Review", StringComparison.Ordinal)).ToArray();
        }
        var songs = audit.Songs.ToDictionary(s => s.Row);
        var artistMap = audit.Artists.ToDictionary(a => a.SourceName, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append(CsvLine("Source row", "Artist", "Song", "Source album", "Source YouTube URL",
            "Source artist MBID", "Source recording MBID", "Artist confidence", "Song identity status",
            "Song confidence", "Reason", "Match method", "Lidarr candidate count", "Candidate album titles",
            "Candidate recording MBIDs", "Apple status", "Deezer status", "External evidence", "ISRC",
            "Recommended next action", "Final Lidarr write authorized"));
        foreach (var d in decisions.OrderBy(x => x.Row))
        {
            var s = songs[d.Row];
            artistMap.TryGetValue(s.Artist, out var artist);
            sb.Append(CsvLine(d.Row, s.Artist, s.Song, s.Album, s.YouTubeUrl,
                artist?.ArtistId ?? s.ArtistId, s.RecordingId, artist?.Status ?? "unknown",
                s.SongIdentityStatus, s.Status, d.Reason, d.MatchMethod, d.CandidateCount,
                string.Join(" | ", d.Candidates.Select(x => x.Album).Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(50)),
                string.Join(" | ", d.Candidates.Select(x => x.RecordingMbid).Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(50)),
                s.AppleStatus, s.DeezerStatus, s.ExternalStatus, s.Isrc,
                ManualReviewNextStep(d, s), "NO"));
        }
        await WriteCsvFile(ctx, sb.ToString(), "lidarr-manual-review-triage.csv");
    }

    static void ApprovalQueueSelfTest()
    {
        var audit = new EvidenceAudit();
        audit.Songs.Add(new SongAudit { Row = 1, Artist = "Example", Song = "Track", Album = "EP" });
        audit.Songs.Add(new SongAudit { Row = 2, Artist = "Example", Song = "Other", Album = "EP" });
        var eligible = new InventoryDecision { Row = 1, LidarrTrackId = "21", LidarrAlbumId = "50",
            LidarrAlbum = "EP", Category = "To Be Added / Songs", ActionEligibility = "eligible-after-user-approval" };
        var disputed = new InventoryDecision { Row = 2, LidarrTrackId = "", LidarrAlbumId = "50",
            LidarrAlbum = "EP", RecordingIdentityDisputed = true, Category = "To Be Added / Songs (album-only search target)",
            ActionEligibility = "requires-final-search-approval" };
        var engine = new DesktopServer { latestAudit = audit,
            inventory = new InventoryState { Done = true, Decisions = new List<InventoryDecision> { eligible, disputed } } };
        var items = engine.GetStagingQueue();
        if (items.Count != 1 || items[0].SafeSongs != 1 || items[0].OtherUnverifiedSongs != 1 || items[0].Staged)
            throw new Exception("Staging must separate safe track-backed evidence from album-only disputed evidence.");
        try { engine.SetStagedAlbums(new[] { 50, 999 }, true); throw new Exception("Invalid album batch should fail"); }
        catch (InvalidOperationException) { }
        if (engine.stagedAlbumSearches.Count != 0)
            throw new Exception("Invalid staging batch must be atomic.");
        if (engine.SetStagedAlbums(new[] { 50 }, true) != 1 || !engine.GetStagingQueue()[0].Staged ||
            engine.SetStagedAlbums(new[] { 50 }, false) != 0)
            throw new Exception("Local album staging should support explicit stage/unstage.");
        engine.ResetLidarrComparison();
        if (engine.stagedAlbumSearches.Count != 0)
            throw new Exception("Staging must clear when the evidence or comparison resets.");
        Console.WriteLine("PASS: track-backed album staging, disputed-recording isolation, atomic validation and invalidation");
    }
}
