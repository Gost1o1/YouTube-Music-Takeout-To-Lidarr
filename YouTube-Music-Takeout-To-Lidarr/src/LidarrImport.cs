using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

sealed partial class DesktopServer
{
    // Lidarr writes are opt-in and are never called from matching, preview,
    // review decisions or automatic metadata fallback.
    sealed record ImportEvent(string Kind, string Name, string Outcome, string Detail);
    sealed class LidarrWriteState
    {
        public bool Running, Done;
        public bool Rechecking;
        public bool RecoveredFromCheckpoint;
        public string EndpointFingerprint = "", InFlightAction = "";
        public DateTimeOffset StartedUtc;
        public string Mode = "", Phase = "Not started";
        public int Total, Completed, AddedArtists, ExistingArtists, MonitoredAlbums,
            AlreadyMonitoredAlbums, SearchesQueued, Failed;
        public string[] ExpectedArtistIds = Array.Empty<string>();
        public int[] ExpectedAlbumIds = Array.Empty<int>();
        public HashSet<string> ConfirmedArtistIds = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> ConfirmedAlbumIds = new();
        public int CatalogVerified => ConfirmedArtistIds.Count + ConfirmedAlbumIds.Count;
        public bool CatalogComplete => Done && !Running && !Rechecking &&
            string.IsNullOrEmpty(InFlightAction) && Completed == Total &&
            Failed == 0 && Total > 0 && CatalogVerified == Total;
        public List<ImportEvent> Events = new();
    }
    // No API keys, OAuth tokens, or raw Takeout file contents are written to
    // the import checkpoint. Artist/album names and IDs are kept for audit.
    // It is an audit trail, not a replay
    // queue: a POST may succeed immediately before Windows terminates the EXE.
    sealed class ImportCheckpoint
    {
        public ImportCheckpoint() { }
        public int Schema { get; set; } = 1;
        public string Mode { get; set; } = "";
        public string Phase { get; set; } = "";
        public string EndpointFingerprint { get; set; } = "";
        public string InFlightAction { get; set; } = "";
        public DateTimeOffset StartedUtc { get; set; }
        public bool Done { get; set; }
        public int Total { get; set; }
        public int Completed { get; set; }
        public int AddedArtists { get; set; }
        public int ExistingArtists { get; set; }
        public int MonitoredAlbums { get; set; }
        public int AlreadyMonitoredAlbums { get; set; }
        public int SearchesQueued { get; set; }
        public int Failed { get; set; }
        public string[] ExpectedArtistIds { get; set; } = Array.Empty<string>();
        public int[] ExpectedAlbumIds { get; set; } = Array.Empty<int>();
        public string[] ConfirmedArtistIds { get; set; } = Array.Empty<string>();
        public int[] ConfirmedAlbumIds { get; set; } = Array.Empty<int>();
        public List<ImportEvent> Events { get; set; } = new();
    }
    readonly object importGate = new();
    LidarrWriteState importWrite = new();
    CancellationTokenSource? importCancellation;
    bool importCheckpointUnreadable;
    string ImportCheckpointPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MusicTakeoutToolkit", "lidarr-import-checkpoint-v1.json");
    static string EndpointHash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value.TrimEnd('/').ToLowerInvariant())));

    // Caller must hold importGate. Atomic replace prevents a partial state
    // file and is performed before any potentially ambiguous Lidarr write.
    void SaveImportCheckpointLocked()
    {
        var s = importWrite;
        if (s.Total <= 0) return;
        var snapshot = new ImportCheckpoint {
            Mode = s.Mode, Phase = s.Phase, EndpointFingerprint = s.EndpointFingerprint,
            InFlightAction = s.InFlightAction, StartedUtc = s.StartedUtc,
            Done = s.Done, Total = s.Total, Completed = s.Completed,
            AddedArtists = s.AddedArtists, ExistingArtists = s.ExistingArtists,
            MonitoredAlbums = s.MonitoredAlbums, AlreadyMonitoredAlbums = s.AlreadyMonitoredAlbums,
            SearchesQueued = s.SearchesQueued, Failed = s.Failed,
            ExpectedArtistIds = s.ExpectedArtistIds, ExpectedAlbumIds = s.ExpectedAlbumIds,
            ConfirmedArtistIds = s.ConfirmedArtistIds.ToArray(),
            ConfirmedAlbumIds = s.ConfirmedAlbumIds.ToArray(), Events = s.Events.ToList()
        };
        var path = ImportCheckpointPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    void MarkImportWriteIntent(string operation)
    {
        lock (importGate)
        {
            importWrite.InFlightAction = operation;
            SaveImportCheckpointLocked();
        }
    }

    void LoadImportCheckpoint()
    {
        var path = ImportCheckpointPath;
        if (!File.Exists(path)) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 3_000_000) throw new InvalidOperationException("Import checkpoint is unexpectedly large.");
            var saved = JsonSerializer.Deserialize<ImportCheckpoint>(File.ReadAllText(path, Encoding.UTF8));
            if (saved == null || saved.Schema != 1 || saved.Total is <= 0 or > 3001 ||
                saved.Completed < 0 || saved.Completed > saved.Total ||
                (saved.ExpectedArtistIds?.Length ?? 0) + (saved.ExpectedAlbumIds?.Length ?? 0) != saved.Total ||
                saved.Events == null || saved.Events.Count > 3005)
                throw new InvalidOperationException("Import checkpoint validation failed.");
            lock (importGate)
            {
                importWrite = new LidarrWriteState {
                    Running = false, Done = saved.Done, RecoveredFromCheckpoint = true,
                    Mode = saved.Mode, EndpointFingerprint = saved.EndpointFingerprint,
                    InFlightAction = saved.InFlightAction, StartedUtc = saved.StartedUtc,
                    Total = saved.Total, Completed = saved.Completed, AddedArtists = saved.AddedArtists,
                    ExistingArtists = saved.ExistingArtists, MonitoredAlbums = saved.MonitoredAlbums,
                    AlreadyMonitoredAlbums = saved.AlreadyMonitoredAlbums,
                    SearchesQueued = saved.SearchesQueued, Failed = saved.Failed,
                    ExpectedArtistIds = saved.ExpectedArtistIds!, ExpectedAlbumIds = saved.ExpectedAlbumIds!,
                    ConfirmedArtistIds = new HashSet<string>(saved.ConfirmedArtistIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase),
                    ConfirmedAlbumIds = new HashSet<int>(saved.ConfirmedAlbumIds ?? Array.Empty<int>()),
                    Events = saved.Events,
                    Phase = saved.Done ? "Previous Lidarr import restored from checkpoint. Reconnect to verify the catalog."
                        : "Previous Lidarr import was interrupted. No write or search will be automatically repeated."
                };
            }
        }
        catch (Exception ex)
        {
            Log("Import checkpoint could not be restored: " + ex.Message);
            // Preserve the existing file for manual diagnosis; never discard it.
            lock (importGate)
            {
                importCheckpointUnreadable = true;
                importWrite.Phase = "Saved import checkpoint could not be read; do not resend the prior batch.";
            }
        }
    }

    async Task ReconnectImportCheckpointAsync(string url, string apiKey)
    {
        string expected;
        lock (importGate)
        {
            if (importWrite.Total <= 0 || importWrite.Running)
                throw new InvalidOperationException("No stopped import checkpoint is available to inspect.");
            expected = importWrite.EndpointFingerprint;
        }
        var validated = ValidateLidarr(url);
        if (string.IsNullOrWhiteSpace(expected) || EndpointHash(validated) != expected)
            throw new InvalidOperationException("This URL is different from the original Lidarr instance. No changes were made.");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Enter the API key for the original Lidarr instance.");
        using var request = new HttpRequestMessage(HttpMethod.Get, validated + "/api/v1/artist");
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Could not reconnect to Lidarr (HTTP {(int)response.StatusCode}).");
        using var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (list.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected Lidarr artist inventory format.");
        lidarrUrl = validated;
        lidarrKey = apiKey; // Memory only; not included in checkpoints.
        connected = true;
    }

    static List<ArtistAudit> VerifiedArtistsMissingFromLidarr(EvidenceAudit audit,
        IReadOnlyList<LidarrArtist> known)
    {
        var existing = known.Where(a => ValidMbid(a.foreignArtistId))
            .Select(a => a.foreignArtistId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return audit.Artists.Where(a => a.Status == "high" &&
                ValidMbid(a.ArtistId) && !existing.Contains(a.ArtistId))
            .GroupBy(a => a.ArtistId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.SourceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    async Task<JsonDocument> ImportGetAsync(string relativePath, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, lidarrUrl + relativePath);
        request.Headers.TryAddWithoutValidation("X-Api-Key", lidarrKey);
        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Lidarr GET {relativePath.Split('?')[0]} returned HTTP {(int)response.StatusCode}: {ShortLidarrError(content)}");
        if (content.Length > 15_000_000) throw new InvalidOperationException("Lidarr response was unexpectedly large.");
        return JsonDocument.Parse(content);
    }

    async Task ImportWriteAsync(HttpMethod method, string relativePath, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, lidarrUrl + relativePath) {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", lidarrKey);
        // Never automatically retry a write. A timeout may mean Lidarr accepted it.
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Lidarr {method.Method} {relativePath} returned HTTP {(int)response.StatusCode}: {ShortLidarrError(responseBody)}");
        }
    }
    static string ShortLidarrError(string response)
        => response.Length > 300 ? response[..300] + "…" : response;

    static object[] ImportOptionsFrom(JsonDocument doc, string idField, string labelField)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected Lidarr settings response.");
        return doc.RootElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new { id = PropInt(x, idField), name = PropText(x, labelField) })
            .Where(x => x.id > 0 && x.name.Length > 0)
            .Select(x => (object)new { x.id, x.name }).ToArray();
    }

    async Task<object> ImportOptionsAsync(CancellationToken ct)
    {
        if (!connected || string.IsNullOrWhiteSpace(lidarrKey))
            throw new InvalidOperationException("Connect to Lidarr first.");
        EvidenceAudit audit; List<LidarrArtist> known; AlbumSearchStageItem[] stages;
        lock (verificationGate)
        {
            if (!inventory.Done || inventory.Running || latestAudit == null)
                throw new InvalidOperationException("Complete the Lidarr comparison first.");
            audit = latestAudit; known = lidarrArtists.ToList(); stages = GetStagingQueue().ToArray();
        }
        using var roots = await ImportGetAsync("/api/v1/rootfolder", ct);
        using var quality = await ImportGetAsync("/api/v1/qualityprofile", ct);
        using var metadata = await ImportGetAsync("/api/v1/metadataprofile", ct);
        if (roots.RootElement.ValueKind != JsonValueKind.Array ||
            quality.RootElement.ValueKind != JsonValueKind.Array ||
            metadata.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Lidarr did not return its root folders and profiles.");
        var rootChoices = roots.RootElement.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => PropText(x, "path")).Where(x => x.Length > 0).Distinct().ToArray();
        return new {
            roots = rootChoices,
            quality = ImportOptionsFrom(quality, "id", "name"),
            metadata = ImportOptionsFrom(metadata, "id", "name"),
            verifiedArtistsToAdd = VerifiedArtistsMissingFromLidarr(audit, known).Count,
            eligibleAlbums = stages.Length,
            note = "Only independently verified artist MBIDs and explicitly selected, track-backed Lidarr albums can be written. Manual source-only proposals stay local."
        };
    }

    object ImportStatus()
    {
        lock (importGate)
        {
            var s = importWrite;
            return new { running = s.Running, done = s.Done, mode = s.Mode, phase = s.Phase,
                total = s.Total, completed = s.Completed, addedArtists = s.AddedArtists,
                existingArtists = s.ExistingArtists, monitoredAlbums = s.MonitoredAlbums,
                alreadyMonitoredAlbums = s.AlreadyMonitoredAlbums, searchesQueued = s.SearchesQueued,
                failed = s.Failed, catalogVerified = s.CatalogVerified,
                catalogPending = Math.Max(0, s.Total - s.CatalogVerified),
                catalogComplete = s.CatalogComplete, rechecking = s.Rechecking,
                restored = s.RecoveredFromCheckpoint,
                checkpointUnreadable = importCheckpointUnreadable,
                interrupted = s.Total > 0 && (!s.Done || s.Completed < s.Total),
                inFlightAction = s.InFlightAction, startedUtc = s.StartedUtc,
                endpointMatches = string.IsNullOrEmpty(s.EndpointFingerprint) ||
                    (connected && EndpointHash(lidarrUrl) == s.EndpointFingerprint),
                events = s.Events.TakeLast(80).ToArray() };
        }
    }

    // A queued search is NOT proof that the artist/album exists in Lidarr.
    // These GETs are read-only; we never repeat a write to resolve uncertainty.
    async Task<bool> ArtistIsInCatalogAsync(string mbid, CancellationToken ct)
    {
        using var artists = await ImportGetAsync("/api/v1/artist", ct);
        return artists.RootElement.ValueKind == JsonValueKind.Array &&
            artists.RootElement.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object &&
                PropText(x, "foreignArtistId").Equals(mbid, StringComparison.OrdinalIgnoreCase));
    }

    async Task<bool> AlbumIsInCatalogAsync(int albumId, CancellationToken ct)
    {
        using var album = await ImportGetAsync("/api/v1/album/" + albumId, ct);
        return album.RootElement.ValueKind == JsonValueKind.Object &&
            PropInt(album.RootElement, "id") == albumId && PropBool(album.RootElement, "monitored");
    }

    void ConfirmArtist(string mbid)
    {
        lock (importGate) { importWrite.ConfirmedArtistIds.Add(mbid); SaveImportCheckpointLocked(); }
    }

    void ConfirmAlbum(int albumId)
    {
        lock (importGate) { importWrite.ConfirmedAlbumIds.Add(albumId); SaveImportCheckpointLocked(); }
    }

    async Task<bool> TryConfirmArtistAsync(string mbid, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await ArtistIsInCatalogAsync(mbid, ct)) { ConfirmArtist(mbid); return true; } }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* Subsequent read-only check may succeed. */ }
            if (attempt < 2) await Task.Delay(650, ct);
        }
        return false;
    }

    async Task<bool> TryConfirmAlbumAsync(int albumId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await AlbumIsInCatalogAsync(albumId, ct)) { ConfirmAlbum(albumId); return true; } }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* Subsequent read-only check may succeed. */ }
            if (attempt < 2) await Task.Delay(650, ct);
        }
        return false;
    }

    // Read-only reconciliation after writes, including an explicit retry for
    // metadata updates that Lidarr exposes only after a short delay.
    async Task RecheckImportCatalogAsync(CancellationToken ct, bool alreadyReserved = false)
    {
        string[] artists; int[] albums;
        lock (importGate)
        {
            if (importWrite.Total <= 0 || importWrite.Running ||
                (importWrite.Rechecking && !alreadyReserved))
                return;
            if (!connected || EndpointHash(lidarrUrl) != importWrite.EndpointFingerprint)
            {
                importWrite.Rechecking = false;
                importWrite.Phase = "Reconnect to the original Lidarr instance before rechecking this import.";
                return;
            }
            importWrite.Rechecking = true;
            importWrite.Phase = "Confirming all selected artists and albums in Lidarr";
            artists = importWrite.ExpectedArtistIds.Except(importWrite.ConfirmedArtistIds,
                StringComparer.OrdinalIgnoreCase).ToArray();
            albums = importWrite.ExpectedAlbumIds.Except(importWrite.ConfirmedAlbumIds).ToArray();
        }
        try
        {
            foreach (var mbid in artists)
            {
                ct.ThrowIfCancellationRequested();
                await TryConfirmArtistAsync(mbid, ct);
            }
            foreach (var id in albums)
            {
                ct.ThrowIfCancellationRequested();
                await TryConfirmAlbumAsync(id, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (importGate)
            {
                importWrite.Rechecking = false;
                importWrite.Phase = importWrite.CatalogComplete
                    ? "Selected artists and albums confirmed in Lidarr. Background downloads may continue."
                    : $"Lidarr catalog verification: {importWrite.CatalogVerified} of {importWrite.Total} confirmed; " +
                      (importWrite.Failed > 0 ? "review failed requests." : "unconfirmed items can be rechecked without resending writes.");
                SaveImportCheckpointLocked();
            }
        }
    }

    void StartImportCatalogRecheck()
    {
        lock (importGate)
        {
            if (importWrite.Total <= 0 || importWrite.Running || importWrite.Rechecking)
                throw new InvalidOperationException("Wait for the import or existing catalog check to finish.");
            if (!connected || EndpointHash(lidarrUrl) != importWrite.EndpointFingerprint)
                throw new InvalidOperationException("Reconnect to the original Lidarr instance before rechecking the checkpoint.");
            importWrite.Rechecking = true; // Reserve before scheduling; two clicks cannot race.
        }
        _ = Task.Run(() => RecheckImportCatalogAsync(CancellationToken.None, alreadyReserved: true));
    }

    async Task<object> ImportBackgroundAsync(CancellationToken ct)
    {
        if (!connected) return new { active = 0, tasks = Array.Empty<object>() };
        using var commandList = await ImportGetAsync("/api/v1/command", ct);
        if (commandList.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Unexpected Lidarr command status response.");
        var active = commandList.RootElement.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new {
                name = PropText(x, "name"), status = PropText(x, "status"),
                state = PropText(x, "state"), message = PropText(x, "message")
            })
            .Where(x => x.status.Equals("queued", StringComparison.OrdinalIgnoreCase) ||
                x.status.Equals("started", StringComparison.OrdinalIgnoreCase) ||
                x.state.Equals("started", StringComparison.OrdinalIgnoreCase) ||
                x.state.Equals("running", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return new { active = active.Length, tasks = active.Take(12)
            .Select(x => (object)new { x.name, x.status, x.state, x.message }).ToArray() };
    }

    void ImportProgress(string kind, string name, string outcome, string detail)
    {
        lock (importGate)
        {
            importWrite.Completed++;
            importWrite.Events.Add(new ImportEvent(kind, name, outcome, detail));
            switch (outcome)
            {
                case "artist-added": importWrite.AddedArtists++; break;
                case "artist-exists": importWrite.ExistingArtists++; break;
                case "album-monitored": importWrite.MonitoredAlbums++; break;
                case "album-already-monitored": importWrite.AlreadyMonitoredAlbums++; break;
                case "search-queued": importWrite.SearchesQueued++; break;
                case "failed": importWrite.Failed++; break;
            }
            importWrite.Phase = $"{importWrite.Completed} of {importWrite.Total} actions checked";
            importWrite.InFlightAction = "";
            SaveImportCheckpointLocked();
        }
    }

    async Task StartImportAsync(JsonElement payload)
    {
        var mode = PropText(payload, "mode");
        if (mode != "catalog" && mode != "search")
            throw new InvalidOperationException("Choose Add to Lidarr Only or Add and Search.");
        if (!payload.TryGetProperty("albumIds", out var selected) ||
            selected.ValueKind != JsonValueKind.Array || selected.GetArrayLength() > 3000)
            throw new InvalidOperationException("Invalid selected album list.");
        var albumIds = selected.EnumerateArray().Select(x =>
            x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var i) ? i : 0).ToArray();
        if (albumIds.Any(i => i <= 0) || albumIds.Distinct().Count() != albumIds.Length)
            throw new InvalidOperationException("Invalid or duplicate album IDs.");
        if (mode == "search" && (albumIds.Length == 0 ||
            !payload.TryGetProperty("confirmAlbumSearch", out var consent) ||
            consent.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException("Choose an eligible album and explicitly confirm its Lidarr search.");
        var includeArtists = payload.TryGetProperty("includeArtists", out var ia) && ia.ValueKind == JsonValueKind.True;
        var rootPath = PropText(payload, "rootPath");
        var qualityId = PropInt(payload, "qualityId");
        var metadataId = PropInt(payload, "metadataId");
        lock (importGate)
            if (importCheckpointUnreadable || importWrite.Running || importWrite.Total > 0 && !importWrite.CatalogComplete)
                throw new InvalidOperationException("A prior import is still pending or interrupted. Reconcile its saved checkpoint before starting a new batch; no duplicate searches were sent.");
        EvidenceAudit audit; List<LidarrArtist> known; HashSet<int> eligible;
        lock (verificationGate)
        {
            if (!connected || !inventory.Done || inventory.Running || latestAudit == null ||
                verification.Running || supplemental.Running || recovery.Running)
                throw new InvalidOperationException("Finish Lidarr connection, verification and comparison first.");
            audit = latestAudit;
            known = lidarrArtists.ToList();
            eligible = GetStagingQueue().Select(x => x.AlbumId).ToHashSet();
        }
        if (albumIds.Any(id => !eligible.Contains(id)))
            throw new InvalidOperationException("Album selection contains an unverified or obsolete entry. Refresh Migration Preview.");
        var artists = includeArtists ? VerifiedArtistsMissingFromLidarr(audit, known) : new List<ArtistAudit>();
        if (artists.Count + albumIds.Length == 0)
            throw new InvalidOperationException("There are no eligible artists or selected albums to add. Unmatched songs remain available for manual import.");
        if (artists.Count > 0)
        {
            using var roots = await ImportGetAsync("/api/v1/rootfolder", CancellationToken.None);
            using var qualities = await ImportGetAsync("/api/v1/qualityprofile", CancellationToken.None);
            using var metadatas = await ImportGetAsync("/api/v1/metadataprofile", CancellationToken.None);
            bool hasRoot = roots.RootElement.EnumerateArray().Any(x => PropText(x, "path") == rootPath);
            bool hasQuality = qualities.RootElement.EnumerateArray().Any(x => PropInt(x, "id") == qualityId);
            bool hasMetadata = metadatas.RootElement.EnumerateArray().Any(x => PropInt(x, "id") == metadataId);
            if (!hasRoot || !hasQuality || !hasMetadata)
                throw new InvalidOperationException("Choose a valid Lidarr root folder, quality profile and metadata profile.");
        }
        lock (importGate)
        {
            if (importCheckpointUnreadable || importWrite.Running || importWrite.Total > 0 && !importWrite.CatalogComplete)
                throw new InvalidOperationException("Resolve the previous import checkpoint before starting another batch.");
            importCancellation?.Dispose();
            importCancellation = new CancellationTokenSource();
            importWrite = new LidarrWriteState {
                Running = true, Mode = mode, Total = artists.Count + albumIds.Length,
                ExpectedArtistIds = artists.Select(x => x.ArtistId).ToArray(),
                ExpectedAlbumIds = albumIds.ToArray(),
                EndpointFingerprint = EndpointHash(lidarrUrl), StartedUtc = DateTimeOffset.UtcNow,
                Phase = "Starting explicitly approved Lidarr changes"
            };
            try { SaveImportCheckpointLocked(); }
            catch
            {
                // No Lidarr write has been attempted yet. Leave this process
                // able to retry after its local storage problem is corrected.
                importWrite = new LidarrWriteState();
                throw;
            }
        }
        var token = importCancellation.Token;
        _ = Task.Run(() => ExecuteImportAsync(artists, albumIds, mode, rootPath, qualityId, metadataId, token));
    }

    async Task ExecuteImportAsync(List<ArtistAudit> artists, int[] selectedAlbums, string mode,
        string rootPath, int qualityId, int metadataId, CancellationToken ct)
    {
        try
        {
            using var allArtists = await ImportGetAsync("/api/v1/artist", ct);
            if (allArtists.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Lidarr artist inventory is not an array.");
            var present = allArtists.RootElement.EnumerateArray()
                .Select(x => PropText(x, "foreignArtistId"))
                .Where(ValidMbid).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var artist in artists)
            {
                ct.ThrowIfCancellationRequested();
                lock (importGate) importWrite.Phase = "Adding verified artist: " + artist.SourceName;
                try
                {
                    if (present.Contains(artist.ArtistId))
                    {
                        ConfirmArtist(artist.ArtistId);
                        ImportProgress("artist", artist.SourceName, "artist-exists", "Existing artist confirmed in Lidarr; no duplicate was added.");
                        continue;
                    }
                    // Lidarr lookup yields its canonical artist model. Never manufacture
                    // an artist ID from a matching name or a review-only proposal.
                    using var lookup = await ImportGetAsync("/api/v1/artist/lookup?term=" +
                        Uri.EscapeDataString("lidarr:" + artist.ArtistId), ct);
                    if (lookup.RootElement.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException("Artist lookup returned no valid list.");
                    var found = lookup.RootElement.EnumerateArray()
                        .FirstOrDefault(x => x.ValueKind == JsonValueKind.Object &&
                            PropText(x, "foreignArtistId").Equals(artist.ArtistId, StringComparison.OrdinalIgnoreCase));
                    if (found.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("Lidarr did not confirm this MusicBrainz artist ID.");
                    var resource = JsonNode.Parse(found.GetRawText())!.AsObject();
                    resource["foreignArtistId"] = artist.ArtistId;
                    resource["rootFolderPath"] = rootPath;
                    resource["qualityProfileId"] = qualityId;
                    resource["metadataProfileId"] = metadataId;
                    resource["monitored"] = false;
                    resource["monitorNewItems"] = "none";
                    resource["addOptions"] = new JsonObject {
                        ["monitor"] = "none", ["monitored"] = false, ["searchForMissingAlbums"] = false
                    };
                    MarkImportWriteIntent("Add artist " + artist.ArtistId);
                    await ImportWriteAsync(HttpMethod.Post, "/api/v1/artist", resource, ct);
                    present.Add(artist.ArtistId);
                    var confirmed = await TryConfirmArtistAsync(artist.ArtistId, ct);
                    ImportProgress("artist", artist.SourceName, "artist-added",
                        confirmed ? "Artist confirmed in Lidarr. No automatic discography search was sent."
                            : "Lidarr accepted the artist; catalog read-back is pending. Use Recheck if needed.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ImportProgress("artist", artist.SourceName, "failed", ex.Message);
                }
            }
            foreach (var albumId in selectedAlbums)
            {
                ct.ThrowIfCancellationRequested();
                lock (importGate) importWrite.Phase = $"Updating selected Lidarr album #{albumId}";
                try
                {
                    using var albumDoc = await ImportGetAsync("/api/v1/album/" + albumId, ct);
                    var album = albumDoc.RootElement;
                    if (album.ValueKind != JsonValueKind.Object || PropInt(album, "id") != albumId)
                        throw new InvalidOperationException("Selected album is no longer available in Lidarr.");
                    var name = PropText(album, "title");
                    var artistId = PropInt(album, "artistId");
                    if (artistId <= 0) throw new InvalidOperationException("Lidarr album has no valid parent artist.");
                    using var artistDoc = await ImportGetAsync("/api/v1/artist/" + artistId, ct);
                    var artistResource = artistDoc.RootElement;
                    if (artistResource.ValueKind != JsonValueKind.Object || PropInt(artistResource, "id") != artistId)
                        throw new InvalidOperationException("Lidarr parent artist was not found.");
                    if (!PropBool(artistResource, "monitored"))
                    {
                        var updated = JsonNode.Parse(artistResource.GetRawText())!.AsObject();
                        updated["monitored"] = true;
                        MarkImportWriteIntent("Monitor parent artist #" + artistId);
                        await ImportWriteAsync(HttpMethod.Put, "/api/v1/artist/" + artistId, updated, ct);
                    }
                    bool already = PropBool(album, "monitored");
                    if (!already)
                    {
                        MarkImportWriteIntent("Monitor album #" + albumId);
                        await ImportWriteAsync(HttpMethod.Put, "/api/v1/album/monitor",
                            new { albumIds = new[] { albumId }, monitored = true }, ct);
                    }
                    var catalogConfirmed = await TryConfirmAlbumAsync(albumId, ct);
                    if (mode == "search")
                    {
                        MarkImportWriteIntent("Queue album search #" + albumId);
                        await ImportWriteAsync(HttpMethod.Post, "/api/v1/command",
                            new { name = "AlbumSearch", albumIds = new[] { albumId } }, ct);
                        ImportProgress("album", name, "search-queued",
                            catalogConfirmed
                                ? $"Album #{albumId} confirmed monitored in Lidarr; search queued. Download and file import are not yet confirmed."
                                : $"Album #{albumId} search queued; album monitoring still needs catalog read-back confirmation.");
                    }
                    else
                    {
                        ImportProgress("album", name, already ? "album-already-monitored" : "album-monitored",
                            catalogConfirmed ? $"Album #{albumId} confirmed monitored in Lidarr. No search requested."
                                : $"Album #{albumId} monitoring requested; catalog read-back confirmation is still pending.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ImportProgress("album", $"Album #{albumId}", "failed", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (importGate) importWrite.Events.Add(new ImportEvent("job", "", "interrupted",
                "Application stopped or job cancelled. Confirm Lidarr state before retrying; earlier writes may have succeeded."));
        }
        catch (Exception ex)
        {
            lock (importGate)
            {
                importWrite.Failed++;
                importWrite.Events.Add(new ImportEvent("job", "", "failed", ex.Message));
            }
        }
        finally
        {
            lock (importGate)
            {
                importWrite.Running = false; importWrite.Done = true;
                importWrite.Rechecking = true;
                importWrite.Phase = "All Lidarr requests checked; confirming catalog changes before completion.";
                try { SaveImportCheckpointLocked(); }
                catch (Exception ex) { Log("Could not save final import checkpoint: " + ex.Message); }
            }
            _ = Task.Run(() => RecheckImportCatalogAsync(CancellationToken.None, alreadyReserved: true));
        }
    }

    async Task SendImportReportCsv(HttpListenerContext ctx)
    {
        ImportEvent[] rows;
        lock (importGate) rows = importWrite.Events.ToArray();
        var sb = new StringBuilder();
        sb.Append(CsvLine("Item type", "Artist or album", "Outcome", "Detail"));
        foreach (var x in rows) sb.Append(CsvLine(x.Kind, x.Name, x.Outcome, x.Detail));
        await WriteCsvFile(ctx, sb.ToString(), "lidarr-import-results.csv");
    }

    static void LidarrImportSelfTest()
    {
        var status = new LidarrWriteState { Total = 2, Completed = 2, Done = true,
            SearchesQueued = 1, ExpectedArtistIds = new[] { "test-artist" }, ExpectedAlbumIds = new[] { 45 } };
        if (status.CatalogComplete || status.CatalogVerified != 0)
            throw new Exception("Queued search requests must not be treated as catalog completion.");
        status.ConfirmedArtistIds.Add("test-artist");
        if (status.CatalogComplete || status.CatalogVerified != 1)
            throw new Exception("Incomplete catalog must stay on the progress screen.");
        status.ConfirmedAlbumIds.Add(45);
        if (!status.CatalogComplete)
            throw new Exception("Only all-confirmed catalog entries should unlock the final screen.");
        status.Rechecking = true;
        if (status.CatalogComplete)
            throw new Exception("Do not celebrate during a catalog recheck.");
        status.Rechecking = false; status.Failed = 1;
        if (status.CatalogComplete)
            throw new Exception("Failed Lidarr requests must prevent a success celebration.");
        var audit = new EvidenceAudit();
        audit.Artists.Add(new ArtistAudit { SourceName = "Verified", ArtistId = "a1a1a1a1-0000-4000-8000-000000000001", Status = "high" });
        audit.Artists.Add(new ArtistAudit { SourceName = "Unverified", ArtistId = "a1a1a1a1-0000-4000-8000-000000000002", Status = "ambiguous" });
        var selected = VerifiedArtistsMissingFromLidarr(audit, new List<LidarrArtist>());
        if (selected.Count != 1 || selected[0].SourceName != "Verified")
            throw new Exception("Importer must exclude review-only and ambiguous artist identities.");
        var known = new List<LidarrArtist> { new("Verified", audit.Artists[0].ArtistId, 23) };
        if (VerifiedArtistsMissingFromLidarr(audit, known).Count != 0)
            throw new Exception("Importer must never re-add artists already in Lidarr by MusicBrainz ID.");
        Console.WriteLine("PASS: Lidarr catalog-confirmation gate, verified-artist allowlist and existing-artist exclusion");
    }
}
