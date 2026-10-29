using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

sealed partial class DesktopServer
{
    // Session-only, read-only album-level planning. A release approval here does
    // NOT authorize searching, downloading, adding, or modifying anything in Lidarr.
    sealed class AlbumGroupChoice
    {
        public int AlbumId;
        public string Title = "";
        public int SupportedRows;
        public string TitleRelationship = "different-title";
        public bool Complete;
        public string Explanation = "";
    }
    sealed class AlbumReviewGroup
    {
        public string Id = "", Artist = "", SourceAlbum = "";
        public int Songs, SuggestedAlbumId, ApprovedAlbumId;
        public string Recommendation = "none", Explanation = "";
        public List<int> SourceRows = new();
        public List<AlbumGroupChoice> Choices = new();
    }

    // These relationships generate explicit review suggestions, NOT exact recording matches.
    // In particular 'Volume' vs 'Vol.' is a spelling variation, whereas Deluxe,
    // Special, Anniversary, etc. can represent materially different editions.
    static string AlbumCore(string text)
    {
        var cleaned = text.Trim();
        cleaned = Regex.Replace(cleaned,
            @"\s*[\(\[]\s*(?:deluxe(?:\s+edition|\s+version)?|expanded(?:\s+edition)?|special\s+(?:edition|version)|complete\s+edition|remaster(?:ed)?(?:\s+edition|\s+version)?|\d+(?:st|nd|rd|th)\s+anniversary\s+edition|platinum\s+edition|bonus\s+tracks?)\s*[\)\]]\s*$",
            "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned,
            @"\s*[-:]\s*(?:the\s+)?(?:complete\s+confection|paradise\s+edition|platinum\s+edition)\s*$",
            "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+(?:deluxe\s+edition|expanded\s+edition)\s*$", "", RegexOptions.IgnoreCase);
        return Normalize(cleaned);
    }
    static string VolumeLabel(string text)
    {
        var normalized = Regex.Replace(text, @"\bvolume\s*(\d+)\b", "vol$1", RegexOptions.IgnoreCase);
        return Regex.Replace(normalized, @"\bvol\.?\s*(\d+)\b", "vol$1", RegexOptions.IgnoreCase);
    }
    static bool SameAlbumExact(string source, string candidate)
    {
        var normalized = Normalize(source);
        // Important: purely symbolic titles must not compare as equal just because
        // the existing generic Normalize removes every non-letter symbol.
        return normalized.Length > 0 && normalized == Normalize(candidate);
    }
    static string AlbumTitleRelationship(string source, string candidate)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate)) return "different-title";
        if (SameAlbumExact(source, candidate)) return "exact-normalized-title";
        // Dedicated glyph variant: <|º_º|> versus <|°_°|>. Never equate all
        // otherwise non-alphanumeric titles after stripping punctuation.
        var sourceGlyph = source.Trim().Replace('º', '°');
        var candidateGlyph = candidate.Trim().Replace('º', '°');
        if (sourceGlyph == candidateGlyph && source.Trim() != candidate.Trim())
            return "unicode-glyph-variant";
        var volumeSource = Normalize(VolumeLabel(source));
        if (volumeSource.Length >= 5 && volumeSource == Normalize(VolumeLabel(candidate)))
            return "volume-notation-variant";
        // EP/E.P./Extended Play is a *release-type clue*, not evidence that
        // a standard album and EP are the same release or recording.
        // Require a nontrivial shared title. EP-only titles are not aliased.
        static string RemoveEpSuffix(string value) => Regex.Replace(value.Trim(),
            @"\s*(?:\(\s*e\.?p\.?\s*\)|\[\s*e\.?p\.?\s*\]|\b(?:e\.?p\.?|extended\s+play))\s*$",
            "", RegexOptions.IgnoreCase).Trim();
        var epSource = Normalize(RemoveEpSuffix(source));
        var epCandidate = Normalize(RemoveEpSuffix(candidate));
        if (epSource.Length >= 5 && epSource == epCandidate &&
            (RemoveEpSuffix(source) != source.Trim() || RemoveEpSuffix(candidate) != candidate.Trim()))
            return "ep-suffix-variant";
        var core = AlbumCore(source);
        if (core.Length >= 5 && core == AlbumCore(candidate)) return "edition-or-suffix-variant";
        return "different-title";
    }
    static bool IsReviewableAlbumAlias(string relationship) =>
        relationship is "edition-or-suffix-variant" or "volume-notation-variant" or "unicode-glyph-variant" or "ep-suffix-variant";
    static string AlbumGroupKey(string artist, string album)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(artist.Trim().ToLowerInvariant() + "\n" + album.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 12));
    }
    // Only albums backed by a validated source artist/title and one of the
    // explicit evidence tiers may be offered as choices. Release/recording IDs
    // are NEVER deemed equivalent solely because their text resembles another.
    static bool SupportedForAlbumPlanning(InventoryDecision decision, LidarrCandidateEvidence candidate, SongAudit song)
    {
        if (!candidate.TitleAgrees || candidate.AlbumId <= 0 || candidate.HasFile || candidate.TrackFileId > 0)
            return false;
        if (decision.MatchMethod is "edition-alias-album-candidate" or "album-title-alias-candidate")
            return IsReviewableAlbumAlias(AlbumTitleRelationship(song.Album, candidate.Album));
        if (decision.MatchMethod == "multiple-source-albums-recording-id-disputed")
            return candidate.AlbumAgrees;
        return candidate.RecordingIdAgrees;
    }

    static List<AlbumReviewGroup> BuildAlbumReviewGroups(EvidenceAudit audit, List<InventoryDecision> decisions)
    {
        var byRow = audit.Songs.ToDictionary(x => x.Row);
        var held = decisions.Where(d => d.ActionEligibility == "requires-release-selection-before-search" && byRow.ContainsKey(d.Row))
            .GroupBy(d => (Artist: byRow[d.Row].Artist.Trim().ToLowerInvariant(), Album: byRow[d.Row].Album.Trim().ToLowerInvariant()));
        var groups = new List<AlbumReviewGroup>();
        foreach (var cluster in held)
        {
            var rows = cluster.OrderBy(d => d.Row).ToArray();
            var source = byRow[rows[0].Row];
            var group = new AlbumReviewGroup {
                Id = AlbumGroupKey(source.Artist, source.Album), Artist = source.Artist, SourceAlbum = source.Album,
                Songs = rows.Length, SourceRows = rows.Select(x => x.Row).ToList()
            };
            var candidates = rows.SelectMany(d => d.Candidates.Where(c => SupportedForAlbumPlanning(d, c, byRow[d.Row])))
                .GroupBy(c => c.AlbumId).Where(x => x.Key > 0).ToArray();
            foreach (var candidatesForAlbum in candidates)
            {
                var list = candidatesForAlbum.ToArray();
                var count = list.Select(c => c.TrackId).Distinct().Count();
                // Multiple candidate tracks within ONE album are acceptable for
                // selecting that ALBUM as a future search target only when the
                // source recording ID is already marked disputed. Never select
                // an arbitrary candidate track/master in this situation.
                var supportedRows = rows.Count(d => {
                    var possible = d.Candidates.Where(c => c.AlbumId == candidatesForAlbum.Key &&
                        SupportedForAlbumPlanning(d, c, byRow[d.Row])).ToArray();
                    return possible.Length == 1 || (d.RecordingIdentityDisputed && possible.Length > 1);
                });
                var rel = AlbumTitleRelationship(source.Album, list[0].Album);
                group.Choices.Add(new AlbumGroupChoice {
                    AlbumId = candidatesForAlbum.Key, Title = list[0].Album, SupportedRows = supportedRows,
                    TitleRelationship = rel, Complete = supportedRows == rows.Length,
                    Explanation = $"{supportedRows} of {rows.Length} held songs have a supported album-search target; {rel}; {count} candidate tracks (exact version may remain unverified)"
                });
            }
            group.Choices = group.Choices.OrderByDescending(c => c.Complete)
                .ThenBy(c => c.TitleRelationship == "exact-normalized-title" ? 0 : IsReviewableAlbumAlias(c.TitleRelationship) ? 1 : 2)
                .ThenByDescending(c => c.SupportedRows).ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var hasVersionDispute = rows.Any(d => d.RecordingIdentityDisputed);
            var hasEditionDifference = rows.Any(d => d.MatchMethod is "edition-alias-album-candidate" or "album-title-alias-candidate");
            var complete = group.Choices.Where(c => c.Complete).ToArray();
            var albumLikely = complete.Where(c => c.TitleRelationship == "exact-normalized-title" || IsReviewableAlbumAlias(c.TitleRelationship)).ToArray();
            if (albumLikely.Length == 1)
            {
                group.SuggestedAlbumId = albumLikely[0].AlbumId;
                group.Recommendation = albumLikely[0].TitleRelationship == "exact-normalized-title"
                    ? "source-album-agrees" : "possible-edition-alias-review";
                group.Explanation = albumLikely[0].TitleRelationship == "exact-normalized-title"
                    ? "Full group has exact normalized source-album and recording-ID support; approval still required"
                    : "Full group shares the recording IDs, but source edition differs from Lidarr album title. Confirm edition before approving";
            }
            else if (complete.Length == 1 && rows.Length >= 2)
            {
                group.SuggestedAlbumId = complete[0].AlbumId;
                group.Recommendation = "group-coverage-only-review";
                group.Explanation = "Only one candidate album covers all eligible held source song titles; title differs from source, so inspect before selecting";
            }
            else
            {
                group.Recommendation = complete.Length > 1 ? "multiple-complete-albums-review" : "incomplete-group-review";
                group.Explanation = complete.Length > 1 ? "Several albums include all held tracks; album choice cannot be inferred safely"
                    : "No one album has exactly one matching track for every held song; individual decisions may be needed";
            }
            if (hasVersionDispute)
            {
                group.Recommendation = hasEditionDifference ? "edition-and-recording-dispute-review"
                    : "album-target-recording-dispute-review";
                group.Explanation = "The album contains all eligible source song titles, but one or more MusicBrainz recording IDs DIFFER. The album may be selected for planning; no exact recording/version is confirmed.";
            }
            if (hasEditionDifference)
                group.Explanation += " Source edition differs; confirm the intended edition before choosing any album.";
            groups.Add(group);
        }
        return groups.OrderByDescending(g => g.Songs).ThenBy(g => g.Artist, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.SourceAlbum, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void ApproveAlbumGroupSelection(string groupId, int albumId)
    {
        lock (verificationGate)
        {
            if (!inventory.Done || inventory.Running || latestAudit == null)
                throw new InvalidOperationException("Finish the read-only Lidarr comparison before reviewing album groups.");
            var group = inventory.AlbumGroups.FirstOrDefault(g => g.Id == groupId)
                ?? throw new InvalidOperationException("Album review group no longer exists. Refresh the comparison.");
            var selected = group.Choices.FirstOrDefault(c => c.AlbumId == albumId && c.Complete)
                ?? throw new InvalidOperationException("This album does not contain one compatible recording for every song in the group. Choose a fully covered album or review individually.");
            var affected = inventory.Decisions.Where(d => group.SourceRows.Contains(d.Row) &&
                d.ActionEligibility == "requires-release-selection-before-search").ToArray();
            if (affected.Length != group.Songs)
                throw new InvalidOperationException("Album group changed since comparison. Re-run comparison before approving.");
            var chosen = new List<(InventoryDecision Decision, LidarrCandidateEvidence Candidate)>();
            foreach (var d in affected)
            {
                var sourceSong = latestAudit.Songs.First(s => s.Row == d.Row);
                var matches = d.Candidates.Where(c => c.AlbumId == albumId && SupportedForAlbumPlanning(d, c, sourceSong)).ToArray();
                if (matches.Length == 0 || (matches.Length > 1 && !d.RecordingIdentityDisputed))
                    throw new InvalidOperationException($"Row {d.Row} has no safe album-search target on that release; no selections were saved.");
                // Album-only planning permits multiple track versions but never
                // selects one of their MusicBrainz recording IDs as definitive.
                chosen.Add((d, matches[0]));
            }
            // This updates a SESSION PLAN ONLY. We never call POST/PUT/DELETE Lidarr endpoints.
            foreach (var pair in chosen)
            {
                var d = pair.Decision;
                var c = pair.Candidate;
                d.LidarrAlbumId = c.AlbumId.ToString();
                d.LidarrAlbum = c.Album;
                // When a recording MBID differs, the selected ALBUM is a search
                // target, not evidence that an individual Lidarr track is the
                // same recording. Keep the original MBID and mark the conflict.
                d.LidarrTrackId = d.RecordingIdentityDisputed ? "" : c.TrackId.ToString();
                d.LidarrTitle = c.Title;
                d.LidarrForeignRecordingId = d.RecordingIdentityDisputed ? "" : c.RecordingMbid;
                d.ReleaseSelection = d.RecordingIdentityDisputed
                    ? "user-selected-album-group; recording-version-unverified"
                    : "user-selected-album-group-no-write";
                d.ActionEligibility = "requires-final-search-approval";
                d.MatchMethod = d.RecordingIdentityDisputed
                    ? "user-selected-album-target-mbid-disputed" : "user-selected-release-supported-by-mbid";
                d.AlbumTargetEvidence = d.RecordingIdentityDisputed
                    ? "user-selected-album; recording-version-unverified" : "user-selected-album; exact-recording-id";
                d.Category = "To Be Added / Songs (release selected; no download)";
                d.Reason = d.RecordingIdentityDisputed
                    ? "User selected an album for SEARCH PLANNING ONLY. MusicBrainz recording IDs are different and are not accepted as equivalent; no Lidarr changes authorized."
                    : "User approved the release for planning only; no Lidarr modification, search, or download authorized";
            }
            // Prior staged album searches cannot silently absorb a changed release choice.
            stagedAlbumSearches.Clear();
            group.ApprovedAlbumId = selected.AlbumId;
            inventory.ReleaseChoicesPending = inventory.Decisions.Count(d => d.ActionEligibility == "requires-release-selection-before-search");
            inventory.GroupApprovals = inventory.AlbumGroups.Count(g => g.ApprovedAlbumId > 0);
            inventory.AlbumAliasesPending = inventory.Decisions.Count(d => d.MatchMethod == "album-title-alias-candidate");
            inventory.AlbumOnlySearchTargets = inventory.Decisions.Count(d => d.ActionEligibility == "requires-final-search-approval" &&
                int.TryParse(d.LidarrAlbumId, out var albumId) && albumId > 0 && d.LidarrTrackId.Length == 0);
            inventory.SearchReady = inventory.Decisions.Count(d => d.ActionEligibility is "eligible-after-user-approval" or "requires-final-search-approval");
            inventory.ToAdd = inventory.Decisions.Count(d => d.Category.StartsWith("To Be Added", StringComparison.Ordinal));
            inventory.Review = inventory.Decisions.Count(d => d.Category.StartsWith("Manual Review", StringComparison.Ordinal));
        }
    }

    async Task SendAlbumGroupsCsv(HttpListenerContext ctx)
    {
        AlbumReviewGroup[] groups;
        lock (verificationGate)
        {
            if (!inventory.Done) throw new InvalidOperationException("Run the Lidarr comparison first.");
            groups = inventory.AlbumGroups.ToArray();
        }
        var sb = new StringBuilder();
        sb.Append(CsvLine("Group ID", "Source artist", "Source album", "Songs held", "Source row IDs", "Candidate Lidarr album ID",
            "Candidate album title", "Compatible source rows", "Complete group coverage", "Album title relation", "Recommendation",
            "Suggested album ID", "User-selected album ID (planning only)", "Explanation", "Candidate explanation"));
        foreach (var group in groups)
        {
            if (group.Choices.Count == 0)
                sb.Append(CsvLine(group.Id, group.Artist, group.SourceAlbum, group.Songs,
                    string.Join(" | ", group.SourceRows), "", "", 0, false, "unknown", group.Recommendation,
                    group.SuggestedAlbumId, group.ApprovedAlbumId, group.Explanation, "no compatible candidates"));
            foreach (var candidate in group.Choices)
                sb.Append(CsvLine(group.Id, group.Artist, group.SourceAlbum, group.Songs,
                    string.Join(" | ", group.SourceRows), candidate.AlbumId, candidate.Title, candidate.SupportedRows,
                    candidate.Complete, candidate.TitleRelationship, group.Recommendation, group.SuggestedAlbumId,
                    group.ApprovedAlbumId, group.Explanation, candidate.Explanation));
        }
        await WriteCsvFile(ctx, sb.ToString(), "lidarr-album-review-groups.csv");
    }

    // Centralized, testable planner: an album-only target is actionable for
    // an approved album search even when no individual track ID can be trusted.
    // Nothing here initiates a search or writes to Lidarr.
    static (string Key, string Operation, bool AlbumOnly) PlanOneAction(
        InventoryDecision d, HashSet<string> proposedAlbums)
    {
        bool ready = d.ActionEligibility is "eligible-after-user-approval" or "requires-final-search-approval";
        var key = ready && int.TryParse(d.LidarrAlbumId, out int targetAlbumId) && targetAlbumId > 0
            ? "lidarr-album:" + targetAlbumId : "";
        bool albumOnly = key.Length > 0 && d.LidarrTrackId.Length == 0;
        var operation = d.Category.StartsWith("Already in Library", StringComparison.Ordinal)
            ? "none-file-present"
            : key.Length > 0 ? proposedAlbums.Add(key)
                ? albumOnly ? "propose-album-search-only-recording-not-verified-after-separate-approval"
                  : "propose-one-album-search-after-separate-approval"
                : "covered-by-other-proposed-search-for-same-album"
            : d.ActionEligibility == "requires-release-selection-before-search" ? "hold-for-album-choice"
              : "hold-for-metadata-or-manual-review";
        return (key, operation, albumOnly);
    }

    async Task SendActionPlanCsv(HttpListenerContext ctx)
    {
        InventoryDecision[] decisions;
        EvidenceAudit audit;
        HashSet<int> stagedNow;
        lock (verificationGate)
        {
            if (!inventory.Done || latestAudit == null) throw new InvalidOperationException("Run the Lidarr comparison first.");
            decisions = inventory.Decisions.ToArray(); audit = latestAudit;
            stagedNow = stagedAlbumSearches.ToHashSet();
        }
        var source = audit.Songs.ToDictionary(x => x.Row);
        var proposedAlbums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append(CsvLine("Source row", "Artist", "Song", "Source album", "Recording MBID", "Artist MBID",
            "Lidarr album ID", "Lidarr track ID", "Selected Lidarr album", "Proposed operation",
            "Release selection", "Eligibility and hold reason", "Album search deduplication key", "Final search approved", "Lidarr write executed", "Notes",
            "Album-only target", "Exact recording ID corroborated", "Album target evidence", "Locally staged (not final approval)"));
        foreach (var d in decisions.OrderBy(d => d.Row))
        {
            var song = source.GetValueOrDefault(d.Row);
            if (song == null) continue;
            var (albumKey, operation, albumOnly) = PlanOneAction(d, proposedAlbums);
            sb.Append(CsvLine(d.Row, song.Artist, song.Song, song.Album, song.RecordingId, song.ArtistId,
                d.LidarrAlbumId, d.LidarrTrackId, d.LidarrAlbum, operation, d.ReleaseSelection,
                d.ActionEligibility, albumKey, "NO", "NO", d.Reason, albumOnly,
                !d.RecordingIdentityDisputed && d.LidarrTrackId.Length > 0, d.AlbumTargetEvidence,
                !d.RecordingIdentityDisputed && int.TryParse(d.LidarrAlbumId, out var stagedId) && stagedNow.Contains(stagedId) && d.LidarrTrackId.Length > 0 ? "YES-LOCAL-ONLY" : "NO"));
        }
        await WriteCsvFile(ctx, sb.ToString(), "lidarr-proposed-action-plan-NO-WRITES.csv");
    }

    static async Task WriteCsvFile(HttpListenerContext ctx, string csv, string fileName)
    {
        var bytes = new UTF8Encoding(true).GetBytes(csv);
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    static void AlbumGroupSelfTest()
    {
        const string id = "aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa";
        var audit = new EvidenceAudit();
        audit.Songs.Add(new SongAudit { Row = 1, Artist = "Example", Album = "Bangarang EP", Song = "First", RecordingId = id });
        audit.Songs.Add(new SongAudit { Row = 2, Artist = "Example", Album = "Bangarang EP", Song = "Second", RecordingId = id });
        var decisions = new List<InventoryDecision>();
        for (int i = 1; i <= 2; i++)
        {
            var d = new InventoryDecision { Row = i, ActionEligibility = "requires-release-selection-before-search" };
            d.Candidates.Add(new LidarrCandidateEvidence(i + 100, 1, 5, "Bangarang", i == 1 ? "First" : "Second", id, false, 0, true, true, false, "exact recording"));
            d.Candidates.Add(new LidarrCandidateEvidence(i + 200, 1, 6, "Compilation", i == 1 ? "First" : "Second", id, false, 0, true, true, false, "exact recording"));
            decisions.Add(d);
        }
        var groups = BuildAlbumReviewGroups(audit, decisions);
        if (groups.Count != 1 || groups[0].Songs != 2 || groups[0].SuggestedAlbumId != 5 ||
            groups[0].Recommendation != "possible-edition-alias-review" || groups[0].Choices.Count(c => c.Complete) != 2)
            throw new Exception("Album group planning self-test failed: edition-alias recommendation");
        if (AlbumTitleRelationship("Bangarang EP", "Bangarang") != "ep-suffix-variant" ||
            AlbumTitleRelationship("21 Guns E.P.", "21 Guns") != "ep-suffix-variant" ||
            AlbumTitleRelationship("Play Hard (EP)", "Play Hard") != "ep-suffix-variant" ||
            AlbumTitleRelationship("Bangarang EP", "Triple Vinyl Box Set") != "different-title" ||
            AlbumTitleRelationship("EP", "Something Else") != "different-title")
            throw new Exception("EP variants must create explicit release-review choices, never blanket aliases");
        if (AlbumTitleRelationship("Smoke + Mirrors", "Imagine Dragons") != "different-title")
            throw new Exception("Album group planning self-test failed: unrelated albums cannot be equivalent");
        var changedRecording = new List<InventoryDecision>();
        var editionAudit = new EvidenceAudit();
        editionAudit.Songs.Add(new SongAudit { Row = 3, Artist = "Example", Album = "Bangarang EP", Song = "Third", RecordingId = id });
        var conflictDecision = new InventoryDecision { Row = 3, MatchMethod = "edition-alias-album-candidate",
            RecordingIdentityDisputed = true, ActionEligibility = "requires-release-selection-before-search" };
        conflictDecision.Candidates.Add(new LidarrCandidateEvidence(301, 1, 5, "Bangarang", "Third",
            "bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb", false, 0, false, true, false, "title+edition-alias; ID differs"));
        changedRecording.Add(conflictDecision);
        var editionGroups = BuildAlbumReviewGroups(editionAudit, changedRecording);
        if (editionGroups.Count != 1 || editionGroups[0].Songs != 1 || !editionGroups[0].Choices.Single().Complete)
            throw new Exception("Edition/MBID conflict group should be reviewable without being treated as exact recording");
        var duplicateDisputed = new InventoryDecision { Row = 3, MatchMethod = "album-title-alias-candidate",
            RecordingIdentityDisputed = true, ActionEligibility = "requires-release-selection-before-search" };
        duplicateDisputed.Candidates.Add(new LidarrCandidateEvidence(301, 1, 5, "Bangarang", "Third",
            "bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb", false, 0, false, true, false, "candidate A"));
        duplicateDisputed.Candidates.Add(new LidarrCandidateEvidence(302, 1, 5, "Bangarang", "Third",
            "cccccccc-cccc-4ccc-cccc-cccccccccccc", false, 0, false, true, false, "candidate B"));
        var multiVersionGroups = BuildAlbumReviewGroups(editionAudit, new List<InventoryDecision> { duplicateDisputed });
        if (multiVersionGroups.Count != 1 || multiVersionGroups[0].Choices.Count != 1 ||
            !multiVersionGroups[0].Choices[0].Complete)
            throw new Exception("Multiple disputed tracks on one album should allow album-only planning, not master acceptance");
        var albumOnlyEngine = new DesktopServer { latestAudit = editionAudit,
            inventory = new InventoryState { Done = true, Decisions = new List<InventoryDecision> { duplicateDisputed },
                AlbumGroups = multiVersionGroups } };
        albumOnlyEngine.ApproveAlbumGroupSelection(multiVersionGroups[0].Id, 5);
        if (duplicateDisputed.LidarrTrackId != "" || duplicateDisputed.LidarrForeignRecordingId != "" ||
            !duplicateDisputed.RecordingIdentityDisputed)
            throw new Exception("Album-only version review must never choose a competing recording ID");
        var engine = new DesktopServer();
        engine.latestAudit = audit;
        engine.inventory = new InventoryState { Done = true, Decisions = decisions, AlbumGroups = groups };
        engine.ApproveAlbumGroupSelection(groups[0].Id, 5);
        var conflictingEngine = new DesktopServer();
        conflictingEngine.latestAudit = editionAudit;
        conflictingEngine.inventory = new InventoryState { Done = true, Decisions = changedRecording, AlbumGroups = editionGroups };
        conflictingEngine.ApproveAlbumGroupSelection(editionGroups[0].Id, 5);
        if (conflictDecision.LidarrTrackId.Length != 0 || conflictDecision.LidarrAlbumId != "5" ||
            !conflictDecision.RecordingIdentityDisputed || conflictDecision.ActionEligibility != "requires-final-search-approval")
            throw new Exception("Selecting an album must NOT silently accept a different recording MBID");
        if (engine.inventory.GroupApprovals != 1 || engine.inventory.ReleaseChoicesPending != 0 ||
            decisions.Any(d => d.ActionEligibility != "requires-final-search-approval" || d.LidarrAlbumId != "5"))
            throw new Exception("Album group planning self-test failed: selected releases must remain no-write proposals");
        var albumOnlyPlan = new InventoryDecision { LidarrAlbumId = "155", LidarrTrackId = "",
            RecordingIdentityDisputed = true, Category = "To Be Added / Songs (album-only search target)",
            ActionEligibility = "requires-final-search-approval" };
        var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proposal = PlanOneAction(albumOnlyPlan, dedup);
        var duplicateProposal = PlanOneAction(albumOnlyPlan, dedup);
        if (!proposal.AlbumOnly || proposal.Key != "lidarr-album:155" ||
            proposal.Operation != "propose-album-search-only-recording-not-verified-after-separate-approval" ||
            duplicateProposal.Operation != "covered-by-other-proposed-search-for-same-album")
            throw new Exception("Album-only planning must not require a disputed track ID and must deduplicate searches");
        var manual = PlanOneAction(new InventoryDecision { LidarrAlbumId = "155", ActionEligibility = "manual-review-required" }, dedup);
        if (manual.Key.Length != 0 || manual.Operation != "hold-for-metadata-or-manual-review")
            throw new Exception("Manual-review rows may not bypass album-search approval");
        Console.WriteLine("PASS: album cohorts, selection approvals, album-only action planning and search deduplication");
    }
}
