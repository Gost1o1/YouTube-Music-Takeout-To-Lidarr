using System.Net;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // This is an explicit, read-only inventory inspection. It uses only the Lidarr
    // server whose origin the user already connected, and never writes to Lidarr.
    sealed class InventoryState
    {
        public bool Running, Done, Failed;
        public string Phase = "Not started", Error = "";
        public int TotalSongs, Already, ToAdd, Review, TracksRead, FilesRead, AlbumsRead;
        public int ArtistsTotal, ArtistsChecked, RequestsFinished, Retries;
        public int SourceArtists, ArtistAlready, ArtistToAdd, ArtistReview;
        public int GroupedKnownRecording, AlbumDisambiguated, ReleaseChoicesPending, SearchReady, KnownTrackMetadata, ManualConflicts, GroupApprovals;
        public int AlbumAliasesPending, AlbumOnlySearchTargets;
        public List<AlbumReviewGroup> AlbumGroups = new();
        public List<InventoryRequestEvent> Events = new();
        public DateTimeOffset StartedAt;
        public List<InventoryDecision> Decisions = new();
        public InventoryState Copy() => (InventoryState)MemberwiseClone();
    }
    sealed record InventoryRequestEvent(string TimestampUtc, string Endpoint, int ArtistId, int Attempt,
        long Milliseconds, string Outcome, int HttpStatus, string Detail);
    sealed class InventoryDecision
    {
        public int Row;
        public string Category = "Pending Lidarr track/file comparison / Songs";
        public string MatchMethod = "not-checked", Reason = "Lidarr track and file inventory not checked";
        public string LidarrTrackId = "", LidarrTrackFileId = "", LidarrAlbumId = "";
        public string LidarrTitle = "", LidarrAlbum = "", LidarrForeignRecordingId = "";
        public int CandidateCount;
        public bool RecordingIdentityDisputed;
        public string AlbumTargetEvidence = "none";
        public string ReleaseSelection = "not-assessed", ActionEligibility = "manual-review-required", MetadataStatus = "not-checked";
        public List<LidarrCandidateEvidence> Candidates = new();
    }
    sealed record LidarrCandidateEvidence(int TrackId, int ArtistId, int AlbumId, string Album,
        string Title, string RecordingMbid, bool HasFile, int TrackFileId,
        bool RecordingIdAgrees, bool TitleAgrees, bool AlbumAgrees, string Evidence);
    sealed record LidarrTrackRecord(int Id, int ArtistId, int AlbumId, string Title, string ForeignRecordingId, int TrackFileId, bool HasFile);
    sealed record LidarrAlbumRecord(int Id, string Title);
    InventoryState inventory = new();
    CancellationTokenSource? inventoryCancellation;

    static string PropText(JsonElement item, string name)
        => item.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.ToString() : "";
    static int PropInt(JsonElement item, string name) => int.TryParse(PropText(item, name), out var i) ? i : 0;
    static bool PropBool(JsonElement item, string name) => bool.TryParse(PropText(item, name), out var b) && b;

    // Inventory has a dedicated HttpClient to avoid the shared client's 100-second
    // default timeout. An individual slow request must not cancel the entire job.
    // Only explicitly cancelled comparisons are reported as interrupted.
    void InventoryEvent(string path, int artistId, int attempt, long elapsedMs,
        string outcome, int httpStatus = 0, string detail = "")
    {
        var record = new InventoryRequestEvent(DateTimeOffset.UtcNow.ToString("O"),
            path, artistId, attempt, elapsedMs, outcome, httpStatus, detail);
        lock (verificationGate)
        {
            inventory.Events.Add(record);
            if (outcome == "success") inventory.RequestsFinished++;
            else if (outcome == "retry-timeout" || outcome == "retry-http" || outcome == "retry-network") inventory.Retries++;
        }
    }

    static bool RetryableLidarrStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    async Task<JsonDocument> ReadLidarrArrayAsync(HttpClient client, string path, int artistId, CancellationToken ct)
    {
        const int attempts = 3;
        // Long local inventory responses can take substantially longer than 90s.
        // Each retry has its own timeout. Explicit user/application cancellation is separate.
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, lidarrUrl + path);
            request.Headers.TryAddWithoutValidation("X-Api-Key", lidarrKey);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(180));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    bool canRetry = RetryableLidarrStatus(response.StatusCode) && attempt < attempts;
                    InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                        canRetry ? "retry-http" : "error-http", (int)response.StatusCode, response.ReasonPhrase ?? "HTTP error");
                    if (!canRetry)
                        throw new InvalidOperationException($"Lidarr returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for GET {path} (attempt {attempt}/{attempts}). No library changes were made.");
                }
                else
                {
                    if (response.Content.Headers.ContentLength > 80_000_000)
                    {
                        InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                            "error-oversized", (int)response.StatusCode, "Content-Length exceeds 80MB safety limit");
                        throw new InvalidOperationException($"Lidarr response for {path} exceeds the 80MB safety limit.");
                    }
                    await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var buffer = new MemoryStream();
                    var bytes = new byte[32768];
                    int received = 0;
                    while (true)
                    {
                        int n = await stream.ReadAsync(bytes.AsMemory(), timeout.Token);
                        if (n == 0) break;
                        received += n;
                        if (received > 80_000_000)
                        {
                            InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                                "error-oversized", (int)response.StatusCode, "Stream exceeds 80MB safety limit");
                            throw new InvalidOperationException($"Lidarr response for {path} exceeds the 80MB safety limit.");
                        }
                        buffer.Write(bytes, 0, n);
                    }
                    buffer.Position = 0;
                    var data = await JsonDocument.ParseAsync(buffer, cancellationToken: timeout.Token);
                    if (data.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        data.Dispose();
                        InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                            "error-json-shape", (int)response.StatusCode, "Expected top-level JSON array");
                        throw new InvalidOperationException($"Lidarr returned non-array inventory JSON for {path}.");
                    }
                    InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds, "success", (int)response.StatusCode);
                    return data;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                bool canRetry = attempt < attempts;
                InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                    canRetry ? "retry-timeout" : "error-timeout", 0,
                    "Inventory request timed out, not a user cancellation");
                if (!canRetry)
                    throw new TimeoutException($"Lidarr took over 180 seconds to answer GET {path} on each of {attempts} attempts. The comparison was not manually interrupted. Export inventory diagnostics for details.");
            }
            catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
            {
                bool canRetry = attempt < attempts;
                InventoryEvent(path, artistId, attempt, watch.ElapsedMilliseconds,
                    canRetry ? "retry-network" : "error-network", 0, ex.GetType().Name);
                if (!canRetry)
                    throw new InvalidOperationException($"Network error contacting Lidarr for GET {path} after {attempts} attempts: {ex.Message}", ex);
            }
            // Don't retry invalid arguments, unexpected JSON, or oversized responses.
            if (attempt < attempts)
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }
        throw new InvalidOperationException($"Lidarr inventory read failed for {path}.");
    }

    // Both Lidarr track and trackfile endpoints reject requests that have no filter.
    // Include the *local Lidarr database artist ID*, never a MusicBrainz GUID.
    static string LidarrTracksPath(int artistId) => artistId > 0
        ? $"/api/v1/track?artistId={artistId}"
        : throw new ArgumentOutOfRangeException(nameof(artistId));
    static string LidarrTrackFilesPath(int artistId) => artistId > 0
        ? $"/api/v1/trackfile?artistId={artistId}"
        : throw new ArgumentOutOfRangeException(nameof(artistId));
    // Lidarr supports an artistId-filtered album query. The unfiltered GET
    // serializes *every* album (including embedded release metadata), which can
    // overwhelm large libraries even before we reach our first relevant artist.
    static string LidarrAlbumsPath(int artistId) => artistId > 0
        ? $"/api/v1/album?artistId={artistId}"
        : throw new ArgumentOutOfRangeException(nameof(artistId));

    // A track can occur on several Lidarr albums/reissues while identifying the SAME
    // MusicBrainz recording. That is not automatically a song-identity conflict.
    // It DOES NOT authorize selecting an arbitrary album to download. This planner
    // groups equivalent recording IDs, picks a specific track only with unique
    // supporting release evidence, and otherwise asks the user to choose the release.
    static List<InventoryDecision> CompareLidarrTracks(EvidenceAudit audit, List<LidarrArtist> lidarr,
        IReadOnlyList<LidarrTrackRecord> tracks, IReadOnlyList<LidarrAlbumRecord> albums, HashSet<int> fileIds)
    {
        var artists = lidarr.Where(x => ValidMbid(x.foreignArtistId) && x.Id > 0)
            .GroupBy(x => x.foreignArtistId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var artistEvidence = audit.Artists.ToDictionary(x => x.SourceName, StringComparer.OrdinalIgnoreCase);
        var albumNames = albums.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().Title);
        var byArtist = tracks.GroupBy(x => x.ArtistId).ToDictionary(x => x.Key, x => x.ToArray());
        var decisions = new List<InventoryDecision>(audit.Songs.Count);
        foreach (var song in audit.Songs)
        {
            var d = new InventoryDecision { Row = song.Row };
            decisions.Add(d);
            if (!artistEvidence.TryGetValue(song.Artist, out var artist) || artist.Status != "high" || !ValidMbid(artist.ArtistId))
            {
                d.Category = "Manual Review / Songs";
                d.Reason = "Artist MBID not independently validated";
                continue;
            }
            if (song.SongIdentityStatus != "artist-and-song-supported" ||
                (song.Status != "high" && song.Status != "artist-supported/version-review"))
            {
                d.Category = "Manual Review / Songs";
                d.Reason = "Artist/song identity not sufficiently supported";
                continue;
            }
            if (!artists.TryGetValue(artist.ArtistId, out var lidarrArtist))
            {
                d.Category = "To Be Added / Songs (proposal only)";
                d.MatchMethod = "validated-artist-not-yet-in-lidarr";
                d.MetadataStatus = "artist-absent-by-mbid";
                d.ActionEligibility = "requires-artist-addition-and-approval";
                d.Reason = "Validated artist MBID absent in Lidarr; future addition requires approval";
                continue;
            }
            var candidateTracks = lidarrArtist.SelectMany(x => byArtist.GetValueOrDefault(x.Id) ?? Array.Empty<LidarrTrackRecord>()).ToArray();
            var exactRecording = ValidMbid(song.RecordingId)
                ? candidateTracks.Where(t => t.ForeignRecordingId.Equals(song.RecordingId, StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<LidarrTrackRecord>();
            var titleMatches = candidateTracks.Where(t => Normalize(t.Title) == Normalize(song.Song)).ToArray();
            var titleAlbumMatches = titleMatches.Where(t => song.Album.Length > 0 &&
                albumNames.TryGetValue(t.AlbumId, out var title) && SameAlbumExact(title, song.Album)).ToArray();

            // The candidate export includes all exact-MBID, exact-title and album-linked
            // records, even when there is no uniquely selected track for a future action.
            var diagnosticCandidates = exactRecording.Concat(titleMatches).DistinctBy(t => t.Id).OrderBy(t => t.Id).ToArray();
            foreach (var t in diagnosticCandidates)
            {
                bool idSame = ValidMbid(song.RecordingId) && t.ForeignRecordingId.Equals(song.RecordingId, StringComparison.OrdinalIgnoreCase);
                bool titleSame = Normalize(t.Title) == Normalize(song.Song);
                bool albumSame = song.Album.Length > 0 && albumNames.TryGetValue(t.AlbumId, out var name) && SameAlbumExact(name, song.Album);
                string albumRelationship = AlbumTitleRelationship(song.Album, albumNames.GetValueOrDefault(t.AlbumId) ?? "");
                string evidence = idSame ? (albumSame ? "exact-recording-id+source-album" : "exact-recording-id") :
                    titleSame && albumSame ? "title+album; verify-recording-id" :
                    titleSame && IsReviewableAlbumAlias(albumRelationship) ? "title+album-alias:" + albumRelationship :
                    "title-only; not-safe-for-automatic-selection";
                if (ValidMbid(song.RecordingId) && ValidMbid(t.ForeignRecordingId) && !idSame)
                    evidence += "; recording-mbid-disagrees; exact-master-unverified";
                d.Candidates.Add(new LidarrCandidateEvidence(t.Id, t.ArtistId, t.AlbumId,
                    albumNames.GetValueOrDefault(t.AlbumId) ?? "", t.Title, t.ForeignRecordingId,
                    t.HasFile, t.TrackFileId, idSame, titleSame, albumSame, evidence));
            }
            d.CandidateCount = d.Candidates.Count;
            d.MetadataStatus = diagnosticCandidates.Length > 0 ? "lidarr-track-metadata-present" : "lidarr-artist-present-no-matching-track";

            // Exact MBID is sufficient to establish recording identity, not which
            // album/release should be searched. Prefer a UNIQUE matching source album.
            if (exactRecording.Length > 0)
            {
                d.MatchMethod = "musicbrainz-recording-id";
                var matchingAlbum = exactRecording.Where(t => song.Album.Length > 0 &&
                    albumNames.TryGetValue(t.AlbumId, out var a) && SameAlbumExact(a, song.Album)).ToArray();
                if (exactRecording.Length == 1 || matchingAlbum.Length == 1)
                {
                    var choice = matchingAlbum.Length == 1 ? matchingAlbum[0] : exactRecording[0];
                    d.ReleaseSelection = matchingAlbum.Length == 1 && exactRecording.Length > 1 ?
                        "unique-source-album-and-recording-id" : "single-matching-recording-track";
                    SelectTrackForPlan(d, choice, albumNames, fileIds);
                }
                else
                {
                    d.ReleaseSelection = "same-recording-id-across-multiple-releases";
                    d.ActionEligibility = "requires-release-selection-before-search";
                    d.MatchMethod = "grouped-same-musicbrainz-recording";
                    d.CandidateCount = exactRecording.Length;
                    // If at least one matching recording already has a file, the user
                    // has this recording; do not require picking a release just to
                    // recognize an existing file. File inventory must corroborate it.
                    var available = exactRecording.Where(t => t.HasFile && t.TrackFileId > 0 && fileIds.Contains(t.TrackFileId)).ToArray();
                    if (available.Length > 0)
                    {
                        d.Category = "Already in Library / Songs (Lidarr file record)";
                        d.Reason = "Same MusicBrainz recording appears across releases and an existing trackfile is reported";
                        d.ActionEligibility = "no-action-required";
                        if (available.Length == 1) SetMatchedTrackFields(d, available[0], albumNames);
                    }
                    else if (exactRecording.All(t => !t.HasFile && t.TrackFileId <= 0))
                    {
                        d.Category = "To Be Added / Songs (known recording; release choice pending)";
                        d.Reason = "MusicBrainz recording already exists in Lidarr on multiple releases; select target album before any search";
                    }
                    else
                    {
                        d.Category = "Manual Review / Songs";
                        d.ActionEligibility = "manual-review-required";
                        d.Reason = "Matching recording group has inconsistent trackfile indicators";
                    }
                }
                continue;
            }

            // Never override different valid recording MBIDs with title/album alone.
            bool conflict = titleAlbumMatches.Any(t => ValidMbid(song.RecordingId) &&
                ValidMbid(t.ForeignRecordingId) && !song.RecordingId.Equals(t.ForeignRecordingId, StringComparison.OrdinalIgnoreCase));
            if (conflict)
            {
                // Album-search eligibility and exact-recording identity are distinct.
                // The former can be supported by validated artist + song identity,
                // exact source album title and a Lidarr album, even when the source
                // recording MBID is a different mastering/version. NEVER change the
                // recording MBID or silently choose a Lidarr track.
                d.RecordingIdentityDisputed = true;
                d.CandidateCount = titleAlbumMatches.Length;
                var albumChoices = titleAlbumMatches.Select(t => t.AlbumId).Distinct().ToArray();
                if (albumChoices.Length == 1 &&
                    titleAlbumMatches.All(t => !t.HasFile && t.TrackFileId <= 0))
                {
                    d.Category = "To Be Added / Songs (album search target; recording version unverified)";
                    d.MatchMethod = "source-album-title-supported-recording-id-disputed";
                    d.ReleaseSelection = "single-exact-source-album; recording-id-disputed";
                    d.AlbumTargetEvidence = "validated-artist+song; exact-title+album; single-Lidarr-album; MBID-conflict";
                    d.ActionEligibility = "requires-final-search-approval";
                    d.LidarrAlbumId = albumChoices[0].ToString();
                    d.LidarrAlbum = albumNames.GetValueOrDefault(albumChoices[0]) ?? "";
                    d.Reason = "A single Lidarr album matches the validated artist, song title and source album. Recording MBIDs DIFFER; only that album is proposed for a future explicitly approved search; no exact track selected.";
                }
                else if (albumChoices.Length > 1 &&
                    titleAlbumMatches.All(t => !t.HasFile && t.TrackFileId <= 0))
                {
                    d.Category = "To Be Added / Songs (album choices pending; recording ID disputed)";
                    d.MatchMethod = "multiple-source-albums-recording-id-disputed";
                    d.ReleaseSelection = "multiple-exact-source-albums; recording-id-disputed";
                    d.AlbumTargetEvidence = "validated-artist+song; title+album; multiple-Lidarr-album-ids; MBID-conflict";
                    d.ActionEligibility = "requires-release-selection-before-search";
                    d.Reason = "Exact source album occurs on multiple Lidarr albums with different recording MBIDs. Choose an album explicitly; never infer the recording master.";
                }
                else
                {
                    d.Category = "Manual Review / Songs";
                    d.MatchMethod = "recording-id-conflict";
                    d.Reason = "Conflicting MusicBrainz recording IDs and ambiguous file/album evidence; manual review retained";
                }
                continue;
            }
            var compatible = titleAlbumMatches.Where(t => !ValidMbid(song.RecordingId) ||
                !ValidMbid(t.ForeignRecordingId) || song.RecordingId.Equals(t.ForeignRecordingId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (compatible.Length == 1)
            {
                d.MatchMethod = "unique-title-and-album";
                d.ReleaseSelection = "unique-source-album";
                SelectTrackForPlan(d, compatible[0], albumNames, fileIds);
                continue;
            }
            if (compatible.Length > 1 && compatible.All(t => ValidMbid(t.ForeignRecordingId)) &&
                compatible.Select(t => t.ForeignRecordingId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                // No arbitrary release selection. One recording shared by several
                // Lidarr tracks is grouped and sent to a held planning state.
                d.Category = "To Be Added / Songs (known recording; release choice pending)";
                d.MatchMethod = "grouped-title-album-recording-id";
                d.ReleaseSelection = "same-recording-id-across-multiple-tracks";
                d.ActionEligibility = "requires-release-selection-before-search";
                d.Reason = "Source artist/title/album and a single recording MBID agree, but multiple Lidarr tracks remain; choose release first";
                d.CandidateCount = compatible.Length;
                continue;
            }
            if (titleMatches.Length == 0)
            {
                d.Category = "To Be Added / Songs (proposal only)";
                d.MatchMethod = "no-lidarr-track";
                d.ActionEligibility = "requires-metadata-resolution-and-approval";
                d.Reason = "Validated artist and song, but no matching Lidarr track metadata; no search started";
            }
            else
            {
                // Picard/beets-inspired *album family* test: a difference caused
                // solely by an explicit Deluxe/EP/Expanded suffix is NOT exact
                // album equality. It can narrow the human release choice, but
                // never automatically identifies a different recording MBID.
                var editionMatches = titleMatches.Where(t => song.Album.Length >= 4 &&
                    albumNames.TryGetValue(t.AlbumId, out var albumTitle) &&
                    IsReviewableAlbumAlias(AlbumTitleRelationship(song.Album, albumTitle))).ToArray();
                var editionAlbumIds = editionMatches.Select(t => t.AlbumId).Distinct().ToArray();
                if (editionAlbumIds.Length > 0 &&
                    editionMatches.All(t => !t.HasFile && t.TrackFileId <= 0))
                {
                    d.Category = "To Be Added / Songs (album title/edition review; recording version unverified)";
                    d.MatchMethod = "album-title-alias-candidate";
                    d.RecordingIdentityDisputed = ValidMbid(song.RecordingId) && editionMatches.Any(t =>
                        ValidMbid(t.ForeignRecordingId) && !song.RecordingId.Equals(t.ForeignRecordingId, StringComparison.OrdinalIgnoreCase));
                    d.ReleaseSelection = "related-album-title; explicit-album-approval-required";
                    d.AlbumTargetEvidence = "validated-artist+song; exact-title; typed-volume-edition-or-glyph-album-alias; review-required";
                    d.ActionEligibility = "requires-release-selection-before-search";
                    d.Reason = "Related album spelling or edition detected (volume notation, Unicode glyph, deluxe/expanded/etc). Explicitly choose a compatible album; recording ID/edition remains unverified.";
                }
                else
                {
                    d.Category = "Manual Review / Songs";
                    d.MatchMethod = compatible.Length > 1 ? "different-or-unidentified-recording-versions" : "title-only-insufficient";
                    d.Reason = compatible.Length > 1 ? "Multiple possible recording versions without one supported MBID" :
                        "Title appears in Lidarr but source album/recording relationship is not established";
                }
            }
        }
        return decisions;
    }

    static void SetMatchedTrackFields(InventoryDecision d, LidarrTrackRecord match, IReadOnlyDictionary<int, string> albumNames)
    {
        d.LidarrTrackId = match.Id.ToString();
        d.LidarrTrackFileId = match.TrackFileId > 0 ? match.TrackFileId.ToString() : "";
        d.LidarrAlbumId = match.AlbumId.ToString();
        d.LidarrTitle = match.Title;
        d.LidarrAlbum = albumNames.GetValueOrDefault(match.AlbumId) ?? "";
        d.LidarrForeignRecordingId = match.ForeignRecordingId;
    }

    static void SelectTrackForPlan(InventoryDecision d, LidarrTrackRecord track,
        IReadOnlyDictionary<int, string> albumNames, HashSet<int> fileIds)
    {
        SetMatchedTrackFields(d, track, albumNames);
        if (track.HasFile && track.TrackFileId > 0 && fileIds.Contains(track.TrackFileId))
        {
            d.Category = "Already in Library / Songs (Lidarr file record)";
            d.ActionEligibility = "no-action-required";
            d.Reason = "Unique supported track has a file record registered in Lidarr; disk not independently checked";
        }
        else if (!track.HasFile && track.TrackFileId <= 0)
        {
            d.Category = "To Be Added / Songs (existing Lidarr track; missing file)";
            d.ActionEligibility = "eligible-after-user-approval";
            d.Reason = "Uniquely supported Lidarr track has no audio file; future album search requires approval";
        }
        else
        {
            d.Category = "Manual Review / Songs";
            d.ActionEligibility = "manual-review-required";
            d.Reason = "Lidarr trackfile indicators conflict or the trackfile record was not returned";
        }
    }

    async Task RunLidarrComparisonAsync(EvidenceAudit audit, List<LidarrArtist> artists, CancellationToken ct)
    {
        try
        {
            using var inventoryHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            // Do not request the entire album catalog. An unfiltered /api/v1/album
            // response can exceed tens of megabytes on a mature Lidarr library.
            // First identify the local artist IDs supported by this Takeout audit;
            // then request each relevant artist's albums, tracks, and trackfiles.
            lock (verificationGate) inventory.Phase = "Identifying relevant Lidarr artists (read-only)...";

            // Don't load other artists' libraries when only a subset can match this Takeout.
            var supportedSourceNames = audit.Songs
                .Where(s => s.SongIdentityStatus == "artist-and-song-supported" &&
                    (s.Status == "high" || s.Status == "artist-supported/version-review"))
                .Select(s => s.Artist).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relevantMbids = audit.Artists
                .Where(a => a.Status == "high" && ValidMbid(a.ArtistId) && supportedSourceNames.Contains(a.SourceName))
                .Select(a => a.ArtistId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var artistIds = artists
                .Where(a => a.Id > 0 && relevantMbids.Contains(a.foreignArtistId))
                .Select(a => a.Id).Distinct().OrderBy(id => id).ToArray();

            if (artistIds.Length == 0 && artists.Any(a => relevantMbids.Contains(a.foreignArtistId)))
                throw new InvalidOperationException("Lidarr supplied matching artists but no usable numeric local artist IDs; song inventory remains NOT CHECKED.");

            lock (verificationGate) inventory.ArtistsTotal = artistIds.Length;
            var tracks = new List<LidarrTrackRecord>();
            var albums = new List<LidarrAlbumRecord>();
            var fileIds = new HashSet<int>();
            // Limit concurrent local requests so Lidarr remains responsive. An entire
            // two-artist batch must succeed before its results can be used.
            // Two artists at a time; track, trackfile and album calls are sequential per artist.
            // Avoid flooding a local Lidarr instance with simultaneous large responses.
            const int requestBatchSize = 2;
            for (var offset = 0; offset < artistIds.Length; offset += requestBatchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = artistIds.Skip(offset).Take(requestBatchSize).ToArray();
                var results = await Task.WhenAll(batch.Select(async id =>
                {
                    using var trackDoc = await ReadLidarrArrayAsync(inventoryHttp, LidarrTracksPath(id), id, ct);
                    var artistTracks = trackDoc.RootElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.Object)
                        .Select(x => new LidarrTrackRecord(PropInt(x, "id"), PropInt(x, "artistId"),
                            PropInt(x, "albumId"), PropText(x, "title"),
                            PropText(x, "foreignRecordingId"), PropInt(x, "trackFileId"),
                            PropBool(x, "hasFile")))
                        .Where(t => t.ArtistId == id && t.Id > 0).ToArray();
                    using var fileDoc = await ReadLidarrArrayAsync(inventoryHttp, LidarrTrackFilesPath(id), id, ct);
                    var artistFileIds = fileDoc.RootElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.Object)
                        .Select(x => PropInt(x, "id")).Where(fileId => fileId > 0).ToArray();
                    // Albums are also retrieved per numeric local Lidarr artist ID.
                    // This avoids the formerly unbounded GET /api/v1/album.
                    using var albumDoc = await ReadLidarrArrayAsync(inventoryHttp, LidarrAlbumsPath(id), id, ct);
                    var artistAlbums = albumDoc.RootElement.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.Object)
                        .Select(x => new LidarrAlbumRecord(PropInt(x, "id"), PropText(x, "title")))
                        .Where(x => x.Id > 0).ToArray();
                    return (Tracks: artistTracks, FileIds: artistFileIds, Albums: artistAlbums);
                }));
                foreach (var result in results)
                {
                    tracks.AddRange(result.Tracks);
                    fileIds.UnionWith(result.FileIds);
                    albums.AddRange(result.Albums);
                }
                var processed = Math.Min(offset + batch.Length, artistIds.Length);
                lock (verificationGate)
                {
                    inventory.ArtistsChecked = processed;
                    inventory.Phase = $"Reading Lidarr track/file inventory: {processed} of {artistIds.Length} artists checked (read-only)...";
                }
            }
            ct.ThrowIfCancellationRequested();
            var decisions = CompareLidarrTracks(audit, artists, tracks, albums, fileIds);
            var albumGroups = BuildAlbumReviewGroups(audit, decisions);
            lock (verificationGate)
            {
                if (ct.IsCancellationRequested) return;
                inventory.Decisions = decisions;
                stagedAlbumSearches.Clear(); // All prior staging applies to a different inventory snapshot.
                inventory.AlbumGroups = albumGroups;
                inventory.TracksRead = tracks.Count;
                inventory.AlbumsRead = albums.Count;
                inventory.FilesRead = fileIds.Count;
                inventory.SourceArtists = audit.Artists.Count;
                var lidarrMbids = artists.Where(a => ValidMbid(a.foreignArtistId))
                    .Select(a => a.foreignArtistId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                inventory.ArtistAlready = audit.Artists.Count(a => a.Status == "high" && ValidMbid(a.ArtistId) && lidarrMbids.Contains(a.ArtistId));
                inventory.ArtistToAdd = audit.Artists.Count(a => a.Status == "high" && ValidMbid(a.ArtistId) && !lidarrMbids.Contains(a.ArtistId));
                inventory.ArtistReview = audit.Artists.Count - inventory.ArtistAlready - inventory.ArtistToAdd;
                inventory.GroupedKnownRecording = decisions.Count(d => d.ReleaseSelection is "same-recording-id-across-multiple-releases" or "same-recording-id-across-multiple-tracks");
                inventory.AlbumDisambiguated = decisions.Count(d => d.ReleaseSelection == "unique-source-album-and-recording-id");
                inventory.ReleaseChoicesPending = decisions.Count(d => d.ActionEligibility == "requires-release-selection-before-search");
                inventory.SearchReady = decisions.Count(d => d.ActionEligibility is "eligible-after-user-approval" or "requires-final-search-approval");
                inventory.AlbumAliasesPending = decisions.Count(d => d.MatchMethod == "album-title-alias-candidate");
                inventory.AlbumOnlySearchTargets = decisions.Count(d => d.ActionEligibility == "requires-final-search-approval" &&
                    int.TryParse(d.LidarrAlbumId, out var albumId) && albumId > 0 && d.LidarrTrackId.Length == 0);
                inventory.KnownTrackMetadata = decisions.Count(d => d.MetadataStatus == "lidarr-track-metadata-present");
                inventory.ManualConflicts = decisions.Count(d => d.RecordingIdentityDisputed);
                inventory.TotalSongs = decisions.Count;
                inventory.Already = decisions.Count(x => x.Category.StartsWith("Already in Library", StringComparison.Ordinal));
                inventory.ToAdd = decisions.Count(x => x.Category.StartsWith("To Be Added", StringComparison.Ordinal));
                inventory.Review = decisions.Count(x => x.Category.StartsWith("Manual Review", StringComparison.Ordinal));
                inventory.Done = true; inventory.Running = false;
                inventory.Phase = "Lidarr comparison finished; export the readiness CSV for individual decisions.";
            }
        }
        catch (Exception ex)
        {
            lock (verificationGate)
            {
                if (ct.IsCancellationRequested) return;
                inventory.Running = false; inventory.Failed = true;
                inventory.Error = ex is OperationCanceledException
                    ? "Comparison was canceled by an application shutdown or data reset."
                    : ex.Message;
                Log("Lidarr inventory failed: " + ex);
                inventory.Phase = "Inventory comparison failed; no partial results were committed.";
            }
        }
    }

    // A complete, row-level candidate trace for future tuning and manual release
    // selection UI. It does not leak API keys, local file paths or Takeout archives.
    async Task SendLidarrCandidatesCsv(HttpListenerContext ctx)
    {
        InventoryDecision[] decisions;
        EvidenceAudit audit;
        lock (verificationGate)
        {
            if (!inventory.Done || latestAudit == null)
                throw new InvalidOperationException("Complete Lidarr comparison before exporting track candidates.");
            decisions = inventory.Decisions.ToArray();
            audit = latestAudit;
        }
        var songsByRow = audit.Songs.ToDictionary(s => s.Row);
        var sb = new StringBuilder();
        sb.Append(CsvLine("YouTube source row", "Artist", "Song", "Source album", "Source recording MBID",
            "Migration category", "Action eligibility", "Release selection status", "Comparison method", "Decision reason",
            "Candidate count", "Lidarr track ID", "Lidarr album ID", "Lidarr album title", "Lidarr song title",
            "Lidarr recording MBID", "Recording MBID agrees", "Title agrees", "Source album agrees", "Trackfile reported",
            "Candidate evidence / conflict", "Selected track ID", "Album-target evidence", "Recording MBID disputed", "Album title relationship"));
        foreach (var d in decisions.OrderBy(x => x.Row))
        {
            if (!songsByRow.TryGetValue(d.Row, out var song)) continue;
            foreach (var c in d.Candidates)
                sb.Append(CsvLine(d.Row, song.Artist, song.Song, song.Album, song.RecordingId,
                    d.Category, d.ActionEligibility, d.ReleaseSelection, d.MatchMethod, d.Reason,
                    d.CandidateCount, c.TrackId, c.AlbumId, c.Album, c.Title, c.RecordingMbid,
                    c.RecordingIdAgrees, c.TitleAgrees, c.AlbumAgrees, c.HasFile && c.TrackFileId > 0,
                    c.Evidence, d.LidarrTrackId, d.AlbumTargetEvidence, d.RecordingIdentityDisputed,
                    AlbumTitleRelationship(song.Album, c.Album)));
            if (d.Candidates.Count == 0)
                sb.Append(CsvLine(d.Row, song.Artist, song.Song, song.Album, song.RecordingId,
                    d.Category, d.ActionEligibility, d.ReleaseSelection, d.MatchMethod, d.Reason,
                    d.CandidateCount, "", "", "", "", "", "", "", "", "", "no-candidates", d.LidarrTrackId, d.AlbumTargetEvidence, d.RecordingIdentityDisputed, "no-candidate"));
        }
        var data = new UTF8Encoding(true).GetBytes(sb.ToString());
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=lidarr-release-candidates.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = data.Length;
        await ctx.Response.OutputStream.WriteAsync(data);
        ctx.Response.Close();
    }

    async Task SendInventoryDiagnosticsCsv(HttpListenerContext ctx)
    {
        InventoryRequestEvent[] events;
        InventoryState snapshot;
        lock (verificationGate)
        {
            snapshot = inventory.Copy();
            events = inventory.Events.ToArray();
        }
        var sb = new StringBuilder();
        sb.Append(CsvLine("Timestamp UTC", "Endpoint (no API key)", "Local Lidarr artist ID",
            "Attempt", "Elapsed milliseconds", "Outcome", "HTTP status", "Details"));
        foreach (var e in events)
            sb.Append(CsvLine(e.TimestampUtc, e.Endpoint, e.ArtistId, e.Attempt,
                e.Milliseconds, e.Outcome, e.HttpStatus, e.Detail));
        sb.Append(CsvLine("SUMMARY", "", "", "", "", snapshot.Done ? "done" : snapshot.Failed ? "failed" : snapshot.Running ? "running" : "not-started", "", snapshot.Error));
        var data = new UTF8Encoding(true).GetBytes(sb.ToString());
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=lidarr-inventory-diagnostics.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = data.Length;
        await ctx.Response.OutputStream.WriteAsync(data);
        ctx.Response.Close();
    }

    static void LidarrInventorySelfTest()
    {
        if (!RetryableLidarrStatus(HttpStatusCode.ServiceUnavailable) ||
            RetryableLidarrStatus(HttpStatusCode.BadRequest))
            throw new Exception("Transient HTTP retry policy failed");
        if (LidarrTracksPath(31) != "/api/v1/track?artistId=31" ||
            LidarrTrackFilesPath(31) != "/api/v1/trackfile?artistId=31" ||
            LidarrAlbumsPath(31) != "/api/v1/album?artistId=31")
            throw new Exception("Lidarr API endpoint self-test: album, track and file requests must be scoped by artistId");
        try { _ = LidarrTracksPath(0); throw new Exception("Expected invalid artist ID to be rejected"); }
        catch (ArgumentOutOfRangeException) { }
        try { _ = LidarrAlbumsPath(0); throw new Exception("Expected invalid album artist ID to be rejected"); }
        catch (ArgumentOutOfRangeException) { }

        const string artistMbid = "056e4f3e-d505-4dad-8ec1-d04f521cbb56";
        const string recordMbid = "f474cd08-46b2-4a1f-91e5-26a718a16db9";
        var audit = new EvidenceAudit();
        audit.Artists.Add(new ArtistAudit { SourceName = "Example", ArtistId = artistMbid, Status = "high" });
        audit.Songs.Add(new SongAudit { Row = 1, Artist = "Example", Song = "Song", Album = "Album",
            RecordingId = recordMbid, SongIdentityStatus = "artist-and-song-supported", Status = "artist-supported/version-review" });
        var artists = new List<LidarrArtist> { new("Example", artistMbid, 31) };
        var tracks = new[] { new LidarrTrackRecord(54, 31, 15, "Song", recordMbid, 91, true) };
        var albums = new[] { new LidarrAlbumRecord(15, "Album") };
        var have = CompareLidarrTracks(audit, artists, tracks, albums, new HashSet<int> { 91 })[0];
        if (!have.Category.StartsWith("Already in Library") || have.LidarrTrackFileId != "91")
            throw new Exception("Lidarr inventory test: expected file-backed track");
        var missing = CompareLidarrTracks(audit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", recordMbid, 0, false) }, albums, new HashSet<int>())[0];
        if (!missing.Category.StartsWith("To Be Added")) throw new Exception("Lidarr inventory test: missing file");
        var conflict = CompareLidarrTracks(audit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", Guid.NewGuid().ToString(), 91, true) }, albums, new HashSet<int> { 91 })[0];
        if (!conflict.Category.StartsWith("Manual Review")) throw new Exception("Lidarr inventory test: conflicting MBID with existing file cannot be falsely treated as downloaded or missing");
        var conflictNoFile = CompareLidarrTracks(audit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", Guid.NewGuid().ToString(), 0, false) }, albums, new HashSet<int>())[0];
        if (!conflictNoFile.Category.StartsWith("To Be Added") || !conflictNoFile.RecordingIdentityDisputed ||
            conflictNoFile.LidarrAlbumId != "15" || conflictNoFile.LidarrTrackId.Length != 0 ||
            conflictNoFile.ActionEligibility != "requires-final-search-approval")
            throw new Exception("Album-target reconciliation: exact artist/title/album permits an album-search proposal, NOT recording-ID acceptance");
        var editionAudit = new EvidenceAudit();
        editionAudit.Artists.Add(new ArtistAudit { SourceName = "Example", ArtistId = artistMbid, Status = "high" });
        editionAudit.Songs.Add(new SongAudit { Row = 2, Artist = "Example", Song = "Song", Album = "Album (Deluxe Edition)",
            RecordingId = recordMbid, SongIdentityStatus = "artist-and-song-supported", Status = "artist-supported/version-review" });
        var edition = CompareLidarrTracks(editionAudit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", Guid.NewGuid().ToString(), 0, false) }, albums, new HashSet<int>())[0];
        if (edition.ActionEligibility != "requires-release-selection-before-search" ||
            !edition.RecordingIdentityDisputed || edition.LidarrTrackId.Length != 0)
            throw new Exception("Edition title can suggest a group, but may not automatically select a different recording");
        // Regression: an explicit source EP suffix may point to a Lidarr EP
        // with the suffix omitted. It must be proposed for human release choice,
        // never silently accepted as the same recording master.
        var epAudit = new EvidenceAudit();
        epAudit.Artists.Add(new ArtistAudit { SourceName = "Example", ArtistId = artistMbid, Status = "high" });
        epAudit.Songs.Add(new SongAudit { Row = 3, Artist = "Example", Song = "Summit", Album = "Bangarang EP",
            RecordingId = recordMbid, SongIdentityStatus = "artist-and-song-supported", Status = "artist-supported/version-review" });
        var epDecision = CompareLidarrTracks(epAudit, artists,
            new[] { new LidarrTrackRecord(88, 31, 28, "Summit", Guid.NewGuid().ToString(), 0, false) },
            new[] { new LidarrAlbumRecord(28, "Bangarang") }, new HashSet<int>())[0];
        if (!epDecision.Category.StartsWith("To Be Added") || epDecision.MatchMethod != "album-title-alias-candidate" ||
            epDecision.ActionEligibility != "requires-release-selection-before-search" || epDecision.LidarrTrackId.Length != 0 ||
            !epDecision.RecordingIdentityDisputed || BuildAlbumReviewGroups(epAudit, new List<InventoryDecision> { epDecision }).Count != 1)
            throw new Exception("EP variant must be grouped for deliberate release review, without replacing recording IDs");
        var unknown = CompareLidarrTracks(audit, artists, tracks, albums, new HashSet<int>())[0];
        if (!unknown.Category.StartsWith("Manual Review")) throw new Exception("Lidarr inventory test: inconsistent file listing");
        // Regression based on the user's 666 multiply-listed tracks. A single
        // recording MBID can appear on two or more albums/editions in Lidarr.
        // An exact source album singles out the intended release when unique.
        var duplicateTracks = new[] {
            new LidarrTrackRecord(54, 31, 15, "Song", recordMbid, 0, false),
            new LidarrTrackRecord(55, 31, 16, "Song", recordMbid, 0, false)
        };
        var duplicateAlbums = new[] { new LidarrAlbumRecord(15, "Album"), new LidarrAlbumRecord(16, "Greatest Hits") };
        var selected = CompareLidarrTracks(audit, artists, duplicateTracks, duplicateAlbums, new HashSet<int>())[0];
        if (!selected.Category.StartsWith("To Be Added") || selected.LidarrTrackId != "54" ||
            selected.ReleaseSelection != "unique-source-album-and-recording-id" || selected.Candidates.Count != 2 ||
            selected.ActionEligibility != "eligible-after-user-approval")
            throw new Exception("Lidarr release planner test: exact source album should disambiguate duplicate recording IDs");
        var noAlbumAudit = new EvidenceAudit();
        noAlbumAudit.Artists.Add(new ArtistAudit { SourceName = "Example", ArtistId = artistMbid, Status = "high" });
        noAlbumAudit.Songs.Add(new SongAudit { Row = 1, Artist = "Example", Song = "Song", Album = "Unknown Album",
            RecordingId = recordMbid, SongIdentityStatus = "artist-and-song-supported", Status = "artist-supported/version-review" });
        var held = CompareLidarrTracks(noAlbumAudit, artists, duplicateTracks, duplicateAlbums, new HashSet<int>())[0];
        if (!held.Category.StartsWith("To Be Added") || held.LidarrTrackId.Length != 0 ||
            held.ReleaseSelection != "same-recording-id-across-multiple-releases" ||
            held.ActionEligibility != "requires-release-selection-before-search")
            throw new Exception("Lidarr release planner test: do not silently choose among multiple releases");
        var differentRecording = CompareLidarrTracks(noAlbumAudit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", Guid.NewGuid().ToString(), 0, false) },
            duplicateAlbums, new HashSet<int>())[0];
        if (!differentRecording.Category.StartsWith("Manual Review") || differentRecording.LidarrTrackId.Length != 0)
            throw new Exception("Lidarr release planner test: conflicting recording IDs must remain manual review");
        // Distinct title spellings must create album-review candidates, never
        // exact recording acceptance, even with a source recording MBID.
        if (AlbumTitleRelationship("The Best of Example, Vol. 1", "The Best of Example, Volume 1") != "volume-notation-variant" ||
            AlbumTitleRelationship("<|º_º|>", "<|°_°|>") != "unicode-glyph-variant" ||
            SameAlbumExact("<|º_º|>", "<|°_°|>") ||
            AlbumTitleRelationship("Hunting High and Low", "Greatest Hits") != "different-title")
            throw new Exception("Album aliases must preserve identity, differentiate symbolic names, and reject unrelated albums");
        var volumeAudit = new EvidenceAudit();
        volumeAudit.Artists.Add(new ArtistAudit { SourceName = "Example", ArtistId = artistMbid, Status = "high" });
        volumeAudit.Songs.Add(new SongAudit { Row = 1, Artist = "Example", Song = "Song",
            Album = "The Best of Example, Vol. 1", RecordingId = Guid.NewGuid().ToString(),
            SongIdentityStatus = "artist-and-song-supported", Status = "artist-supported/version-review" });
        var volumeCase = CompareLidarrTracks(volumeAudit, artists,
            new[] { new LidarrTrackRecord(54, 31, 15, "Song", Guid.NewGuid().ToString(), 0, false) },
            new[] { new LidarrAlbumRecord(15, "The Best of Example, Volume 1") }, new HashSet<int>())[0];
        if (volumeCase.ActionEligibility != "requires-release-selection-before-search" ||
            !volumeCase.RecordingIdentityDisputed || volumeCase.LidarrTrackId.Length != 0 ||
            BuildAlbumReviewGroups(volumeAudit, new List<InventoryDecision>{ volumeCase }).Count != 1)
            throw new Exception("Volume-title alias must require album approval without accepting a disputed recording ID");
        Console.WriteLine("PASS: Lidarr inventory, album aliases, safe release grouping and recording conflicts");
    }
}
