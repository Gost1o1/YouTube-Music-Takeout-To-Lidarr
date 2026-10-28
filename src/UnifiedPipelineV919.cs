using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

sealed partial class DesktopServer
{
    // Fresh, process-local evidence cache. Never saved to disk or carried into another scan.
    // A cached response is raw evidence, not a verified identity or approval.
    readonly RequestPacer sharedMbPacer = new();
    readonly object mbCacheGate = new();
    readonly Dictionary<string, string> mbResponseCache = new(StringComparer.Ordinal);
    const int MaxCacheEntries = 6000;
    const long MaxCacheBytes = 80_000_000;
    long mbCacheBytes;
    int mbCacheHits;
    readonly object pipelineGate = new();
    volatile bool pipelineActive;
    bool pipelineFinished;
    bool pipelineFailed;
    string pipelineError = "";
    string pipelinePhase = "idle";
    DateTimeOffset pipelineStarted;

    void ResetUnifiedCache()
    {
        lock (mbCacheGate) { mbResponseCache.Clear(); mbCacheBytes = 0; mbCacheHits = 0; }
    }
    void IncrementUnifiedCacheHit() { lock (mbCacheGate) mbCacheHits++; }
    static bool IsMusicBrainzUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" &&
        u.Host.Equals("musicbrainz.org", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.StartsWith("/ws/2/", StringComparison.Ordinal);

    async Task<(string? Body, int Status, long Elapsed)> UnifiedMusicBrainzGetAsync(
        HttpClient client, string url, RequestPacer pacer, CancellationToken ct)
    {
        if (!IsMusicBrainzUrl(url)) throw new InvalidOperationException("Unexpected MusicBrainz endpoint.");
        lock (mbCacheGate)
        {
            if (mbResponseCache.TryGetValue(url, out var cached))
            {
                mbCacheHits++;
                return (cached, 200, 0);
            }
        }
        var watch = Stopwatch.StartNew();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await pacer.Wait(ct);
            try
            {
                using var reply = await client.GetAsync(url, ct);
                var status = (int)reply.StatusCode;
                lock (verificationGate)
                {
                    verification.Requests++;
                    if (status == 429 || status >= 500) verification.HttpErrors++;
                }
                if (status == 429 || status >= 500)
                {
                    if (attempt == 3) return (null, status, watch.ElapsedMilliseconds);
                    lock (verificationGate) verification.Retries++;
                    // Respect server instructions, with bounded backoff. Every attempt also
                    // passes through the same global MusicBrainz rate limiter.
                    var retryAfter = reply.Headers.RetryAfter;
                    var wait = retryAfter?.Delta ??
                        (retryAfter?.Date is DateTimeOffset until
                            ? until - DateTimeOffset.UtcNow
                            : TimeSpan.FromSeconds(Math.Min(24, 3 * (1 << attempt))));
                    if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                    if (wait > TimeSpan.FromMinutes(2)) wait = TimeSpan.FromMinutes(2);
                    await Task.Delay(wait, ct);
                    continue;
                }
                if (!reply.IsSuccessStatusCode) return (null, status, watch.ElapsedMilliseconds);
                var body = await reply.Content.ReadAsStringAsync(ct);
                if (body.Length > 4_000_000) return (null, 413, watch.ElapsedMilliseconds);
                lock (mbCacheGate)
                {
                    var byteCount = body.Length * 2L;
                    if (byteCount < 300_000 && mbResponseCache.Count < MaxCacheEntries && mbCacheBytes + byteCount <= MaxCacheBytes
                        && mbResponseCache.TryAdd(url, body))
                    {
                        mbCacheBytes += byteCount;
                    }
                }
                return (body, status, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < 2) { lock (verificationGate) verification.Retries++; await Task.Delay(TimeSpan.FromSeconds(2 + attempt * 2), ct); }
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 2) Log("MusicBrainz network error: " + ex.Message);
                else { lock (verificationGate) verification.Retries++; await Task.Delay(TimeSpan.FromSeconds(2 + attempt * 2), ct); }
            }
        }
        return (null, 0, watch.ElapsedMilliseconds);
    }

    void BeginUnifiedPipeline(Track[] source)
    {
        if (!connected || source.Length == 0) throw new InvalidOperationException("Connect Lidarr and prepare the Takeout library first.");
        lock (pipelineGate)
        {
            if (pipelineActive) throw new InvalidOperationException("The automatic library verification is already running.");
            lock (verificationGate)
            {
                if (verification.Running || supplemental.Running || recovery.Running || inventory.Running)
                    throw new InvalidOperationException("Wait until the previous library operation completes.");
                verificationCancellation?.Cancel();
                verificationCancellation?.Dispose();
                verificationCancellation = new CancellationTokenSource();
                ResetUnifiedCache();
                latestAudit = null;
                supplemental = new SupplementalState();
                recovery = new IdentifierRecoveryState();
                corroboration.Clear();
                ResetLidarrComparison();
                verification = new VerifyState
                {
                    Started = true, Running = true, StartedAt = DateTimeOffset.UtcNow,
                    Total = source.Length,
                    TotalArtists = source.Select(t => t.Artist.Trim()).Where(x => x.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Phase = "Verifying songs"
                };
                pipelineActive = true; pipelineFinished = false; pipelineFailed = false;
                pipelineError = ""; pipelinePhase = "verification"; pipelineStarted = DateTimeOffset.UtcNow;
            }
        }
        var token = verificationCancellation!.Token;
        _ = Task.Run(() => RunUnifiedPipelineAsync(source, token));
    }

    async Task RunUnifiedPipelineAsync(Track[] source, CancellationToken ct)
    {
        try
        {
            await VerifyTracksAsync(source, ct);
            ct.ThrowIfCancellationRequested();
            EvidenceAudit audit;
            lock (verificationGate)
            {
                if (verification.Failed || verification.Cancelled || latestAudit == null)
                    throw new InvalidOperationException("MusicBrainz verification did not produce an audit. " + verification.Error);
                audit = latestAudit;
                supplemental = new SupplementalState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                    Total = audit.Songs.Count(x => x.Status is "ambiguous" or "unmatched"),
                    Phase = "Cross-checking external catalogs" };
            }
            lock (pipelineGate) pipelinePhase = "catalogs";
            await RunSupplementalAsync(source, audit, ct);
            ct.ThrowIfCancellationRequested();
            lock (verificationGate)
            {
                if (supplemental.Failed) throw new InvalidOperationException("Catalog comparison could not complete. " + supplemental.Error);
                recovery = new IdentifierRecoveryState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                    Total = audit.Artists.Count(a => a.Status != "high") +
                        audit.Songs.Count(a => a.DeezerTrackId.Length > 0 && a.Status != "high"),
                    Phase = "Reconciling recording and artist evidence" };
            }
            lock (pipelineGate) pipelinePhase = "identifiers";
            await RunIdentifierRecoveryAsync(source, audit, ct);
            ct.ThrowIfCancellationRequested();
            lock (verificationGate)
            {
                if (recovery.Failed) throw new InvalidOperationException("Identifier reconciliation could not complete. " + recovery.Error);
                inventory = new InventoryState { Running = true, StartedAt = DateTimeOffset.UtcNow,
                    TotalSongs = audit.Songs.Count, Phase = "Comparing against Lidarr (read-only)" };
            }
            lock (pipelineGate) pipelinePhase = "lidarr";
            await RunLidarrComparisonAsync(audit, lidarrArtists.ToList(), ct);
            ct.ThrowIfCancellationRequested();
            lock (verificationGate)
                if (inventory.Failed || !inventory.Done)
                    throw new InvalidOperationException("Lidarr inventory comparison could not complete. " + inventory.Error);
            lock (pipelineGate) { pipelineActive = false; pipelineFinished = true; pipelinePhase = "complete"; }
            Log("Unified pipeline complete: fresh matching, external evidence, identifiers and Lidarr inventory.");
        }
        catch (Exception ex)
        {
            lock (pipelineGate)
            {
                pipelineActive = false; pipelineFailed = true;
                pipelinePhase = "error"; pipelineError = ex is OperationCanceledException ? "Operation cancelled." : ex.Message;
            }
            Log("Unified verification pipeline stopped: " + ex);
        }
    }

    object UnifiedPipelineStatus()
    {
        bool active, finished, failed; string phase, error; DateTimeOffset started;
        lock (pipelineGate)
        {
            active = pipelineActive; finished = pipelineFinished; failed = pipelineFailed;
            phase = pipelinePhase; error = pipelineError; started = pipelineStarted;
        }
        VerifyState v; SupplementalState s; IdentifierRecoveryState r; InventoryState i;
        lock (verificationGate) { v = verification.Copy(); s = supplemental.Copy(); r = recovery.Copy(); i = inventory.Copy(); }
        var progress = phase switch
        {
            "verification" => 68.0 * Math.Clamp(v.Checked / (double)Math.Max(1, v.Total), 0, 1),
            "catalogs" => 68 + 13.0 * Math.Clamp(s.Checked / (double)Math.Max(1, s.Total), 0, 1),
            "identifiers" => 81 + 11.0 * Math.Clamp(r.Checked / (double)Math.Max(1, r.Total), 0, 1),
            "lidarr" => 92 + 7.5 * Math.Clamp(i.ArtistsChecked / (double)Math.Max(1, i.ArtistsTotal), 0, 1),
            "complete" => 100.0,
            "error" => 0.0,
            _ => 0.0
        };
        int hits; int entries;
        lock (mbCacheGate) { hits = mbCacheHits; entries = mbResponseCache.Count; }
        var elapsed = started == default ? 0 : Math.Max(0, (int)(DateTimeOffset.UtcNow - started).TotalSeconds);
        return new { running = active, done = finished, failed,
            error, phase, progressPercent = Math.Min(100, Math.Round(progress, 1)),
            elapsedSeconds = elapsed, songsChecked = v.Checked, songsTotal = v.Total,
            totalArtists = v.TotalArtists, artistsMatched = auditArtistHighCount(),
            requests = v.Requests, retries = v.Retries, cacheHits = hits, cachedResponses = entries,
            catalogDone = s.Checked, catalogTotal = s.Total,
            recoveryDone = r.Checked, recoveryTotal = r.Total,
            lidarrDone = i.ArtistsChecked, lidarrTotal = i.ArtistsTotal,
            proposedSongs = i.ToAdd, needsReview = i.Review,
            speed = v.Checked > 0 && elapsed > 0 ? v.Checked * 60.0 / elapsed : 0
        };
    }
    int auditArtistHighCount()
    {
        lock (verificationGate) return latestAudit?.Artists.Count(a => a.Status == "high") ?? verification.ResolvedArtists;
    }

    sealed class FixtureMusicBrainzHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"recordings\":[]}") });
        }
    }
    static void UnifiedPipelineSelfTests()
    {
        if (MaxCacheEntries < 100 || MaxCacheBytes < 1_000_000) throw new Exception("Unreasonable MusicBrainz cache limits.");
        if (!IsMusicBrainzUrl("https://musicbrainz.org/ws/2/recording/?query=foo"))
            throw new Exception("MusicBrainz URL validation regression.");
        if (IsMusicBrainzUrl("https://example.org/ws/2/recording"))
            throw new Exception("MusicBrainz URL validation accepted another domain.");
        // Offline HTTP fixture: a second matching request must not hit the network.
        var worker = new DesktopServer();
        var handler = new FixtureMusicBrainzHandler();
        using (var client = new HttpClient(handler))
        {
            const string url = "https://musicbrainz.org/ws/2/recording/?query=smoke&fmt=json&limit=50";
            var first = worker.UnifiedMusicBrainzGetAsync(client, url, worker.sharedMbPacer, CancellationToken.None)
                .GetAwaiter().GetResult();
            var second = worker.UnifiedMusicBrainzGetAsync(client, url, worker.sharedMbPacer, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (handler.Calls != 1 || first.Status != 200 || first.Body != second.Body || worker.mbCacheHits != 1)
                throw new Exception("In-run MusicBrainz response caching regression.");
        }
        Console.WriteLine("PASS: v0.9.1.9 unified pipeline, scoped cache limits and MusicBrainz URL safety");
    }
}
