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
        public string Mode = "", Phase = "Not started";
        public int Total, Completed, AddedArtists, ExistingArtists, MonitoredAlbums,
            AlreadyMonitoredAlbums, SearchesQueued, Failed;
        public List<ImportEvent> Events = new();
    }
    readonly object importGate = new();
    LidarrWriteState importWrite = new();
    CancellationTokenSource? importCancellation;

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
                failed = s.Failed,
                events = s.Events.TakeLast(80).ToArray() };
        }
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
            if (importWrite.Running) throw new InvalidOperationException("Lidarr import is already running.");
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
            if (importWrite.Running) throw new InvalidOperationException("Another import has already started.");
            importCancellation?.Dispose();
            importCancellation = new CancellationTokenSource();
            importWrite = new LidarrWriteState {
                Running = true, Mode = mode, Total = artists.Count + albumIds.Length,
                Phase = "Starting explicitly approved Lidarr changes"
            };
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
                        ImportProgress("artist", artist.SourceName, "artist-exists", "Already found in Lidarr; no duplicate was added.");
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
                    await ImportWriteAsync(HttpMethod.Post, "/api/v1/artist", resource, ct);
                    present.Add(artist.ArtistId);
                    ImportProgress("artist", artist.SourceName, "artist-added",
                        "Artist added without automatic discography monitoring or searching; Lidarr may still be refreshing metadata.");
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
                        await ImportWriteAsync(HttpMethod.Put, "/api/v1/artist/" + artistId, updated, ct);
                    }
                    bool already = PropBool(album, "monitored");
                    if (!already)
                        await ImportWriteAsync(HttpMethod.Put, "/api/v1/album/monitor",
                            new { albumIds = new[] { albumId }, monitored = true }, ct);
                    if (mode == "search")
                    {
                        await ImportWriteAsync(HttpMethod.Post, "/api/v1/command",
                            new { name = "AlbumSearch", albumIds = new[] { albumId } }, ct);
                        ImportProgress("album", name, "search-queued",
                            $"Album #{albumId} monitored; Lidarr accepted the search request. Download and file import are not yet confirmed.");
                    }
                    else
                    {
                        ImportProgress("album", name, already ? "album-already-monitored" : "album-monitored",
                            $"Album #{albumId} is monitored. No search or download requested.");
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
                importWrite.Phase = "Lidarr requests finished; background metadata, searches and downloads may still be running.";
            }
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
        var audit = new EvidenceAudit();
        audit.Artists.Add(new ArtistAudit { SourceName = "Verified", ArtistId = "a1a1a1a1-0000-4000-8000-000000000001", Status = "high" });
        audit.Artists.Add(new ArtistAudit { SourceName = "Unverified", ArtistId = "a1a1a1a1-0000-4000-8000-000000000002", Status = "ambiguous" });
        var selected = VerifiedArtistsMissingFromLidarr(audit, new List<LidarrArtist>());
        if (selected.Count != 1 || selected[0].SourceName != "Verified")
            throw new Exception("Importer must exclude review-only and ambiguous artist identities.");
        var known = new List<LidarrArtist> { new("Verified", audit.Artists[0].ArtistId, 23) };
        if (VerifiedArtistsMissingFromLidarr(audit, known).Count != 0)
            throw new Exception("Importer must never re-add artists already in Lidarr by MusicBrainz ID.");
        Console.WriteLine("PASS: Lidarr importer verified-artist allowlist and existing-artist exclusion");
    }
}
