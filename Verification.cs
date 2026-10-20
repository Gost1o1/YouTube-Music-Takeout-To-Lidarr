using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

sealed partial class DesktopServer
{
    sealed class VerifyState
    {
        public bool Started, Running, Completed, Failed, Cancelled;
        public int Checked, Total, TotalArtists, ResolvedArtists, VerifiedRecordings, Ambiguous, Unmatched, LookupFailures, Requests;
        public int ProcessedSecondPass, SecondPassTotal, BatchesCompleted, TotalBatches, Retries, HttpErrors;
        public int AuditedArtists, ArtistHighConfidence, ArtistNeedsReview, SongHighConfidence, SongNeedsReview;
        public string Phase = "Not started", Error = "";
        public VerifyState Copy() => (VerifyState)MemberwiseClone();
    }
    sealed class MatchResult
    {
        public bool RequestSucceeded, Verified, Ambiguous;
        public string ArtistId = "", RecordingId = "", Reason = "";
        public List<string> CandidateArtistIds = new(), CandidateRecordings = new(), CandidateAlbums = new(), ObservedCreditNames = new();
        public int ArtistCandidateCount, RecordingCandidateCount;
        public bool ExactTitle, ExactArtist, AlbumMatch, CrossSongSupport, MultipleRecordingVersions;
        public int Confidence;
    }
    sealed class Candidate
    {
        public string RecordingId = "", Title = "";
        public List<string> Albums = new();
        public int SearchScore;
        public HashSet<string> ArtistIds = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Names = new();
        public Dictionary<string, string> Credits = new(StringComparer.OrdinalIgnoreCase);
    }
    sealed class RequestPacer
    {
        public DateTimeOffset Last = DateTimeOffset.MinValue;
        public async Task Wait(CancellationToken ct)
        {
            var gap = TimeSpan.FromMilliseconds(1200) - (DateTimeOffset.UtcNow - Last);
            if (gap > TimeSpan.Zero) await Task.Delay(gap, ct);
            Last = DateTimeOffset.UtcNow;
        }
    }
    // These are per-run evidence only; no persisted responses, matching decisions, or resumption cache.
    async Task VerifyTracksAsync(Track[] source, CancellationToken ct)
    {
        const int batchSize = 100;
        var results = new MatchResult[source.Length];
        var pacer = new RequestPacer();
        var artistEvidence = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var artistIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<int>();
        var sourceSnapshot = source.ToArray();
        try
        {
            int batches = (source.Length + batchSize - 1) / batchSize;
            lock (verificationGate) { verification.TotalBatches = batches; verification.Phase = "Starting first verification pass"; }
            for (int start = 0; start < source.Length; start += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                int end = Math.Min(source.Length, start + batchSize);
                using var mb = CreateMbClient();
                for (int i = start; i < end; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var track = source[i];
                    lock (verificationGate) verification.Phase = $"First pass: batch {start / batchSize + 1}/{batches}, recording {i + 1}/{source.Length}";
                    // Query both title and artist, rather than stopping at multiple versions of a common title.
                    var item = await MatchAsync(mb, pacer, track, null, false, ct);
                    results[i] = item;
                    if (item.Verified) AddEvidence(artistEvidence, track.Artist, item.ArtistId, item.Confidence);
                    else if (item.RequestSucceeded) unresolved.Add(i);
                    lock (verificationGate) { verification.Checked++; UpdateCounts(results, artistEvidence); }
                }
                lock (verificationGate) { verification.BatchesCompleted++; verification.Phase = $"Finished first-pass batch {verification.BatchesCompleted}/{batches}"; }
                Log($"MusicBrainz first pass: batch {start / batchSize + 1}/{batches}, processed {end}/{source.Length}, verified {results.Count(x => x?.Verified == true)}, unresolved {unresolved.Count}");
            }
            // Confirm artist identity only when one candidate has clearly stronger independent song evidence.
            foreach (var (artist, ids) in artistEvidence)
            {
                var ranked = ids.OrderByDescending(x => x.Value).ToArray();
                if (ranked.Length > 0 && ranked[0].Value >= 100 && (ranked.Length == 1 || ranked[0].Value >= ranked[1].Value * 2))
                    artistIds[artist] = ranked[0].Key;
            }
            lock (verificationGate) { verification.SecondPassTotal = unresolved.Count; verification.Phase = $"Starting artist-corroboration pass for {unresolved.Count} unresolved recordings"; }
            // A new HttpClient per batch renews the underlying connections. All MusicBrainz calls still share one pacer.
            int secondBatches = (unresolved.Count + batchSize - 1) / batchSize;
            for (int start = 0; start < unresolved.Count; start += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                using var mb = CreateMbClient();
                for (int j = start; j < Math.Min(start + batchSize, unresolved.Count); j++)
                {
                    ct.ThrowIfCancellationRequested();
                    int i = unresolved[j];
                    var track = source[i];
                    lock (verificationGate) verification.Phase = $"Second pass: batch {start / batchSize + 1}/{Math.Max(1, secondBatches)}, recording {j + 1}/{unresolved.Count}";
                    artistIds.TryGetValue(track.Artist, out var knownId);
                    var second = await MatchAsync(mb, pacer, track, knownId, true, ct);
                    // Never overwrite a successful earlier result with a failed secondary lookup.
                    if (second.RequestSucceeded && (second.Verified || !results[i].RequestSucceeded))
                    {
                        results[i] = second;
                        if (second.Verified) AddEvidence(artistEvidence, track.Artist, second.ArtistId, second.Confidence);
                    }
                    lock (verificationGate) { verification.ProcessedSecondPass++; UpdateCounts(results, artistEvidence); }
                }
                Log($"MusicBrainz second pass: batch {start / batchSize + 1}/{secondBatches}, processed {Math.Min(start + batchSize, unresolved.Count)}/{unresolved.Count}");
            }
            // Retry transient failures from BOTH passes once more, without demanding that successful records be re-queried.
            var failedIndices = Enumerable.Range(0, results.Length).Where(i => !results[i].RequestSucceeded).ToArray();
            if (failedIndices.Length > 0)
            {
                lock (verificationGate) verification.Phase = $"Retrying {failedIndices.Length} failed lookups";
                for (int start = 0; start < failedIndices.Length; start += batchSize)
                {
                    using var mb = CreateMbClient();
                    foreach (var i in failedIndices.Skip(start).Take(batchSize))
                    {
                        ct.ThrowIfCancellationRequested();
                        artistIds.TryGetValue(source[i].Artist, out var knownId);
                        var retry = await MatchAsync(mb, pacer, source[i], knownId, true, ct);
                        if (retry.RequestSucceeded) results[i] = retry;
                        lock (verificationGate) UpdateCounts(results, artistEvidence);
                    }
                }
            }
            lock (verificationGate) verification.Phase = "Auditing match evidence and comparing artist/song coverage";
            // Audit must not silently upgrade one song hit into proof of an entire artist catalog.
            var audit = BuildEvidenceAudit(sourceSnapshot, results, artistEvidence);
            lock (verificationGate)
            {
                latestAudit = audit;
                verification.AuditedArtists = audit.Artists.Count;
                verification.ArtistHighConfidence = audit.Artists.Count(a => a.Status == "high");
                verification.ArtistNeedsReview = audit.Artists.Count(a => a.Status != "high");
                verification.SongHighConfidence = audit.Songs.Count(x => x.Status == "high");
                verification.SongNeedsReview = audit.Songs.Count(x => x.Status != "high");
                UpdateCounts(results, artistEvidence);
                verification.Running = false;
                verification.Completed = verification.Checked == verification.Total && verification.LookupFailures == 0;
                verification.Phase = verification.Completed
                    ? "All verification passes completed. Automatic matches are evidence-based; unresolved recordings still require review. No Lidarr changes were made."
                    : $"Completed available passes with {verification.LookupFailures} unsuccessful requests. Verified matches retained for this run; retry unsuccessful records in a new scan.";
            }
            Log($"Evidence audit: artists_high={audit.Artists.Count(a=>a.Status=="high")} artists_review={audit.Artists.Count(a=>a.Status!="high")} songs_high={audit.Songs.Count(x=>x.Status=="high")} songs_review={audit.Songs.Count(x=>x.Status!="high")}");
            Log($"MusicBrainz final: checked={source.Length} verified={results.Count(x => x.Verified)} ambiguous={results.Count(x => x.RequestSucceeded && x.Ambiguous)} unmatched={results.Count(x => x.RequestSucceeded && !x.Ambiguous && !x.Verified)} failures={results.Count(x => !x.RequestSucceeded)} requests={verification.Requests}");
        }
        catch (OperationCanceledException)
        {
            lock (verificationGate) { verification.Running = false; verification.Cancelled = true; verification.Phase = "Verification cancelled; results incomplete."; }
        }
        catch (Exception ex)
        {
            Log("MusicBrainz worker exception: " + ex);
            lock (verificationGate) { verification.Running = false; verification.Failed = true; verification.Error = ex.Message; verification.Phase = "Verification failed unexpectedly. See desktop.log; no Lidarr changes were made."; }
        }
    }
    static HttpClient CreateMbClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeMusicTakeoutToLidarr/0.8.4 (https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
    async Task<MatchResult> MatchAsync(HttpClient mb, RequestPacer pacer, Track track, string? corroboratedArtistId, bool secondPass, CancellationToken ct)
    {
        var title = track.Song.Trim();
        var artist = track.Artist.Trim();
        string artistSegment = artist.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? artist;
        var firstQuery = $"recording:\"{EscapeMb(title)}\" AND artist:\"{EscapeMb(artistSegment)}\"";
        var broadQuery = $"recording:\"{EscapeMb(title)}\"";
        // First pass: artist-aware query, with fallback to title only. Second pass: wider title results, supported by cross-song evidence.
        string[] searches = secondPass ? new[] { broadQuery } : new[] { firstQuery, broadQuery };
        bool anySuccess = false;
        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in searches)
        {
            var found = await SearchMbAsync(mb, pacer, query, secondPass ? 50 : 25, ct);
            if (found is null) continue;
            anySuccess = true;
            foreach (var item in found)
            {
                if (item.RecordingId.Length > 0 && (!candidates.TryGetValue(item.RecordingId, out var previous) || item.SearchScore > previous.SearchScore))
                    candidates[item.RecordingId] = item;
            }
            // No unnecessary second call when a title + artist query already returned credible credits.
            if (!secondPass && candidates.Values.Any(x => x.SearchScore >= 85 && x.Names.Any(n => ArtistCreditMatches(artist, n)))) break;
        }
        if (!anySuccess) return new MatchResult { Reason = "All lookups failed" };
        var scored = new List<(Candidate candidate, int score, string id)>();
        foreach (var c in candidates.Values)
        {
            int similarity = TitleSimilarity(title, c.Title);
            if (similarity < 85) continue;
            bool credit = c.Credits.Any(kv => ArtistCreditMatches(artist, kv.Value));
            bool corroborated = corroboratedArtistId is not null && c.ArtistIds.Contains(corroboratedArtistId);
            if (!credit && !corroborated) continue;
            bool album = track.Album.Length > 0 && c.Albums.Any(a => Normalize(a) == Normalize(track.Album));
            int score = Math.Min(similarity, c.SearchScore) + (credit ? 20 : 0) + (corroborated ? 30 : 0) + (album ? 15 : 0);
            string id = corroborated ? corroboratedArtistId! : c.Credits.FirstOrDefault(kv => ArtistCreditMatches(artist, kv.Value)).Key ?? "";
            if (id.Length > 0) scored.Add((c, score, id));
        }
        var sorted = scored.OrderByDescending(x => x.score).ToArray();
        if (sorted.Length == 0) return new MatchResult { RequestSucceeded = true, Ambiguous = candidates.Count > 0, Reason = candidates.Count == 0 ? "No recording returned for title" : "Artist credits differ or version/title conflict", CandidateRecordings = candidates.Values.Take(8).Select(x => x.RecordingId).ToList(), CandidateArtistIds = candidates.Values.SelectMany(x => x.ArtistIds).Distinct().Take(8).ToList(), ObservedCreditNames = candidates.Values.SelectMany(x => x.Names).Distinct().Take(8).ToList(), RecordingCandidateCount = candidates.Count };
        var top = sorted[0];
        // Multiple releases/recording MBIDs for the same song are common. Group by credited artist ID instead of rejecting them all.
        var groups = sorted.GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Id = g.Key, Score = g.Max(x => x.score), Count = g.Count(), Best = g.OrderByDescending(x => x.score).First() })
            .OrderByDescending(g => g.Score).ToArray();
        var best = groups[0];
        bool uniqueArtist = groups.Length == 1 || best.Score >= groups[1].Score + 20;
        bool confident = best.Score >= 105 && uniqueArtist;
        return new MatchResult
        {
            RequestSucceeded = true, Verified = confident, Ambiguous = !confident,
            ArtistId = confident ? best.Id : "", RecordingId = confident ? best.Best.candidate.RecordingId : "",
            Confidence = best.Score, ExactTitle = TitleSimilarity(title, best.Best.candidate.Title) == 100,
            ExactArtist = best.Best.candidate.Credits.Any(kv => ArtistCreditMatches(artist, kv.Value)),
            CrossSongSupport = corroboratedArtistId != null && best.Id == corroboratedArtistId,
            AlbumMatch = track.Album.Length > 0 && best.Best.candidate.Albums.Any(a => Normalize(a) == Normalize(track.Album)),
            MultipleRecordingVersions = best.Count > 1,
            ArtistCandidateCount = groups.Length, RecordingCandidateCount = sorted.Length,
            CandidateArtistIds = groups.Take(8).Select(g => g.Id).ToList(),
            CandidateRecordings = sorted.Take(8).Select(g => g.candidate.RecordingId).ToList(),
            CandidateAlbums = best.Best.candidate.Albums.Take(8).ToList(),
            ObservedCreditNames = best.Best.candidate.Names.Take(8).ToList(),
            Reason = confident ? "Title and artist-credit evidence; alternate recordings grouped by artist" : "Competing artist identity or insufficient evidence"
        };
    }
    async Task<List<Candidate>?> SearchMbAsync(HttpClient client, RequestPacer pacer, string query, int limit, CancellationToken ct)
    {
        string url = "https://musicbrainz.org/ws/2/recording/?query=" + Uri.EscapeDataString(query) + "&fmt=json&limit=" + limit;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await pacer.Wait(ct);
            try
            {
                using var response = await client.GetAsync(url, ct);
                lock (verificationGate) verification.Requests++;
                if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                {
                    lock (verificationGate) verification.HttpErrors++;
                    if (attempt < 3) { lock (verificationGate) verification.Retries++; await Task.Delay(TimeSpan.FromSeconds((attempt + 1) * (attempt + 1) * 3), ct); continue; }
                    Log($"MusicBrainz HTTP {(int)response.StatusCode} after retries for query: {query}"); return null;
                }
                if (!response.IsSuccessStatusCode) { Log($"MusicBrainz HTTP {(int)response.StatusCode} for query: {query}"); return null; }
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var found = new List<Candidate>();
                if (!doc.RootElement.TryGetProperty("recordings", out var recs) || recs.ValueKind != JsonValueKind.Array) return found;
                foreach (var rec in recs.EnumerateArray())
                {
                    var c = new Candidate
                    {
                        RecordingId = rec.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Title = rec.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        SearchScore = rec.TryGetProperty("score", out var s) && s.TryGetInt32(out int x) ? x : 0
                    };
                    if (rec.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
                        foreach (var credit in credits.EnumerateArray())
                        {
                            if (credit.ValueKind != JsonValueKind.Object || !credit.TryGetProperty("artist", out var artist)) continue;
                            var name = artist.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            var aid = artist.TryGetProperty("id", out var a) ? a.GetString() ?? "" : "";
                            if (name.Length > 0) c.Names.Add(name);
                            if (aid.Length > 0) { c.ArtistIds.Add(aid); c.Credits[aid] = name; }
                        }
                    if ((rec.TryGetProperty("release-list", out var releases) || rec.TryGetProperty("releases", out releases)) && releases.ValueKind == JsonValueKind.Array)
                        foreach (var release in releases.EnumerateArray())
                            if (release.TryGetProperty("title", out var albumTitle) && albumTitle.ValueKind == JsonValueKind.String)
                                c.Albums.Add(albumTitle.GetString() ?? "");
                    found.Add(c);
                }
                return found;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { if (attempt < 3) { lock (verificationGate) verification.Retries++; await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct); } }
            catch (HttpRequestException ex)
            { if (attempt == 3) Log("MusicBrainz network error: " + ex.Message); else { lock (verificationGate) verification.Retries++; await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct); } }
            catch (JsonException ex) { Log("MusicBrainz invalid JSON: " + ex.Message); return null; }
        }
        return null;
    }
    static int TitleSimilarity(string a, string b)
    {
        string x = Normalize(a), y = Normalize(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 100;
        // Do not silently equate live, remix, acoustic and studio recordings.
        if (x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal)) return 75;
        return 0;
    }
    static string EscapeMb(string text) => Regex.Replace(text, "[\\\\\"()]", " ").Trim();
    static bool ArtistCreditMatches(string takeout, string credit)
    {
        var a = Normalize(takeout); var b = Normalize(credit);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        return takeout.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => Normalize(part) == b);
    }
    static void AddEvidence(Dictionary<string, Dictionary<string, int>> evidence, string artist, string id, int confidence)
    {
        if (artist.Length == 0 || id.Length == 0) return;
        if (!evidence.TryGetValue(artist, out var ids)) evidence[artist] = ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ids[id] = ids.GetValueOrDefault(id) + confidence;
    }
    void UpdateCounts(MatchResult[] results, Dictionary<string, Dictionary<string, int>> evidence)
    {
        verification.VerifiedRecordings = results.Count(x => x is not null && x.Verified);
        verification.Ambiguous = results.Count(x => x is not null && x.RequestSucceeded && x.Ambiguous);
        verification.Unmatched = results.Count(x => x is not null && x.RequestSucceeded && !x.Ambiguous && !x.Verified);
        verification.LookupFailures = results.Count(x => x is not null && !x.RequestSucceeded);
        verification.ResolvedArtists = evidence.Count(kv => { var r = kv.Value.Values.OrderByDescending(v => v).Take(2).ToArray(); return r.Length == 1 || (r.Length == 2 && r[0] >= r[1] * 2); });
    }
}
