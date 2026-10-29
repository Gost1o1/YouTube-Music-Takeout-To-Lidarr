using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // A separate, local-only decision notebook. It deliberately NEVER changes audit
    // confidence, inferred MusicBrainz IDs, stage queues, or Lidarr itself.
    sealed class ReviewBookmark
    {
        public string Key { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Source { get; set; } = "";
        public string SelectedCandidate { get; set; } = "";
        public string Disposition { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTimeOffset UpdatedUtc { get; set; }
    }
    sealed record ReviewChoice(string Id, string Label, string Evidence);
    sealed class ReviewItem
    {
        public string Key = "", Kind = "", Artist = "", Song = "", Album = "", Reason = "";
        public int Row;
        public List<ReviewChoice> Candidates = new();
        public string Disposition = "unreviewed", Selected = "", Note = "", EvidenceStatus = "current";
        public string Source = "", YouTubeUrl = "";
    }
    readonly Dictionary<string, ReviewBookmark> reviewLedger = new(StringComparer.Ordinal);
    bool reviewLedgerLoaded;
    string ReviewLedgerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MusicTakeoutToolkit", "review-decisions-v1.json");
    static string ReviewKey(string kind, string source) => kind + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).Substring(0, 32);
    static string ReviewArtistSource(string name) => name.Trim().ToUpperInvariant();
    static string ReviewSongSource(SongAudit song) => string.Join("\n", song.Row,
        song.Artist.Trim(), song.Song.Trim(), song.Album.Trim());
    static string SelectedReviewCandidate(IReadOnlyDictionary<string, ReviewBookmark> choices,
        string kind, string source)
    {
        var bookmark = choices.GetValueOrDefault(ReviewKey(kind, source));
        // A user selection is visible in planning, but never promotes the
        // independent audit confidence or authorizes a Lidarr write.
        return bookmark != null && bookmark.Kind == kind && bookmark.Source == source &&
            bookmark.Disposition == "candidate-for-planning" && ValidMbid(bookmark.SelectedCandidate)
            ? bookmark.SelectedCandidate : "";
    }
    // A review choice is a proposed addition, not a verified recording or an
    // authorization to search/download through Lidarr. Keep those distinct.
    static (string Category, string Origin, string Note) ReviewPreviewPlacement(
        string category, bool songChoice, bool artistChoice, string sourceSaveMode)
    {
        var stillReview = category.StartsWith("Manual Review", StringComparison.Ordinal);
        if (songChoice || artistChoice)
            return (stillReview ? "To Be Added / Songs (manual proposal; verification pending)" : category,
                "Manual Review", songChoice
                    ? "User selected a recording candidate; identity and album edition still require verification. No Lidarr write."
                    : "User selected an artist candidate; recording and album still require verification. No Lidarr write.");
        if (sourceSaveMode is "auto" or "manual")
            return (stillReview ? "To Be Added / Songs (source metadata; manual import required)" : category,
                sourceSaveMode == "auto" ? "Auto Manual Review" : "Manual Review",
                "Takeout metadata saved for manual import; recording identity is unverified. No Lidarr write.");
        return (category, category.StartsWith("To Be Added", StringComparison.Ordinal) ||
            category.StartsWith("Already in Library", StringComparison.Ordinal) ? "Algorithm" : "", "");
    }
    static string ReviewGroupSource(AlbumReviewGroup group) => group.Id + "\n" + group.Artist.Trim() + "\n" + group.SourceAlbum.Trim();
    static string ReviewAlbumKey(AlbumReviewGroup group) => ReviewKey("album", ReviewGroupSource(group));

    void LoadReviewLedger()
    {
        if (reviewLedgerLoaded) return;
        var path = ReviewLedgerPath;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Length > 3_000_000) throw new InvalidOperationException("Saved review notebook is unexpectedly large; inspect the file before continuing.");
            var saved = JsonSerializer.Deserialize<List<ReviewBookmark>>(text)
                ?? throw new InvalidOperationException("Saved review notebook is invalid; no changes were made.");
            if (saved.Count > 10_000) throw new InvalidOperationException("Saved review notebook exceeds the entry limit.");
            foreach (var x in saved.Where(x => x.Key.Length >= 10 && x.Kind is "artist" or "song" or "album"))
                reviewLedger[x.Key] = x;
        }
        reviewLedgerLoaded = true;
    }
    void SaveReviewLedger()
    {
        // Caller must hold verificationGate. Atomic replacement to avoid partially written JSON.
        var path = ReviewLedgerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        try
        {
            var serialized = JsonSerializer.Serialize(reviewLedger.Values.OrderBy(x => x.Key),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, serialized, new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    static void AddReviewCandidate(ReviewItem item, string value, string label, string evidence)
    {
        if (!ValidMbid(value) || item.Candidates.Any(c => c.Id.Equals(value, StringComparison.OrdinalIgnoreCase))) return;
        // UI is bounded: source audits can contain hundreds of versions for one recording.
        if (item.Candidates.Count < 12) item.Candidates.Add(new ReviewChoice(value, label, evidence));
    }
    static void AddReviewCandidates(ReviewItem item, string values, string label, string evidence)
    {
        foreach (var id in CandidateIds(values)) AddReviewCandidate(item, id, label, evidence);
    }
    // Import a previous exported candidate-scoring audit as REVIEW EVIDENCE ONLY.
    // Exact artist/row alignment with the selected Takeout ZIP is mandatory.
    // These imported search results cannot change artist/song confidence, add
    // recording IDs to the audit, or authorize any Lidarr action.
    static List<CorroborationCandidate> ParsePreviousCandidateScores(string csv, EvidenceAudit audit)
    {
        var parsed = new List<CorroborationCandidate>();
        if (string.IsNullOrWhiteSpace(csv)) return parsed;
        if (csv.Length > 1_000_000) throw new InvalidOperationException("Candidate-scoring CSV is too large.");
        using var input = new StringReader(csv);
        var headers = CsvRow(input) ?? throw new InvalidOperationException("Candidate-scoring CSV has no header.");
        var fields = headers.Select((x, i) => (x, i)).ToDictionary(x => x.x.TrimStart('\uFEFF'), x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (var mandatory in new[] { "Stage", "Source artist", "Source rows", "Candidate entity ID", "Decision", "Recording MBID candidates" })
            if (!fields.ContainsKey(mandatory)) throw new InvalidOperationException("Unsupported candidate-scoring CSV format: missing " + mandatory);
        var byRow = audit.Songs.ToDictionary(x => x.Row);
        var knownArtists = audit.Artists.Select(a => a.SourceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string Field(string[] row, string field) => fields.TryGetValue(field, out int index) && index < row.Length ? row[index].Trim() : "";
        string[]? record;
        while ((record = CsvRow(input)) != null)
        {
            if (record.All(string.IsNullOrWhiteSpace)) continue;
            if (parsed.Count >= 3_000) throw new InvalidOperationException("Too many historical candidate rows; no audit data was imported.");
            var sourceArtist = Field(record, "Source artist");
            if (!knownArtists.Contains(sourceArtist)) throw new InvalidOperationException("Candidate-scoring CSV contains an artist not present in the selected Takeout: " + sourceArtist);
            var sourceRows = Field(record, "Source rows");
            if (string.IsNullOrWhiteSpace(sourceRows))
                throw new InvalidOperationException("Historical candidate row lacks a source-row relationship.");
            foreach (var token in sourceRows.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(token, out var row) || !byRow.TryGetValue(row, out var track) ||
                    !track.Artist.Equals(sourceArtist, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Candidate-scoring CSV source rows do not match the selected Takeout archive.");
            }
            parsed.Add(new CorroborationCandidate {
                Stage = Field(record, "Stage"), SourceArtist = sourceArtist, SourceAlbum = Field(record, "Source album"),
                SourceSongs = Field(record, "Source songs"), SourceRows = sourceRows,
                CandidateId = Field(record, "Candidate entity ID"), CandidateName = Field(record, "Candidate name"),
                CandidateType = Field(record, "Candidate type"), NameRelation = Field(record, "Canonical/alias/album relation"),
                TitleMatches = int.TryParse(Field(record, "Credited matching title count"), out var titleHits) ? Math.Clamp(titleHits, 0, 2505) : 0,
                UniqueSourceTitles = int.TryParse(Field(record, "Source title count"), out var sourceTitles) ? Math.Clamp(sourceTitles, 0, 2505) : 0,
                RecordingMbids = string.Join(" | ", CandidateIds(Field(record, "Recording MBID candidates"))),
                ReleaseMbids = Field(record, "Release MBIDs examined"),
                MatchedTitles = Field(record, "Matched titles"), ConflictingTitles = Field(record, "Conflicting titles"),
                RequestStatus = "historical-review-only:" + Field(record, "Request HTTP statuses"),
                Decision = "imported-review-only:" + Field(record, "Decision"),
                Reason = Field(record, "Decision reason"),
                ArtistIdValidated = false, Changed = false
            });
        }
        return parsed;
    }

    List<ReviewItem> BuildReviewQueue()
    {
        if (latestAudit == null) throw new InvalidOperationException("Import or verify your audit evidence first.");
        LoadReviewLedger();
        var audit = latestAudit;
        var results = new List<ReviewItem>();
        var candidateSnapshot = corroboration.ToArray();
        foreach (var artist in audit.Artists.Where(a => a.Status != "high"))
        {
            var item = new ReviewItem { Kind = "artist", Artist = artist.SourceName,
                Reason = artist.Reason, Source = ReviewArtistSource(artist.SourceName) };
            // Show the listener an actual track from their Takeout instead of a
            // generic "Artist identity" placeholder. A review is still artist-level.
            var example = audit.Songs.FirstOrDefault(s => s.Artist.Equals(artist.SourceName, StringComparison.OrdinalIgnoreCase));
            if (example != null) { item.Song = example.Song; item.Album = example.Album; item.YouTubeUrl = example.YouTubeUrl; }
            item.Key = ReviewKey(item.Kind, item.Source);
            AddReviewCandidates(item, artist.ArtistIdCandidates, "MusicBrainz artist candidate", "Imported audit evidence; identity unverified");
            AddReviewCandidates(item, artist.LidarrMbIdCandidate, "Lidarr artist candidate", "Registered artist is NOT independently validated");
            AddReviewCandidates(item, artist.ArtistId, "Prior artist MBID", "Must verify artist identity independently");
            foreach (var c in candidateSnapshot.Where(c => c.Stage == "artist-id" &&
                c.SourceArtist.Equals(artist.SourceName, StringComparison.OrdinalIgnoreCase)))
                AddReviewCandidate(item, c.CandidateId, c.CandidateName.Length > 0 ? c.CandidateName : "MusicBrainz artist",
                    c.Decision + "; credited title matches " + c.TitleMatches);
            ApplyReviewBookmark(item);
            results.Add(item);
        }
        var reviewedRows = inventory.Done && !inventory.Running ? inventory.Decisions
            .Where(d => d.Category.StartsWith("Manual Review", StringComparison.Ordinal))
            .Select(d => d.Row).ToHashSet() : null;
        foreach (var song in audit.Songs.Where(s => reviewedRows != null ? reviewedRows.Contains(s.Row) :
            s.SongIdentityStatus != "artist-and-song-supported"))
        {
            var item = new ReviewItem { Kind = "song", Row = song.Row, Artist = song.Artist,
                Song = song.Song, Album = song.Album, Reason = song.Reason,
                Source = ReviewSongSource(song), YouTubeUrl = song.YouTubeUrl };
            item.Key = ReviewKey(item.Kind, item.Source);
            AddReviewCandidates(item, song.IsrcCandidateRecordingIds, "ISRC recording candidate", "Review recording version and credited artist");
            AddReviewCandidates(item, song.RecordingId, "Prior recording identity", "Verify its edition/version before use");
            // A candidate in the score sheet is not a verified match. Keep it review-only.
            foreach (var c in candidateSnapshot.Where(c => c.SourceArtist.Equals(song.Artist, StringComparison.OrdinalIgnoreCase) &&
                c.Stage != "artist-id" && (c.SourceRows.Split('|', StringSplitOptions.TrimEntries)
                    .Any(row => int.TryParse(row, out var n) && n == song.Row) ||
                    c.MatchedTitles.Split('|', StringSplitOptions.TrimEntries)
                    .Any(t => Normalize(t) == Normalize(song.Song) && Normalize(song.Song).Length > 0))))
                AddReviewCandidates(item, c.RecordingMbids, "MusicBrainz credited track candidate", c.Decision + "; " + c.Reason);
            if (inventory.Done && !inventory.Running)
                foreach (var d in inventory.Decisions.Where(d => d.Row == song.Row))
                    foreach (var c in d.Candidates)
                        AddReviewCandidate(item, c.RecordingMbid, "Lidarr candidate recording", "Track and edition must be confirmed; title match=" + c.TitleAgrees);
            ApplyReviewBookmark(item);
            results.Add(item);
        }
        return results.OrderBy(i => i.Disposition != "unreviewed" && i.EvidenceStatus == "current" ? 1 : 0)
            .ThenByDescending(i => i.Candidates.Count > 0).ThenBy(i => i.Kind == "artist" ? 0 : 1)
            .ThenBy(i => i.Row).ThenBy(i => i.Artist, StringComparer.OrdinalIgnoreCase).ToList();
    }
    void ApplyReviewBookmark(ReviewItem item)
    {
        if (!reviewLedger.TryGetValue(item.Key, out var bookmark) || bookmark.Kind != item.Kind || bookmark.Source != item.Source) return;
        item.Disposition = bookmark.Disposition;
        item.Selected = bookmark.SelectedCandidate;
        item.Note = bookmark.Note;
        if (item.Disposition == "candidate-for-planning" &&
            !item.Candidates.Any(c => c.Id.Equals(item.Selected, StringComparison.OrdinalIgnoreCase)))
            item.EvidenceStatus = "stale-candidate-requires-review";
    }
    void SaveReviewDecision(string key, string disposition, string candidateId, string note)
    {
        lock (verificationGate)
        {
            var item = BuildReviewQueue().FirstOrDefault(x => x.Key == key)
                ?? throw new InvalidOperationException("This review row no longer exists in the current audit. Refresh the queue.");
            if (note.Length > 400) throw new InvalidOperationException("Review note must be at most 400 characters.");
            if (disposition is not ("candidate-for-planning" or "needs-research" or "defer" or "clear"))
                throw new InvalidOperationException("Unknown review decision.");
            if (disposition == "candidate-for-planning" &&
                !item.Candidates.Any(c => c.Id.Equals(candidateId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Select a candidate from the CURRENT evidence. No change was saved.");
            if (disposition == "clear") reviewLedger.Remove(key);
            else reviewLedger[key] = new ReviewBookmark { Key = key, Kind = item.Kind, Source = item.Source,
                SelectedCandidate = disposition == "candidate-for-planning" ? candidateId : "",
                Disposition = disposition, Note = note, UpdatedUtc = DateTimeOffset.UtcNow };
            SaveReviewLedger();
        }
    }
    void SaveAlbumReviewBookmark(AlbumReviewGroup group, int albumId)
    {
        // Called under verificationGate from an explicit user album-approval action only.
        LoadReviewLedger();
        var id = ReviewAlbumKey(group);
        reviewLedger[id] = new ReviewBookmark { Key = id, Kind = "album", Source = ReviewGroupSource(group),
            Disposition = "candidate-for-planning", SelectedCandidate = albumId.ToString(), UpdatedUtc = DateTimeOffset.UtcNow };
        SaveReviewLedger();
    }
    ReviewBookmark? GetAlbumReviewBookmark(AlbumReviewGroup group)
    {
        lock (verificationGate)
        {
            LoadReviewLedger();
            var id = ReviewAlbumKey(group);
            return reviewLedger.TryGetValue(id, out var choice) && choice.Kind == "album" && choice.Source == ReviewGroupSource(group) ? choice : null;
        }
    }
    void ReapplySavedAlbumReview(string groupId)
    {
        lock (verificationGate)
        {
            if (!inventory.Done || inventory.Running) throw new InvalidOperationException("Run Lidarr comparison first.");
            var group = inventory.AlbumGroups.FirstOrDefault(g => g.Id == groupId)
                ?? throw new InvalidOperationException("Album review group is no longer available.");
            var saved = GetAlbumReviewBookmark(group)
                ?? throw new InvalidOperationException("No saved album decision matches this source artist and album.");
            if (!int.TryParse(saved.SelectedCandidate, out var id) || !group.Choices.Any(c => c.AlbumId == id && c.Complete))
                throw new InvalidOperationException("Saved choice is no longer completely supported by the current Lidarr inventory; review manually.");
            ApproveAlbumGroupSelection(groupId, id); // Existing strict checks; no Lidarr writes.
        }
    }
    void ForgetAlbumReview(string groupId)
    {
        lock (verificationGate)
        {
            var group = inventory.AlbumGroups.FirstOrDefault(g => g.Id == groupId)
                ?? throw new InvalidOperationException("Album group not available.");
            LoadReviewLedger();
            reviewLedger.Remove(ReviewAlbumKey(group));
            SaveReviewLedger();
        }
    }
    async Task SendReviewQueue(HttpListenerContext ctx)
    {
        List<ReviewItem> items;
        int savedAlbumCount;
        List<ManualFallbackEntry> manualEntries;
        HashSet<string> restored;
        object progress;
        lock (verificationGate)
        {
            var allItems = BuildReviewQueue();
            // Automatic filing is performed by the completed pre-review pass.
            // Never remove an entry as a side effect of fetching a card or queue.
            manualEntries = CurrentManualFallbacks(allItems);
            var handled = manualEntries.Select(x => x.ReviewKey).ToHashSet(StringComparer.Ordinal);
            restored = RestoredManualFallbackKeys();
            items = allItems.Where(x => !handled.Contains(x.Key)).ToList();
            savedAlbumCount = inventory.Done ? inventory.AlbumGroups.Count(g => GetAlbumReviewBookmark(g) != null) : 0;
            // Base every counter on the *same* current review queue and saved
            // decisions. Verification's earlier totals can be stale after a
            // manual choice or an automatic source-only save.
            object ForKind(string kind, int total)
            {
                var unresolved = allItems.Where(x => x.Kind == kind).ToArray();
                var addressed = unresolved.Count(x =>
                    handled.Contains(x.Key) ||
                    ((x.Disposition is "candidate-for-planning" or "needs-research" or "defer") &&
                     x.EvidenceStatus == "current"));
                var automatic = Math.Max(0, total - unresolved.Length);
                return new { total, automatic, reviewed = addressed,
                    addressed = Math.Min(total, automatic + addressed),
                    pending = Math.Max(0, unresolved.Length - addressed),
                    unresolvedIdentities = unresolved.Length };
            }
            progress = new {
                artist = ForKind("artist", latestAudit!.Artists.Count),
                song = ForKind("song", latestAudit.Songs.Count)
            };
        }
        await Respond(ctx, 200, new { items = items.Select(i => new {
            key = i.Key, kind = i.Kind, row = i.Row, manualOverride = restored.Contains(i.Key), artist = i.Artist, song = i.Song,
            album = i.Album, youtubeUrl = i.YouTubeUrl, reason = i.Reason, disposition = i.Disposition,
            selected = i.Selected, note = i.Note, evidenceStatus = i.EvidenceStatus,
            candidates = i.Candidates.Select(c => new { id = c.Id, label = c.Label, evidence = c.Evidence }).ToArray()
        }).ToArray(), savedAlbumCount, reviewProgress = progress,
            savedDecisions = items.Count(i => i.Disposition != "unreviewed"),
            manualItems = manualEntries.Select(x => new { key = x.ReviewKey, kind = x.Kind,
                artist = x.Artist, song = x.Song, album = x.Album, youtubeUrl = x.YouTubeUrl,
                saveMode = x.SaveMode, reason = x.Reason, updatedUtc = x.UpdatedUtc }).ToArray(),
            manualCount = manualEntries.Count,
            note = "Saved-for-manual-import entries are source metadata only; no MusicBrainz identities were verified, no audio was downloaded, and no Lidarr changes occurred." });
    }
    async Task SendReviewDecisionsCsv(HttpListenerContext ctx)
    {
        List<ReviewBookmark> rows;
        lock (verificationGate) { LoadReviewLedger(); rows = reviewLedger.Values.OrderBy(x => x.Kind).ThenBy(x => x.Key).ToList(); }
        var sb = new StringBuilder();
        sb.Append(CsvLine("Entity", "Evidence key", "Source descriptor", "User disposition", "Selected candidate ID", "User note", "Saved UTC", "Recording/artist identity independently verified", "Lidarr search/write/download authorized"));
        foreach (var x in rows)
            sb.Append(CsvLine(x.Kind, x.Key, x.Source, x.Disposition, x.SelectedCandidate,
                x.Note, x.UpdatedUtc.ToString("O"), "NO - user review only", "NO"));
        await WriteCsvFile(ctx, sb.ToString(), "music-review-decisions-NO-WRITES.csv");
    }
    static void GuidedReviewSelfTests()
    {
        var manualProposal = ReviewPreviewPlacement("Manual Review / Songs", true, false, "");
        var artistProposal = ReviewPreviewPlacement("Manual Review / Songs", false, true, "");
        var autoSource = ReviewPreviewPlacement("Manual Review / Songs", false, false, "auto");
        var pending = ReviewPreviewPlacement("Manual Review / Songs", false, false, "");
        if (!manualProposal.Category.StartsWith("To Be Added", StringComparison.Ordinal) ||
            manualProposal.Origin != "Manual Review" ||
            !artistProposal.Category.StartsWith("To Be Added", StringComparison.Ordinal) ||
            !artistProposal.Note.Contains("artist candidate", StringComparison.Ordinal) ||
            !autoSource.Category.StartsWith("To Be Added", StringComparison.Ordinal) ||
            autoSource.Origin != "Auto Manual Review" ||
            pending.Category != "Manual Review / Songs" || pending.Origin.Length != 0)
            throw new Exception("Review decisions must reach the proposal list without inventing verified identities or hiding unreviewed rows.");
        var example = new SongAudit { Row = 17, Artist = "Sample", Song = "Single", Album = "EP" };
        var same = new SongAudit { Row = 17, Artist = "Sample", Song = "Single", Album = "EP" };
        var changed = new SongAudit { Row = 17, Artist = "Sample", Song = "Single", Album = "Deluxe EP" };
        if (ReviewKey("song", ReviewSongSource(example)) != ReviewKey("song", ReviewSongSource(same)) ||
            ReviewKey("song", ReviewSongSource(example)) == ReviewKey("song", ReviewSongSource(changed)))
            throw new Exception("Review decisions must bind to a stable source row and never transfer silently to a changed edition.");
        var item = new ReviewItem { Kind = "song" };
        AddReviewCandidate(item, "not-an-id", "bad", "bad");
        if (item.Candidates.Count != 0) throw new Exception("Malformed review candidate accepted.");
        AddReviewCandidate(item, "054b38c1-5e01-4a44-a407-1bb2a3db0f7e", "good", "candidate-only");
        AddReviewCandidate(item, "054b38c1-5e01-4a44-a407-1bb2a3db0f7e", "duplicate", "candidate-only");
        if (item.Candidates.Count != 1) throw new Exception("Review candidates must be distinct and validated.");
        var testAudit = new EvidenceAudit();
        testAudit.Artists.Add(new ArtistAudit { SourceName = "Sample" });
        testAudit.Songs.Add(example);
        var historical = CsvLine("Stage", "Source artist", "Source rows", "Candidate entity ID", "Decision", "Recording MBID candidates") +
            CsvLine("v0916-recording-first-review", "Sample", "17", "", "recording-first-review-only",
                "054b38c1-5e01-4a44-a407-1bb2a3db0f7e");
        var recovered = ParsePreviousCandidateScores(historical, testAudit);
        if (recovered.Count != 1 || recovered[0].ArtistIdValidated || recovered[0].Changed ||
            !recovered[0].Decision.StartsWith("imported-review-only:", StringComparison.Ordinal))
            throw new Exception("Historical candidate import must be row-matched and never auto-promote an identity.");
        try
        {
            ParsePreviousCandidateScores(historical.Replace("Sample", "Different Artist"), testAudit);
            throw new Exception("Mismatched artist evidence must be rejected.");
        }
        catch (InvalidOperationException) { }
        var saved = new ReviewBookmark { Key = "song:123", Kind = "song", Source = ReviewSongSource(example),
            SelectedCandidate = "054b38c1-5e01-4a44-a407-1bb2a3db0f7e", Disposition = "candidate-for-planning",
            Note = "Version still uncertain", UpdatedUtc = DateTimeOffset.UtcNow };
        var roundtrip = JsonSerializer.Deserialize<ReviewBookmark>(JsonSerializer.Serialize(saved));
        if (roundtrip == null || roundtrip.Source != saved.Source || roundtrip.SelectedCandidate != saved.SelectedCandidate ||
            roundtrip.Note != saved.Note)
            throw new Exception("Saved review decisions must survive JSON serialization without losing evidence binding or notes.");
        var savedSource = ReviewSongSource(example);
        saved.Key = ReviewKey("song", savedSource);
        var decisions = new Dictionary<string, ReviewBookmark> { [saved.Key] = saved };
        if (SelectedReviewCandidate(decisions, "song", savedSource) != saved.SelectedCandidate ||
            SelectedReviewCandidate(decisions, "song", ReviewSongSource(changed)).Length != 0 ||
            SelectedReviewCandidate(decisions, "artist", savedSource).Length != 0)
            throw new Exception("Only an exact current-source review selection may appear in the migration preview.");
        var album = new AlbumReviewGroup { Id = AlbumGroupKey("Sample", "EP"), Artist = "Sample", SourceAlbum = "EP" };
        if (ReviewAlbumKey(album) != ReviewAlbumKey(new AlbumReviewGroup { Id = album.Id, Artist = "Sample", SourceAlbum = "EP" }))
            throw new Exception("Album review keys must be reproducible between versions.");
        Console.WriteLine("PASS: v0.9.1.7 review keys, bounded candidate choices, album-choice fingerprints and non-write review ledger");
    }
}
