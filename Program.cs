using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Native .NET application: no PowerShell runtime, command shell, VBScript or script-policy changes.
if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    DesktopServer.RunSelfTests();
    return;
}
var app = new DesktopServer();
await app.RunAsync();

sealed partial class DesktopServer
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    readonly List<Track> tracks = new();
    readonly object gate = new();
    readonly string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MusicTakeoutToolkit", "dotnet-logs");
    string archive = "";
    string lidarrUrl = "http://127.0.0.1:8686";
    string lidarrKey = "";
    List<LidarrArtist> lidarrArtists = new();
    bool connected, analyzing;
    string analysisError = "";
    HttpListener? listener;
    CancellationTokenSource? appStop;
    readonly object verificationGate = new();
    VerifyState verification = new();
    CancellationTokenSource? verificationCancellation;
    CancellationTokenSource? supplementalCancellation;
    CancellationTokenSource? recoveryCancellation;
    // Invalidated when source, audit, or Lidarr connection changes.
    void ResetLidarrComparison() { inventoryCancellation?.Cancel(); inventory = new InventoryState(); stagedAlbumSearches.Clear(); }

    public async Task RunAsync()
    {
        Directory.CreateDirectory(logDir);
        var portListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        portListener.Start();
        int port = ((IPEndPoint)portListener.LocalEndpoint).Port;
        portListener.Stop();
        var address = $"http://127.0.0.1:{port}/";
        using var server = new HttpListener();
        listener = server;
        server.Prefixes.Add(address);
        server.Start();
        Console.WriteLine("YouTube Music to Lidarr — .NET v0.9.1.15.4 (song title filing and stable review navigation)");
        Console.WriteLine("No PowerShell scripts are run. No changes to Windows execution policy.");
        Console.WriteLine("Read-only: import, inspect, and Lidarr inventory only. No downloads or imports.");
        Console.WriteLine("Local address: " + address);
        Console.WriteLine("Press Ctrl+C to close the app.");
        Log("Listening on " + address);
        using var stop = new CancellationTokenSource();
        appStop = stop;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); try { server.Stop(); } catch { } };
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine("Open this address in your browser: " + address + " (" + ex.Message + ")"); }
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await server.GetContextAsync(); }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            _ = Task.Run(async () => { try { await HandleAsync(context); } catch (Exception ex) { Log("Request failure: " + ex); try { await Respond(context, 500, new { error = "Internal error. See the .NET diagnostic log." }); } catch { } } });
        }
        verificationCancellation?.Cancel();
        supplementalCancellation?.Cancel();
        recoveryCancellation?.Cancel();
        inventoryCancellation?.Cancel();
        lidarrKey = "";
        githubAccessToken = null;
    }

    async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath ?? "";
        var expectedHost = "127.0.0.1:" + req.LocalEndPoint.Port;
        var origin = req.Headers["Origin"];
        if (req.Headers["Host"] != expectedHost || (origin != null && origin != "http://" + expectedHost)) { await Respond(ctx, 403, new { error = "Invalid local origin." }); return; }
        if (path == "/" || path == "/index.html")
        {
            if (req.HttpMethod != "GET") { await Respond(ctx, 405, new { error = "Method not permitted." }); return; }
            var html = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "AppUI", "index.html"));
            html = html.Replace("__LOCAL_SESSION_TOKEN__", token);
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers.Add("Content-Security-Policy", "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-src https://www.youtube-nocookie.com; img-src 'self' data: https://*.mzstatic.com https://i.ytimg.com https://img.youtube.com; media-src 'self' https://*.itunes.apple.com https://*.mzstatic.com; base-uri 'none'; object-src 'none'");
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            ctx.Response.Headers.Add("Referrer-Policy", "no-referrer");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close(); return;
        }
        if (path == "/flow918.css" || path == "/flow918.js")
        {
            if (req.HttpMethod != "GET") { await Respond(ctx, 405, new { error = "Method not permitted." }); return; }
            var file = path == "/flow918.css" ? "flow918.css" : "flow918.js";
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "AppUI", file));
            ctx.Response.ContentType = path.EndsWith(".css") ? "text/css; charset=utf-8" : "application/javascript; charset=utf-8";
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close(); return;
        }
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || req.HttpMethod != "POST") { await Respond(ctx, 404, new { error = "Not found." }); return; }
        if (req.Headers["X-Local-Session"] != token) { await Respond(ctx, 403, new { error = "Invalid session." }); return; }
        try
        {
            if (req.ContentLength64 > (path == "/api/audit/import" ? 12_000_000 : 65536))
                throw new InvalidOperationException("Request exceeds allowed size.");
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            var maxBody = path == "/api/audit/import" ? 8_000_000 : 65536;
            if (body.Length > maxBody) throw new InvalidOperationException("Request too large.");
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            string Value(string name) => json.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            switch (path)
            {
                case "/api/heartbeat": await Respond(ctx, 200, new { ok = true }); return;
                case "/api/review/auto-classify/start": await Respond(ctx, 200, StartAutomaticReviewClassification()); return;
                case "/api/review/auto-classify/status": await Respond(ctx, 200, AutomaticReviewStatus()); return;
                case "/api/review/manual-metadata/restore":
                    RestoreManualFallback(Value("key"));
                    InvalidateReviewMedia(Value("key"));
                    await Respond(ctx, 200, new { restored = true, localOnly = true }); return;
                case "/api/review/manual-metadata/auto":
                    var automaticallySaved = AutoSaveManualFallback(Value("key"), Value("reason") is { Length: > 0 } reason ? reason : "no-usable-candidate");
                    await Respond(ctx, 200, new { saved = true, newEntry = automaticallySaved, localOnly = true,
                        note = "Saved source metadata for later manual import. No Lidarr writes, downloads, or verified identities." }); return;
                case "/api/review/manual-metadata":
                    SaveManualFallback(Value("key"), Value("artist"), Value("song"), Value("album"), Value("youtubeUrl"));
                    InvalidateReviewMedia(Value("key"));
                    await Respond(ctx, 200, new { saved = true, note = "Saved to local manual metadata. Nothing sent to Lidarr or downloaded." }); return;
                case "/api/review/candidate/details": await SendReviewCandidateDetails(ctx, Value("key"), Value("candidateId")); return;
                case "/api/pipeline/status": await Respond(ctx, 200, UnifiedPipelineStatus()); return;
                case "/api/pipeline/start":
                    Track[] pipelineSource;
                    lock (gate) { if (analyzing) throw new InvalidOperationException("Takeout analysis is still running."); pipelineSource = tracks.ToArray(); }
                    BeginUnifiedPipeline(pipelineSource);
                    await Respond(ctx, 200, new { started = true }); return;
                case "/api/app/exit":
                    await Respond(ctx, 200, new { ok = true });
                    _ = Task.Run(async () => { await Task.Delay(250); try { appStop?.Cancel(); listener?.Stop(); } catch { } });
                    return;
                case "/api/github/status": await Respond(ctx, 200, GitHubStatus()); return;
                case "/api/github/authorize/start": await Respond(ctx, 200, await BeginGitHubDeviceAuthorization()); return;
                case "/api/github/authorize/poll": await Respond(ctx, 200, await PollGitHubAuthorization()); return;
                case "/api/github/star": await Respond(ctx, 200, await StarGitHubRepository()); return;
                case "/api/state":
                    Track[] current; string selected, err; bool active;
                    lock (gate) { current = tracks.ToArray(); selected = archive; err = analysisError; active = analyzing; }
                    await Respond(ctx, 200, new { archive = selected, analyzing = active, analysisError = err, summary = current.Length == 0 ? null : new { songs = current.Length, artists = current.Select(t => t.Artist).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(), warning = "Source artist strings are not yet verified identities." }, tracks = current.Select(t => new { t.Artist, t.Song, t.YouTubeUrl }), lidarrUrl, lidarrConnected = connected }); return;
                case "/api/scan":
                    if (pipelineActive) throw new InvalidOperationException("Verification is still running. Refresh the page to resume the progress screen; do not rescan your Takeout ZIP.");
                    var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    var files = Directory.Exists(downloads) ? Directory.GetFiles(downloads, "*.zip").Where(p => Path.GetFileName(p).StartsWith("takeout", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc) : Enumerable.Empty<string>();
                    string supplied = Value("path");
                    string? selectedFile = !string.IsNullOrWhiteSpace(supplied) && ContainsMusicCsv(supplied) ? supplied : files.FirstOrDefault(ContainsMusicCsv);
                    lock (gate) { archive = selectedFile ?? ""; }
                    await Respond(ctx, 200, new { found = selectedFile != null, archive = selectedFile ?? "", message = selectedFile is null ? "No YouTube Music Takeout ZIP with 'music library songs.csv' found in Downloads. Move your Takeout ZIP there." : "" }); return;
                case "/api/analyze":
                    if (string.IsNullOrWhiteSpace(archive)) throw new InvalidOperationException("Scan for a Takeout ZIP first.");
                    lock (verificationGate) { if (pipelineActive || verification.Running || supplemental.Running || recovery.Running || inventory.Running) throw new InvalidOperationException("Cannot change Takeout data while research or Lidarr comparison is running."); verification = new VerifyState(); supplemental = new SupplementalState(); recovery = new IdentifierRecoveryState(); corroboration.Clear(); latestAudit = null; ResetLidarrComparison(); }
                    lock (gate) { tracks.Clear(); analysisError = ""; analyzing = true; }
                    _ = Task.Run(() => { try { var imported = ParseMusicZip(archive); lock (gate) { tracks.Clear(); tracks.AddRange(imported); } Log($"Imported {imported.Count} Takeout song rows."); } catch (Exception ex) { lock (gate) { analysisError = ex.Message; } Log("Takeout error: " + ex); } finally { lock (gate) { analyzing = false; } } });
                    await Respond(ctx, 200, new { started = true }); return;
                case "/api/lidarr/check":
                    var checkUrl = ValidateLidarr(Value("url"));
                    using (var check = await http.GetAsync(checkUrl + "/api/v1/system/status"))
                        await Respond(ctx, 200, new { found = true, url = checkUrl, requiresApiKey = check.StatusCode == HttpStatusCode.Unauthorized || check.StatusCode == HttpStatusCode.Forbidden });
                    return;
                case "/api/lidarr/connect":
                    if (pipelineActive) throw new InvalidOperationException("The automatic verification is running; wait until it finishes.");
                    var uri = ValidateLidarr(Value("url"));
                    var key = Value("key");
                    if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Enter your Lidarr API key.");
                    using (var msg = new HttpRequestMessage(HttpMethod.Get, uri + "/api/v1/artist"))
                    {
                        msg.Headers.TryAddWithoutValidation("X-Api-Key", key);
                        using var response = await http.SendAsync(msg);
                        response.EnsureSuccessStatusCode();
                        using var artistJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (artistJson.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Unexpected Lidarr artist inventory format.");
                        lidarrArtists = artistJson.RootElement.EnumerateArray().Select(x => new LidarrArtist(
                            x.TryGetProperty("artistName", out var n) ? n.GetString() ?? "" : "",
                            x.TryGetProperty("foreignArtistId", out var i) ? i.GetString() ?? "" : "",
                            x.TryGetProperty("id", out var lid) && lid.TryGetInt32(out var artistNumber) ? artistNumber : 0)).ToList();
                        lock (verificationGate) ResetLidarrComparison();
                        lidarrUrl = uri; lidarrKey = key; connected = true;
                        Log($"Lidarr inventory returned {lidarrArtists.Count} artists from {uri}; first names: {string.Join(", ", lidarrArtists.Take(3).Select(x => x.artistName))}");
                        Track[] automaticSource; lock (gate) automaticSource = tracks.ToArray();
                        BeginUnifiedPipeline(automaticSource); // Starts automatically after successful Lidarr authentication.
                        await Respond(ctx, 200, new { connected = true, automaticVerificationStarted = true,
                            artists = lidarrArtists.Count, inventoryWarning = lidarrArtists.Count <= 1 ? "Lidarr reports one or fewer artists. Please confirm this is the correct Lidarr instance." : "", artistRecords = lidarrArtists });
                    }
                    return;
                case "/api/lidarr/compare":
                    if (!connected) throw new InvalidOperationException("Connect to Lidarr first.");
                    Track[] all; lock (gate) all = tracks.ToArray();
                    var sourceArtists = all.Select(t => t.Artist).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    var knownNames = lidarrArtists.Select(x => Normalize(x.artistName)).ToHashSet();
                    int matched = sourceArtists.Count(x => knownNames.Contains(Normalize(x)));
                    await Respond(ctx, 200, new { totalArtists = sourceArtists.Length, namePresent = matched, requiresVerification = sourceArtists.Length - matched, songs = all.Length, tracksChecked = 0, trackReadErrors = 0, songTitleCandidates = 0, songFileCandidates = 0, songsNeedsReview = all.Length, songsNotTrackedByName = 0, verifiedArtistsToAdd = 0, verifiedSongsToSearch = 0, note = "Artist name overlap only. No album, recording or file checks have run. All song verification remains incomplete." }); return;
                case "/api/verify/start":
                    Track[] source;
                    lock (gate) { if (analyzing) throw new InvalidOperationException("Wait for Takeout analysis to finish."); source = tracks.ToArray(); }
                    if (!connected) throw new InvalidOperationException("Connect to Lidarr first.");
                    if (source.Length == 0) throw new InvalidOperationException("Import Takeout music first.");
                    lock (verificationGate)
                    {
                        if (pipelineActive || verification.Running || supplemental.Running || recovery.Running || inventory.Running) throw new InvalidOperationException("Research, verification or Lidarr comparison is already running.");
                        verificationCancellation?.Dispose();
                        verificationCancellation = new CancellationTokenSource();
                        latestAudit = null; supplemental = new SupplementalState(); recovery = new IdentifierRecoveryState(); corroboration.Clear(); ResetLidarrComparison(); verification = new VerifyState { Started = true, Running = true, StartedAt = DateTimeOffset.UtcNow, Total = source.Length, TotalArtists = source.Select(x => x.Artist.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Phase = "Starting fresh MusicBrainz lookups..." };
                        var ct = verificationCancellation.Token;
                        _ = Task.Run(() => VerifyTracksAsync(source, ct));
                    }
                    await Respond(ctx, 200, new { started = true, total = source.Length }); return;
                case "/api/audit/import":
                    Track[] importedSource;
                    lock (gate) importedSource = tracks.ToArray();
                    if (importedSource.Length == 0) throw new InvalidOperationException("Analyze the matching Takeout archive first.");
                    lock (verificationGate) if (verification.Running || supplemental.Running || recovery.Running || inventory.Running) throw new InvalidOperationException("Wait for the current operation to finish.");
                    var songsText = Value("songsCsv"); var artistsText = Value("artistsCsv");
                    var baselineSongsText = Value("baselineSongsCsv");
                    var candidateScoresText = Value("candidateScoresCsv");
                    if (songsText.Length == 0 || artistsText.Length == 0) throw new InvalidOperationException("Select both exported audit CSV files.");
                    var priorAudit = ParsePriorAudit(songsText, artistsText, importedSource);
                    var repairedRows = 0;
                    if (!string.IsNullOrWhiteSpace(baselineSongsText))
                    {
                        // Reuse the same artist file only for validating row alignment;
                        // original artist counts do not overwrite the newly recovered IDs.
                        var baselineAudit = ParsePriorAudit(baselineSongsText, artistsText, importedSource);
                        repairedRows = RestoreBaselineEvidence(priorAudit, baselineAudit);
                    }
                    var importedCandidates = ParsePreviousCandidateScores(candidateScoresText, priorAudit);
                    lock (verificationGate)
                    {
                        latestAudit = priorAudit; supplemental = new SupplementalState(); recovery = new IdentifierRecoveryState(); corroboration.Clear();
                        corroboration.AddRange(importedCandidates); ResetLidarrComparison();
                        verification = new VerifyState { Started = true, Completed = true, ImportedAuditOnly = true,
                            Total = importedSource.Length, Checked = importedSource.Length,
                            TotalArtists = priorAudit.Artists.Count, ResolvedArtists = priorAudit.Artists.Count(a => a.ArtistId.Length > 0),
                            ArtistHighConfidence = priorAudit.Artists.Count(a => a.Status == "high"),
                            ArtistNeedsReview = priorAudit.Artists.Count(a => a.Status != "high"),
                            SongHighConfidence = priorAudit.Songs.Count(a => a.Status == "high"),
                            SongNeedsReview = priorAudit.Songs.Count(a => a.Status != "high"),
                            SongIdentitySupported = priorAudit.Songs.Count(a => a.SongIdentityStatus == "artist-and-song-supported"),
                            VerifiedRecordings = priorAudit.Songs.Count(a => a.Status is "high" or "artist-supported/version-review"),
                            Ambiguous = priorAudit.Songs.Count(a => a.Status == "ambiguous"),
                            Unmatched = priorAudit.Songs.Count(a => a.Status == "unmatched"),
                            Phase = "Imported previous CSV evidence (not a fresh MusicBrainz scan). Ready for optional catalog research."
                        };
                    }
                    await Respond(ctx, 200, new { imported = true, songs = priorAudit.Songs.Count, artists = priorAudit.Artists.Count, candidateScoreRows = importedCandidates.Count, repairedRows }); return;
                case "/api/catalog/start":
                    Track[] catalogSource;
                    lock (gate) catalogSource = tracks.ToArray();
                    int catalogCount;
                    lock (verificationGate)
                    {
                        if (verification.Running || supplemental.Running || recovery.Running || inventory.Running) throw new InvalidOperationException("Another research or comparison operation is running.");
                        if (supplemental.Done) throw new InvalidOperationException("Already researched these rows. Import new reports or start a fresh verification to repeat.");
                        if (latestAudit == null || catalogSource.Length != latestAudit.Songs.Count)
                            throw new InvalidOperationException("Run verification or import matching audit exports first.");
                        supplementalCancellation?.Dispose(); supplementalCancellation = new CancellationTokenSource();
                        var auditSnapshot = latestAudit;
                        // Research is explicitly started by the user; no automatic transfer
                        // of track titles to Apple or Deezer without this action.
                        ResetLidarrComparison();
                        supplemental = new SupplementalState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                            Phase = "Beginning optional external catalog checks" };
                        catalogCount = auditSnapshot.Songs.Count(x => x.Status is "ambiguous" or "unmatched");
                        var catalogToken = supplementalCancellation.Token;
                        _ = Task.Run(() => RunSupplementalAsync(catalogSource, auditSnapshot, catalogToken));
                    }
                    await Respond(ctx, 200, new { started = true, targetSongs = catalogCount }); return;
                case "/api/catalog/status":
                    SupplementalState cat;
                    lock (verificationGate) cat = supplemental.Copy();
                    await Respond(ctx, 200, new { running = cat.Running, done = cat.Done, failed = cat.Failed,
                        total = cat.Total, checkedCount = cat.Checked,
                        appleSupported = cat.AppleSupported, deezerSupported = cat.DeezerSupported,
                        bothSupported = cat.BothSupported, newMusicBrainzIdentities = cat.MbRecovered, providerFailures = cat.ApiFailures,
                        phase = cat.Phase, error = cat.Error,
                        elapsedSeconds = cat.StartedAt == DateTimeOffset.MinValue ? 0 : Math.Max(0,(int)(DateTimeOffset.UtcNow-cat.StartedAt).TotalSeconds) }); return;
                case "/api/recovery/start":
                    Track[] identifierSource;
                    lock (gate) identifierSource = tracks.ToArray();
                    int recoveryTotal;
                    lock (verificationGate)
                    {
                        if (pipelineActive || verification.Running || supplemental.Running || recovery.Running || inventory.Running)
                            throw new InvalidOperationException("Wait for the active research or comparison operation to finish.");
                        if (recovery.Done) throw new InvalidOperationException("This evidence was already researched. Re-import the audit files to restart intentionally.");
                        if (!connected || latestAudit == null || latestAudit.Songs.Count != identifierSource.Length)
                            throw new InvalidOperationException("Connect Lidarr and verify or import matching audit files first.");
                        recoveryCancellation?.Dispose(); recoveryCancellation = new CancellationTokenSource();
                        var recoveryAudit = latestAudit;
                        recoveryTotal = recoveryAudit.Artists.Count(a => a.Status != "high") +
                            recoveryAudit.Songs.Count(a => a.DeezerTrackId.Length > 0 && a.Status != "high");
                        ResetLidarrComparison();
                        corroboration.Clear();
                        recovery = new IdentifierRecoveryState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                            Total = recoveryTotal, Phase = "Checking artist identifiers and track ISRC evidence" };
                        var ct = recoveryCancellation.Token;
                        _ = Task.Run(() => RunIdentifierRecoveryAsync(identifierSource, recoveryAudit, ct));
                    }
                    await Respond(ctx, 200, new { started = true, eligible = recoveryTotal }); return;
                case "/api/recovery/status":
                    IdentifierRecoveryState rs;
                    lock (verificationGate) rs = recovery.Copy();
                    await Respond(ctx, 200, new { running = rs.Running, done = rs.Done, failed = rs.Failed,
                        total = rs.Total, checkedCount = rs.Checked, artistsResolved = rs.ArtistsResolved,
                        artistCandidates = rs.ArtistCandidates, isrcFound = rs.IsrcFound,
                        songsRecovered = rs.SongsRecovered, conflicts = rs.Conflicts,
                        titleFormatCandidates = rs.TitleFormatCandidates,
                        versionConflicts = rs.VersionConflicts, artistCreditCandidates = rs.ArtistCreditCandidates,
                        diagnosticRows = rs.Events.Count, candidateScoreRows = corroboration.Count,
                        phase = rs.Phase, error = rs.Error }); return;
                case "/api/lidarr/inventory/start":
                    EvidenceAudit compareAudit; List<LidarrArtist> compareArtists;
                    lock (verificationGate)
                    {
                        if (!connected || latestAudit == null) throw new InvalidOperationException("Connect to Lidarr and import/verify matching evidence first.");
                        if (pipelineActive || verification.Running || supplemental.Running || recovery.Running || inventory.Running)
                            throw new InvalidOperationException("Wait for the active lookup to finish.");
                        compareAudit = latestAudit;
                        compareArtists = lidarrArtists.ToList();
                        inventoryCancellation?.Dispose(); inventoryCancellation = new CancellationTokenSource();
                        inventory = new InventoryState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                            TotalSongs = compareAudit.Songs.Count, Phase = "Reading Lidarr inventory (read-only)..." };
                    }
                    var compareToken = inventoryCancellation.Token;
                    _ = Task.Run(() => RunLidarrComparisonAsync(compareAudit, compareArtists, compareToken));
                    await Respond(ctx, 200, new { started = true }); return;
                case "/api/lidarr/inventory/status":
                    InventoryState inv;
                    lock (verificationGate) inv = inventory.Copy();
                    await Respond(ctx, 200, new { running = inv.Running, done = inv.Done, failed = inv.Failed,
                        phase = inv.Phase, error = inv.Error, totalSongs = inv.TotalSongs,
                        already = inv.Already, toAdd = inv.ToAdd, review = inv.Review,
                        tracksRead = inv.TracksRead, filesRead = inv.FilesRead, albumsRead = inv.AlbumsRead,
                        artistsTotal = inv.ArtistsTotal, artistsChecked = inv.ArtistsChecked,
                        requestsFinished = inv.RequestsFinished, retries = inv.Retries,
                        sourceArtists = inv.SourceArtists, artistAlready = inv.ArtistAlready,
                        artistToAdd = inv.ArtistToAdd, artistReview = inv.ArtistReview,
                        groupedKnownRecording = inv.GroupedKnownRecording,
                        albumDisambiguated = inv.AlbumDisambiguated,
                        releaseChoicesPending = inv.ReleaseChoicesPending,
                        searchReady = inv.SearchReady, knownTrackMetadata = inv.KnownTrackMetadata,
                        manualConflicts = inv.ManualConflicts, groupApprovals = inv.GroupApprovals,
                        albumAliasesPending = inv.AlbumAliasesPending, albumOnlySearchTargets = inv.AlbumOnlySearchTargets,
                        albumReviewGroups = inv.AlbumGroups.Count,
                        diagnosticRows = inv.Events.Count }); return;
                case "/api/lidarr/inventory/items":
                    EvidenceAudit itemAudit;
                    InventoryDecision[] itemDecisions;
                    List<LidarrArtist> itemArtists;
                    Dictionary<string, ReviewBookmark> itemReviewChoices;
                    lock (verificationGate)
                    {
                        if (!inventory.Done || latestAudit == null)
                            throw new InvalidOperationException("Complete Lidarr comparison first.");
                        itemAudit = latestAudit;
                        itemDecisions = inventory.Decisions.ToArray();
                        itemArtists = lidarrArtists.ToList();
                        LoadReviewLedger();
                        itemReviewChoices = reviewLedger.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                    }
                    var itemRows = itemAudit.Songs.ToDictionary(s => s.Row);
                    var sourceArtistIds = itemArtists.Select(a => a.foreignArtistId).Where(ValidMbid)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    await Respond(ctx, 200, new {
                        items = itemDecisions.Select(d => {
                            var song = itemRows[d.Row];
                            var artistSource = ReviewArtistSource(song.Artist);
                            var songSource = ReviewSongSource(song);
                            return new { type = "song", row = d.Row, artist = song.Artist, title = song.Song,
                                album = song.Album, category = d.Category, reason = d.Reason,
                                selectedAlbum = d.LidarrAlbum, trackId = d.LidarrTrackId,
                                actionEligibility = d.ActionEligibility, metadataStatus = d.MetadataStatus,
                                artistReviewCandidateId = SelectedReviewCandidate(itemReviewChoices, "artist", artistSource),
                                songReviewCandidateId = SelectedReviewCandidate(itemReviewChoices, "song", songSource) };
                        }).Cast<object>().Concat(itemAudit.Artists.Select(a => {
                            var cat = a.Status != "high" || !ValidMbid(a.ArtistId) ? "Manual Review / Artists"
                                : sourceArtistIds.Contains(a.ArtistId) ? "Already in Library / Artists" : "To Be Added / Artists";
                            return (object)new { type = "artist", row = 0, artist = a.SourceName,
                                title = "", album = "", category = cat, reason = a.Reason,
                                selectedAlbum = "", trackId = "", actionEligibility = "not-yet-approved" };
                        })).ToArray()
                    }); return;
                case "/api/lidarr/inventory/groups":
                    AlbumReviewGroup[] reviewGroups;
                    lock (verificationGate)
                    {
                        if (!inventory.Done) throw new InvalidOperationException("Complete Lidarr comparison first.");
                        reviewGroups = inventory.AlbumGroups.ToArray();
                    }
                    await Respond(ctx, 200, new { groups = reviewGroups.Select(g => new {
                        id = g.Id, artist = g.Artist, sourceAlbum = g.SourceAlbum, songs = g.Songs,
                        recommendation = g.Recommendation, explanation = g.Explanation,
                        suggestedAlbumId = g.SuggestedAlbumId, approvedAlbumId = g.ApprovedAlbumId,
                        savedAlbumId = GetAlbumReviewBookmark(g)?.SelectedCandidate ?? "",
                        choices = g.Choices.Select(c => new { albumId = c.AlbumId, title = c.Title,
                            supportedRows = c.SupportedRows, complete = c.Complete,
                            relationship = c.TitleRelationship, explanation = c.Explanation }).ToArray()
                    }).ToArray() }); return;
                case "/api/lidarr/inventory/approve-group":
                {
                    // The request body was already read and parsed as `json` above.
                    // Re-reading Request.InputStream here would find an exhausted stream,
                    // while declaring another `body` would shadow the outer local.
                    var requestedGroupId = Value("groupId");
                    var requestedAlbumId = PropInt(json.RootElement, "albumId");
                    ApproveAlbumGroupSelection(requestedGroupId, requestedAlbumId);
                    lock (verificationGate)
                    {
                        var savedGroup = inventory.AlbumGroups.First(g => g.Id == requestedGroupId);
                        SaveAlbumReviewBookmark(savedGroup, requestedAlbumId);
                    }
                    await Respond(ctx, 200, new { planned = true, lidarrWrites = 0,
                        message = "Release selected for planning only. Final download/search approval is separate." });
                    return;
                }
                case "/api/review/queue":
                    await SendReviewQueue(ctx); return;
                case "/api/review/decision":
                    SaveReviewDecision(Value("key"), Value("disposition"), Value("candidateId"), Value("note"));
                    await Respond(ctx, 200, new { localOnly = true, lidarrWrites = 0, auditConfidenceChanged = false }); return;
                case "/api/review/decisions.csv":
                    await SendReviewDecisionsCsv(ctx); return;
                case "/api/review/reapply-album":
                    ReapplySavedAlbumReview(Value("groupId"));
                    await Respond(ctx, 200, new { localOnly = true, lidarrWrites = 0, auditConfidenceChanged = false }); return;
                case "/api/review/forget-album":
                    ForgetAlbumReview(Value("groupId"));
                    await Respond(ctx, 200, new { localOnly = true, lidarrWrites = 0 }); return;
                case "/api/lidarr/inventory/staging-queue":
                {
                    AlbumSearchStageItem[] items;
                    lock (verificationGate) items = GetStagingQueue().ToArray();
                    await Respond(ctx, 200, new { count = items.Length, staged = items.Count(x => x.Staged),
                        songsCovered = items.Sum(x => x.SafeSongs),
                        items = items.Select(x => new { albumId = x.AlbumId, artist = x.Artist, album = x.Album,
                            safeSongs = x.SafeSongs, otherUnverifiedSongs = x.OtherUnverifiedSongs,
                            sourceRows = x.SourceRows, staged = x.Staged }).ToArray() }); return;
                }
                case "/api/lidarr/inventory/stage-albums":
                {
                    if (!json.RootElement.TryGetProperty("albumIds", out var idsJson) ||
                        idsJson.ValueKind != JsonValueKind.Array || idsJson.GetArrayLength() > 100)
                        throw new InvalidOperationException("Select up to 100 proposed album IDs at a time.");
                    bool stage = json.RootElement.TryGetProperty("stage", out var stageJson) &&
                        stageJson.ValueKind == JsonValueKind.True;
                    var idsToChange = idsJson.EnumerateArray().Select(x =>
                        x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out var id) ? id : 0).ToArray();
                    var stagedCount = SetStagedAlbums(idsToChange, stage);
                    await Respond(ctx, 200, new { localOnly = true, lidarrWrites = 0,
                        stagedCount, message = "Only the local proposal queue changed. No Lidarr searches or downloads were started." }); return;
                }
                case "/api/lidarr/inventory/staging-queue.csv":
                    await SendStagingQueueCsv(ctx); return;
                case "/api/lidarr/inventory/manual-review.csv":
                    await SendManualReviewTriageCsv(ctx); return;
                case "/api/lidarr/inventory/album-groups.csv":
                    await SendAlbumGroupsCsv(ctx); return;
                case "/api/lidarr/inventory/action-plan.csv":
                    await SendActionPlanCsv(ctx); return;
                case "/api/lidarr/inventory/diagnostics.csv":
                    await SendInventoryDiagnosticsCsv(ctx); return;
                case "/api/lidarr/inventory/candidates.csv":
                    await SendLidarrCandidatesCsv(ctx); return;
                case "/api/migration/readiness.csv":
                    await SendMigrationReadinessCsv(ctx); return;
                case "/api/recovery/candidate-scores.csv":
                    await SendCandidateScoresCsv(ctx); return;
                case "/api/recovery/diagnostics.csv":
                    await SendRecoveryCsv(ctx); return;
                case "/api/audit/artists.csv": await SendAuditCsv(ctx, "artists"); return;
                case "/api/audit/songs.csv": await SendAuditCsv(ctx, "songs"); return;
                case "/api/audit/summary":
                    EvidenceAudit? a;
                    lock (verificationGate) a = latestAudit;
                    await Respond(ctx, 200, new { available = a != null,
                        artists = a?.Artists.Count ?? 0, artistHighConfidence = a?.Artists.Count(x => x.Status == "high") ?? 0,
                        artistsNeedingReview = a?.Artists.Count(x => x.Status != "high") ?? 0,
                        songs = a?.Songs.Count ?? 0, songHighConfidence = a?.Songs.Count(x => x.Status == "high") ?? 0,
                        songsNeedingReview = a?.Songs.Count(x => x.Status != "high") ?? 0, songIdentitySupported = a?.Songs.Count(x => x.SongIdentityStatus == "artist-and-song-supported") ?? 0,
                        externallySupported = a?.Songs.Count(x => x.ExternalStatus is "one-catalog-supports-title-and-artist" or "both-catalogs-support-title-and-artist") ?? 0,
                        artistsNeedingReviewWithExternalHits = a?.Artists.Count(ar => ar.Status != "high" && a.Songs.Any(s => s.Artist.Equals(ar.SourceName, StringComparison.OrdinalIgnoreCase) && (s.ExternalStatus is "one-catalog-supports-title-and-artist" or "both-catalogs-support-title-and-artist"))) ?? 0,
                        catalogCoverage = "Partial. MusicBrainz-wide artist catalog is not enumerated; artist-wide extra-song totals cannot be inferred from recording search results." }); return;
                case "/api/verify/status":
                    VerifyState snapshot;
                    lock (verificationGate) snapshot = verification.Copy();
                    await Respond(ctx, 200, new { started = snapshot.Started, running = snapshot.Running, importedAuditOnly = snapshot.ImportedAuditOnly, elapsedSeconds = snapshot.StartedAt == DateTimeOffset.MinValue ? 0 : Math.Max(0, (int)(DateTimeOffset.UtcNow - snapshot.StartedAt).TotalSeconds), completed = snapshot.Completed, failed = snapshot.Failed, cancelled = snapshot.Cancelled, checkedCount = snapshot.Checked, total = snapshot.Total, totalArtists = snapshot.TotalArtists, resolvedArtists = snapshot.ResolvedArtists, verifiedRecordings = snapshot.VerifiedRecordings, ambiguous = snapshot.Ambiguous, unmatched = snapshot.Unmatched, lookupFailures = snapshot.LookupFailures, requests = snapshot.Requests, firstPassSeconds = snapshot.FirstPassSeconds, secondPassDone = snapshot.ProcessedSecondPass, secondPassTotal = snapshot.SecondPassTotal, focusedDone = snapshot.FocusedDone, focusedTotal = snapshot.FocusedTotal, batchesDone = snapshot.BatchesCompleted, batchesTotal = snapshot.TotalBatches, retries = snapshot.Retries, httpErrors = snapshot.HttpErrors, artistHighConfidence = snapshot.ArtistHighConfidence, artistNeedsReview = snapshot.ArtistNeedsReview, songHighConfidence = snapshot.SongHighConfidence, songNeedsReview = snapshot.SongNeedsReview, songIdentitySupported = snapshot.SongIdentitySupported, phase = snapshot.Phase, errorDetail = snapshot.Error }); return;
                default: await Respond(ctx, 404, new { error = "Unknown API action." }); return;
            }
        }
        catch (Exception ex) { Log("API error " + path + ": " + ex.Message); await Respond(ctx, 400, new { error = ex.Message }); }
    }

    // Treat "&" and "and" as equivalent and ignore accent marks. Never use
    // name similarity alone to establish a MusicBrainz artist/recording ID.
    static string Normalize(string name)
    {
        var decomposed = (name ?? "").Replace("&", " and ").Normalize(NormalizationForm.FormD);
        decomposed = Regex.Replace(decomposed, @"\p{Mn}", "");
        return Regex.Replace(decomposed.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
    }
    static string ValidateLidarr(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || !new[] { "http", "https" }.Contains(uri.Scheme) || uri.UserInfo.Length > 0 || uri.Host == "0.0.0.0" || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("Invalid Lidarr URL. Enter only the http(s) origin, such as http://127.0.0.1:8686.");
        // Limit API-key destinations to explicitly loopback hosts in the diagnostic build.
        if (uri.Host != "127.0.0.1" && uri.Host != "localhost" && uri.Host != "[::1]" && uri.Host != "::1")
            throw new InvalidOperationException("This diagnostic build only connects to Lidarr on the same computer. Remote servers will be supported after host validation is implemented.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    static bool ContainsMusicCsv(string path)
    {
        try { using var zip = ZipFile.OpenRead(path); return zip.Entries.Any(e => e.FullName.EndsWith("music library songs.csv", StringComparison.OrdinalIgnoreCase)); } catch { return false; }
    }
    static List<Track> ParseMusicZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("music library songs.csv", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The archive does not contain music library songs.csv.");
        if (entry.Length > 60_000_000) throw new InvalidOperationException("Song metadata is unexpectedly large. Please review the archive.");
        using var stream = entry.Open(); using var reader = new StreamReader(stream, Encoding.UTF8);
        var header = CsvRow(reader) ?? throw new InvalidOperationException("Empty music CSV.");
        int title = Array.FindIndex(header, x => x.Trim().Equals("Song Title", StringComparison.OrdinalIgnoreCase));
        int artist = Array.FindIndex(header, x => x.Trim().Equals("Artist Name 1", StringComparison.OrdinalIgnoreCase));
        var artistColumns = header.Select((name, index) => (name: name.Trim(), index))
            .Where(x => Regex.IsMatch(x.name, @"^Artist Name \d+$", RegexOptions.IgnoreCase))
            .OrderBy(x => int.Parse(Regex.Match(x.name, @"\d+$").Value))
            .Select(x => x.index).ToArray();
        int video = Array.FindIndex(header, x => x.Trim().Equals("Video ID", StringComparison.OrdinalIgnoreCase));
        int album = Array.FindIndex(header, x => x.Trim().Equals("Album Title", StringComparison.OrdinalIgnoreCase));
        if (title < 0 || artist < 0) throw new InvalidOperationException("Unrecognized song CSV headers.");
        var result = new List<Track>(); string[]? row;
        while ((row = CsvRow(reader)) != null)
        {
            string Get(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";
            var song = Get(title); if (song.Length == 0) continue;
            var id = Get(video);
            var url = Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? "https://www.youtube.com/watch?v=" + id : "";
            var credits = artistColumns.Select(Get).Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            result.Add(new Track(Get(artist), song, url, Get(album), credits));
            if (result.Count > 250_000) throw new InvalidOperationException("Too many songs in archive.");
        }
        if (result.Count == 0) throw new InvalidOperationException("No songs found.");
        return result;
    }
    // Basic RFC-4180 style CSV parser; handles commas, quotes and newlines in quoted fields.
    static string[]? CsvRow(TextReader r)
    {
        var fields = new List<string>(); var value = new StringBuilder(); bool quoted = false; bool started = false;
        while (true)
        {
            var next = r.Read();
            if (next < 0) { if (!started && value.Length == 0 && fields.Count == 0) return null; fields.Add(value.ToString()); return fields.ToArray(); }
            var c = (char)next; started = true;
            if (quoted) { if (c == '"') { if (r.Peek() == '"') { r.Read(); value.Append('"'); } else quoted = false; } else value.Append(c); }
            else if (c == '"' && value.Length == 0) quoted = true;
            else if (c == ',') { fields.Add(value.ToString()); value.Clear(); }
            else if (c == '\n') { fields.Add(value.ToString().TrimEnd('\r')); return fields.ToArray(); }
            else value.Append(c);
        }
    }
    async Task Respond(HttpListenerContext ctx, int status, object result)
    {
        if (!ctx.Response.OutputStream.CanWrite) return;
        var json = JsonSerializer.Serialize(result);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
    void Log(string message) { try { File.AppendAllText(Path.Combine(logDir, "desktop.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); } catch { } }
    record Track(string Artist, string Song, string YouTubeUrl, string Album, string[] ArtistCredits);
    record LidarrArtist(string artistName, string foreignArtistId, int Id = 0);
}
