using System.Net;
using System.Text.Json;
using System.Text;

sealed partial class DesktopServer
{
    // Optional on-demand checks. They never manufacture or replace MusicBrainz IDs.
    // A successful Apple/Deezer catalog hit supports a title/artist pair, not a specific
    // MusicBrainz recording, audio master, ownership, or a complete catalog inventory.
    sealed class SupplementalState
    {
        public bool Running, Done, Failed;
        public int Total, Checked, AppleSupported, DeezerSupported, BothSupported, ApiFailures, MbRecovered;
        public string Phase = "Not started", Error = "";
        public DateTimeOffset StartedAt;
        public SupplementalState Copy() => (SupplementalState)MemberwiseClone();
    }
    SupplementalState supplemental = new();

    sealed class CatalogHit
    {
        public string Status = "not-checked", Title = "", Artist = "", Album = "", Url = "", Id = "";
        public bool AlbumExact;
    }

    static string JsonString(JsonElement el, string property)
        => el.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    // Both provider parsers are pure and can be exercised with offline fixtures.
    static CatalogHit ParseApple(string json, string title, string artist, string album)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new JsonException("Unexpected Apple search format.");
        var hits = new List<CatalogHit>();
        foreach (var entry in results.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || JsonString(entry, "kind") != "song") continue;
            var name = JsonString(entry, "trackName");
            var credit = JsonString(entry, "artistName");
            if (!CatalogTitleMatches(title, name) || !CatalogArtistMatches(artist, credit)) continue;
            var recordAlbum = JsonString(entry, "collectionName");
            hits.Add(new CatalogHit { Status = "supported", Title = name, Artist = credit,
                Album = recordAlbum, AlbumExact = album.Length > 0 && Normalize(album) == Normalize(recordAlbum),
                Url = JsonString(entry, "trackViewUrl"),
                Id = entry.TryGetProperty("trackId", out var id) ? id.ToString() : "" });
        }
        return hits.OrderByDescending(x => x.AlbumExact).FirstOrDefault() ?? new CatalogHit { Status = "no-match" };
    }

    static CatalogHit ParseDeezer(string json, string title, string artist, string album)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out _)) throw new JsonException("Deezer API returned an error.");
        if (!doc.RootElement.TryGetProperty("data", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new JsonException("Unexpected Deezer search format.");
        var hits = new List<CatalogHit>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            string name = JsonString(entry, "title"), credit = "", recordAlbum = "";
            if (entry.TryGetProperty("artist", out var ar) && ar.ValueKind == JsonValueKind.Object) credit = JsonString(ar, "name");
            if (entry.TryGetProperty("album", out var al) && al.ValueKind == JsonValueKind.Object) recordAlbum = JsonString(al, "title");
            if (!CatalogTitleMatches(title, name) || !CatalogArtistMatches(artist, credit)) continue;
            hits.Add(new CatalogHit { Status = "supported", Title = name, Artist = credit,
                Album = recordAlbum, AlbumExact = album.Length > 0 && Normalize(album) == Normalize(recordAlbum),
                Url = JsonString(entry, "link"), Id = entry.TryGetProperty("id", out var id) ? id.ToString() : "" });
        }
        return hits.OrderByDescending(x => x.AlbumExact).FirstOrDefault() ?? new CatalogHit { Status = "no-match" };
    }

    // Strict title/artist equivalence: an ambiguous cover, remix, or live version
    // cannot be confirmed merely by a loosely similar title or shared substring.
    internal static bool CatalogTitleMatches(string a, string b) => a.Length > 0 && Normalize(a) == Normalize(b);
    internal static bool CatalogArtistMatches(string a, string b) => a.Length > 0 && Normalize(a) == Normalize(b);

    async Task<string?> GetCatalogJsonAsync(HttpClient client, Uri uri, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(uri, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt < 2) { await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct); continue; }
                    return null;
                }
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (HttpRequestException)
            {
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        return null;
    }

    async Task RunSupplementalAsync(Track[] source, EvidenceAudit audit, CancellationToken ct)
    {
        // Do not query all 2,505 songs again. Only truly ambiguous or unmatched MB songs.
        var indices = audit.Songs.Select((s, i) => (s, i))
            .Where(x => x.s.Status is "ambiguous" or "unmatched")
            .Select(x => x.i).ToArray();
        lock (verificationGate) supplemental.Total = indices.Length;
        using var apple = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        using var deezer = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        apple.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeMusicTakeoutToLidarr/0.8.9 (https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr)");
        deezer.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeMusicTakeoutToLidarr/0.8.9 (https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr)");
        using var recoveryMb = CreateMbClient();
        var recoveryPacer = sharedMbPacer;
        var lastApple = DateTimeOffset.MinValue;
        var catalogCache = new Dictionary<string, (CatalogHit Apple, CatalogHit Deezer)>(StringComparer.Ordinal);
        try
        {
            foreach (var i in indices)
            {
                ct.ThrowIfCancellationRequested();
                var t = source[i];
                lock (verificationGate) supplemental.Phase = $"Checking independent catalogs: {supplemental.Checked + 1}/{supplemental.Total}";
                var externalKey = string.Join("\u001f", t.Artist, t.Song, t.Album);
                CatalogHit ah, dh;
                if (catalogCache.TryGetValue(externalKey, out var previous))
                {
                    (ah, dh) = previous;
                    IncrementUnifiedCacheHit();
                }
                else
                {
                    // Respect Apple's published search cadence even when another provider is fast.
                    var wait = TimeSpan.FromMilliseconds(3200) - (DateTimeOffset.UtcNow - lastApple);
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                    lastApple = DateTimeOffset.UtcNow;
                    string query = t.Artist + " " + t.Song;
                    var appleUri = new Uri("https://itunes.apple.com/search?media=music&entity=song&country=US&limit=50&term=" + Uri.EscapeDataString(query));
                    var deezerUri = new Uri("https://api.deezer.com/search?limit=50&q=" + Uri.EscapeDataString(query));
                    ah = new CatalogHit { Status = "unavailable" };
                    dh = new CatalogHit { Status = "unavailable" };
                    // Both catalogs are independent, so their I/O can overlap.
                    var appleTask = GetCatalogJsonAsync(apple, appleUri, ct);
                    var deezerTask = GetCatalogJsonAsync(deezer, deezerUri, ct);
                    await Task.WhenAll(appleTask, deezerTask);
                    try { if (appleTask.Result is { } body) ah = ParseApple(body, t.Song, t.Artist, t.Album); }
                    catch (JsonException) { }
                    try { if (deezerTask.Result is { } body) dh = ParseDeezer(body, t.Song, t.Artist, t.Album); }
                    catch (JsonException) { }
                    // Do not cache failed provider results; next identical row can retry.
                    if (ah.Status != "unavailable" && dh.Status != "unavailable")
                        catalogCache[externalKey] = (ah, dh);
                }
                // An outside catalog may reveal a DIFFERENT release title than
                // Takeout. In that case, perform a new targeted MusicBrainz query
                // instead of fabricating a MusicBrainz ID from a store URL.
                MatchResult? recovery = null;
                var winner = ah.Status == "supported" ? ah : dh;
                bool providersConflict = ah.Status == "supported" && dh.Status == "supported" &&
                    Normalize(ah.Album) != Normalize(dh.Album);
                if (!providersConflict && winner.Status == "supported" && winner.Album.Length > 0 &&
                    Normalize(winner.Album) != Normalize(t.Album))
                {
                    lock (verificationGate) supplemental.Phase = $"Checking MusicBrainz against corroborated release: {supplemental.Checked + 1}/{supplemental.Total}";
                    var alt = new Track(t.Artist, t.Song, t.YouTubeUrl, winner.Album, t.ArtistCredits);
                    var mb = await MatchAsync(recoveryMb, recoveryPacer, alt, null, true, ct, albumOnly: true);
                    if (mb.RequestSucceeded && mb.Verified && mb.ExactTitle && mb.ExactArtist &&
                        mb.ArtistId.Length > 0 && mb.RecordingId.Length > 0)
                        recovery = mb;
                }
                lock (verificationGate)
                {
                    var row = audit.Songs[i];
                    if (recovery != null && (row.Status is "ambiguous" or "unmatched"))
                    {
                        string originalStatus = row.Status;
                        row.ArtistId = recovery.ArtistId;
                        row.RecordingId = recovery.RecordingId;
                        row.Status = "artist-supported/version-review";
                        row.Reason = "New MusicBrainz artist + title found using an externally corroborated release; exact recording version remains uncertain";
                        row.SongIdentityStatus = "artist-and-song-supported";
                        row.RecoveredMusicBrainzIdentity = true;
                        supplemental.MbRecovered++;
                        verification.VerifiedRecordings++;
                        verification.SongIdentitySupported++;
                        if (originalStatus == "ambiguous") verification.Ambiguous--;
                        else verification.Unmatched--;
                    }
                    row.AppleStatus = ah.Status; row.AppleTrackId = ah.Id; row.AppleUrl = ah.Url;
                    row.DeezerStatus = dh.Status; row.DeezerTrackId = dh.Id; row.DeezerUrl = dh.Url;
                    row.AppleAlbumMatch = ah.AlbumExact; row.DeezerAlbumMatch = dh.AlbumExact;
                    // Store evidence NEVER substitutes for MusicBrainz evidence.
                    // Only a separate successful targeted MusicBrainz query above
                    // can add provisional IDs; exact-recording confidence stays put.
                    row.ExternalStatus = ah.Status == "supported" && dh.Status == "supported" ? "both-catalogs-support-title-and-artist" :
                        ah.Status == "supported" || dh.Status == "supported" ? "one-catalog-supports-title-and-artist" :
                        ah.Status == "unavailable" && dh.Status == "unavailable" ? "both-providers-unavailable" :
                        "no-confirming-catalog-hit";
                    if (ah.Status == "supported") supplemental.AppleSupported++;
                    if (dh.Status == "supported") supplemental.DeezerSupported++;
                    if (ah.Status == "supported" && dh.Status == "supported") supplemental.BothSupported++;
                    if (ah.Status == "unavailable" || dh.Status == "unavailable") supplemental.ApiFailures++;
                    supplemental.Checked++;
                }
            }
            lock (verificationGate) { supplemental.Running = false; supplemental.Done = true; supplemental.Phase = $"Supplemental checks complete. {supplemental.MbRecovered} provisional MusicBrainz identities recovered by new MusicBrainz queries."; }
        }
        catch (OperationCanceledException)
        {
            lock (verificationGate) { supplemental.Running = false; supplemental.Failed = true; supplemental.Phase = "Supplemental catalog pass interrupted; completed rows retained."; }
        }
        catch (Exception ex)
        {
            Log("Supplemental catalog error: " + ex);
            lock (verificationGate) { supplemental.Running = false; supplemental.Failed = true; supplemental.Error = ex.Message; supplemental.Phase = "Supplemental catalog pass stopped; partial results retained."; }
        }
    }

    // Import *explicitly selected* v0.8.8/v0.8.9 audit exports for optional provider
    // investigation without another 2-hour MusicBrainz scan. Fresh verify ignores this.
    static EvidenceAudit ParsePriorAudit(string songCsv, string artistCsv, Track[] source)
    {
        static List<Dictionary<string, string>> ReadRows(string csv)
        {
            using var reader = new StringReader(csv.TrimStart('\uFEFF'));
            var headers = CsvRow(reader) ?? throw new InvalidOperationException("Empty audit CSV.");
            if (headers.Length > 48) throw new InvalidOperationException("Unexpected audit CSV column count.");
            var rows = new List<Dictionary<string, string>>();
            string[]? fields;
            while ((fields = CsvRow(reader)) != null)
            {
                if (fields.Length == 1 && fields[0].Length == 0) continue;
                if (fields.Length != headers.Length) throw new InvalidOperationException("Audit CSV column mismatch.");
                rows.Add(headers.Select((h, j) => (h, j)).ToDictionary(x => x.h, x => fields[x.j], StringComparer.OrdinalIgnoreCase));
                if (rows.Count > 250_000) throw new InvalidOperationException("Audit CSV too large.");
            }
            return rows;
        }
        static string Get(Dictionary<string, string> r, string name) => r.TryGetValue(name, out var value) ? value : "";
        var sr = ReadRows(songCsv);
        var ar = ReadRows(artistCsv);
        if (sr.Count != source.Length || sr.Count == 0) throw new InvalidOperationException("Song audit must contain exactly the imported Takeout rows.");
        var audit = new EvidenceAudit();
        for (int i = 0; i < sr.Count; i++)
        {
            var r = sr[i]; var t = source[i];
            if (Get(r, "Source row") != (i + 1).ToString() ||
                !string.Equals(Get(r, "YouTube artist"), t.Artist, StringComparison.Ordinal) ||
                !string.Equals(Get(r, "YouTube song"), t.Song, StringComparison.Ordinal) ||
                !string.Equals(Get(r, "YouTube album"), t.Album, StringComparison.Ordinal) ||
                !string.Equals(Get(r, "YouTube link"), t.YouTubeUrl, StringComparison.Ordinal))
                throw new InvalidOperationException($"Source Takeout and audit differ at row {i + 1}. Use matching exports.");
            string status = Get(r, "Confidence");
            if (status is not ("high" or "artist-supported/version-review" or "ambiguous" or "unmatched" or "lookup-failed"))
                throw new InvalidOperationException("Unexpected song confidence value in audit.");
            audit.Songs.Add(new SongAudit
            {
                Row = i + 1, Artist = t.Artist, AllArtistCredits = string.Join(" | ", t.ArtistCredits), Song = t.Song,
                Album = t.Album, YouTubeUrl = t.YouTubeUrl, ArtistId = Get(r, "MusicBrainz artist ID"),
                RecordingId = Get(r, "MusicBrainz recording ID"), Status = status,
                Reason = Get(r, "Evidence/remaining issue"),
                SongIdentityStatus = Get(r, "Song identity status (separate from exact recording)"),
                AppleStatus = Get(r, "Apple song & artist") is { Length: > 0 } aps ? aps : "not-checked",
                DeezerStatus = Get(r, "Deezer song & artist") is { Length: > 0 } dps ? dps : "not-checked",
                ExternalStatus = Get(r, "External catalog evidence") is { Length: > 0 } eps ? eps : "not-checked",
                AppleTrackId = Get(r, "Apple track ID"), AppleUrl = Get(r, "Apple link"),
                DeezerTrackId = Get(r, "Deezer track ID"), DeezerUrl = Get(r, "Deezer link"),
                EvidenceScore = int.TryParse(Get(r, "MB search score (NOT confidence probability)"), out var score) ? score : 0,
                ExactTitle = bool.TryParse(Get(r, "Exact title"), out var et) && et,
                ExactArtist = bool.TryParse(Get(r, "Artist credit agrees"), out var ea) && ea,
                AlbumMatch = bool.TryParse(Get(r, "Album agrees"), out var am) && am,
                CrossSongSupport = bool.TryParse(Get(r, "Cross-song support"), out var css) && css,
                MultipleRecordingVersions = bool.TryParse(Get(r, "Multiple recording versions"), out var multi) && multi,
                AlbumIdentifiesVersion = bool.TryParse(Get(r, "Album uniquely distinguishes version among retrieved candidates"), out var unique) && unique,
                AlbumSearchConfirmed = bool.TryParse(Get(r, "Targeted album query corroborates recording"), out var ac) && ac,
                Alternatives = Get(r, "Alternate artist IDs"),
                AppleAlbumMatch = bool.TryParse(Get(r, "Apple album agrees"), out var aa) && aa,
                DeezerAlbumMatch = bool.TryParse(Get(r, "Deezer album agrees"), out var da) && da,
                RecoveredMusicBrainzIdentity = bool.TryParse(Get(r, "New targeted MusicBrainz candidate from external album"), out var ri) && ri,
                Isrc = Get(r, "Deezer ISRC"), IsrcStatus = Get(r, "ISRC status") is { Length: > 0 } ist ? ist : "not-checked",
                IsrcCandidateRecordingIds = Get(r, "ISRC candidate recording IDs"),
                IsrcMatchingCandidates = int.TryParse(Get(r, "ISRC matching candidate count"), out var mc) ? mc : 0,
                RecoveryDecision = Get(r, "Identifier recovery decision") is { Length: > 0 } rdec ? rdec : "not-checked",
                IsrcCandidateValidationDetails = Get(r, "ISRC candidate validation details"),
                PriorEvidenceRestored = Get(r, "Prior evidence restored")
            });
        }
        var sourceArtists = source.Select(x => x.Artist).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ar.Count != sourceArtists.Count) throw new InvalidOperationException("Artist audit count doesn't match this Takeout.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in ar)
        {
            string name = Get(r, "Artist"), status = Get(r, "Confidence");
            if (!sourceArtists.Contains(name) || !names.Add(name) || status is not ("high" or "needs-review"))
                throw new InvalidOperationException("Artist audit doesn't match the imported source.");
            audit.Artists.Add(new ArtistAudit {
                SourceName = name, ArtistId = Get(r, "Artist MBID"), Status = status,
                Reason = Get(r, "Explanation"),
                SourceSongs = int.TryParse(Get(r, "Unique source songs"), out var ns) ? ns : 0,
                VerifiedSongs = int.TryParse(Get(r, "High-confidence song titles"), out var vs) ? vs : 0,
                UnresolvedSongs = int.TryParse(Get(r, "Unresolved source titles"), out var us) ? us : 0,
                ArtistIdRecoveryStatus = Get(r, "Artist ID recovery status") is { Length: > 0 } ars ? ars : "not-checked",
                ArtistIdRecoverySource = Get(r, "Artist ID recovery source"),
                ArtistIdCandidates = Get(r, "Artist ID candidates"), LidarrMbIdCandidate = Get(r, "Lidarr MBID candidate"),
                SourceSongList = Get(r, "Source songs (names)"),
                CorroboratedSongList = Get(r, "High-confidence overlapping songs (names)"),
                SourceOnlySongList = Get(r, "Remaining source songs (names)"),
                CatalogCoverage = Get(r, "MusicBrainz catalog completeness") is { Length: > 0 } cov ? cov : "Partial: exported evidence only; full artist catalogs not browsed",
                AlreadyInLidarr = bool.TryParse(Get(r, "Already in Lidarr"), out var il) && il
            });
        }
        // The earlier importer zeroed the aggregate counts even when the CSV supplied
        // exact values. Reconstruct counts from the current Takeout and the row evidence
        // when importing a damaged v0.9.0.1 export; NEVER invent catalog-wide counts.
        foreach (var artist in audit.Artists)
        {
            var songs = audit.Songs.Where(x => x.Artist.Equals(artist.SourceName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var distinct = songs.Select(x => Normalize(x.Song)).Distinct().Count();
            var high = songs.Where(x => x.Status == "high").Select(x => Normalize(x.Song)).Distinct().Count();
            if (artist.SourceSongs == 0 && distinct > 0) artist.SourceSongs = distinct;
            if (artist.VerifiedSongs == 0 && high > 0) artist.VerifiedSongs = high;
            if (artist.UnresolvedSongs == 0 && distinct > artist.VerifiedSongs) artist.UnresolvedSongs = distinct - artist.VerifiedSongs;
            artist.ExactSupportedTitles = songs.Where(x => x.ExactArtist && x.ExactTitle)
                .Select(x => Normalize(x.Song)).Distinct().Count();
        }
        return audit;
    }

    // Explicitly merge the original song-evidence export with a newer recovery export.
    // Older exports hold recording-level flags discarded by the v0.9.0.1 importer.
    // The baseline is never permitted to change a recovered MusicBrainz ID or status.
    static int RestoreBaselineEvidence(EvidenceAudit current, EvidenceAudit baseline)
    {
        if (current.Songs.Count != baseline.Songs.Count) throw new InvalidOperationException("Baseline song audit row count differs.");
        int repaired = 0;
        for (int i = 0; i < current.Songs.Count; i++)
        {
            var now = current.Songs[i]; var old = baseline.Songs[i];
            if (now.Row != old.Row || now.Artist != old.Artist || now.Song != old.Song ||
                now.Album != old.Album || now.YouTubeUrl != old.YouTubeUrl)
                throw new InvalidOperationException($"Baseline does not match Takeout row {i + 1}.");
            // Never resurrect version-specific evidence for a different candidate.
            if (!string.Equals(now.RecordingId, old.RecordingId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(now.ArtistId, old.ArtistId, StringComparison.OrdinalIgnoreCase)) continue;
            bool changed = false;
            if (old.AlbumMatch && !now.AlbumMatch) { now.AlbumMatch = true; changed = true; }
            if (old.MultipleRecordingVersions && !now.MultipleRecordingVersions) { now.MultipleRecordingVersions = true; changed = true; }
            if (old.AlbumIdentifiesVersion && !now.AlbumIdentifiesVersion) { now.AlbumIdentifiesVersion = true; changed = true; }
            if (old.AlbumSearchConfirmed && !now.AlbumSearchConfirmed) { now.AlbumSearchConfirmed = true; changed = true; }
            if (old.CrossSongSupport && !now.CrossSongSupport) { now.CrossSongSupport = true; changed = true; }
            if (old.ExactTitle && !now.ExactTitle) { now.ExactTitle = true; changed = true; }
            if (old.ExactArtist && !now.ExactArtist) { now.ExactArtist = true; changed = true; }
            if (now.EvidenceScore == 0 && old.EvidenceScore != 0) { now.EvidenceScore = old.EvidenceScore; changed = true; }
            if (now.Alternatives.Length == 0 && old.Alternatives.Length > 0) { now.Alternatives = old.Alternatives; changed = true; }
            if (changed) { now.PriorEvidenceRestored = "baseline-song-audit"; repaired++; }
        }
        return repaired;
    }

    public static void RunSelfTests() { SupplementalSelfTest(); IdentifierRecoverySelfTests(); CatalogCorroborationSelfTests(); ReleaseDiscoveryV915SelfTests(); MigrationReadinessSelfTest(); LidarrInventorySelfTest(); AlbumGroupSelfTest(); ApprovalQueueSelfTest(); LidarrImportSelfTest(); GuidedReviewSelfTests(); GitHubStarSelfTests(); UnifiedPipelineSelfTests();
        ReviewMediaSelfTests(); ManualFallbackSelfTests(); AutomaticReviewClassificationSelfTests(); }

    static void SupplementalSelfTest()
    {
        const string apple = "{\"results\":[{\"kind\":\"song\",\"artistName\":\"Daft Punk\",\"trackName\":\"One More Time\",\"collectionName\":\"Discovery\",\"trackId\":123,\"trackViewUrl\":\"https://itunes.apple.com/track\"}]}";
        const string deezer = "{\"data\":[{\"id\":456,\"title\":\"One More Time\",\"artist\":{\"name\":\"Daft Punk\"},\"album\":{\"title\":\"Discovery\"},\"link\":\"https://www.deezer.com/track/456\"}]}";
        static void Check(bool ok, string message) { if (!ok) throw new Exception("Self-test failed: " + message); }
        Check(ParseApple(apple, "One More Time", "Daft Punk", "Discovery").AlbumExact, "Apple exact album");
        Check(ParseDeezer(deezer, "One More Time", "Daft Punk", "Discovery").Status == "supported", "Deezer exact hit");
        Check(ParseApple(apple, "One More Time (Live)", "Daft Punk", "Discovery").Status == "no-match", "live versions must not conflate");
        Check(ParseDeezer(deezer, "One More Time", "Someone Else", "Discovery").Status == "no-match", "other artist must not match");
        Check(ParseDeezer("{\"data\":[]}", "One More Time", "Daft Punk", "Discovery").Status == "no-match", "empty results");
        Check(CatalogArtistMatches("AC/DC", "ACDC"), "punctuation normalization");
        // Test prior-export import validation without bundling any user's personal music data.
        var source = new[] { new Track("Daft Punk", "One More Time", "", "Discovery", new[] { "Daft Punk" }) };
        var songs = CsvLine("Source row", "YouTube artist", "YouTube song", "YouTube album", "YouTube link", "Confidence", "Song identity status (separate from exact recording)") +
                    CsvLine("1", "Daft Punk", "One More Time", "Discovery", "", "unmatched", "unresolved");
        var artists = CsvLine("Artist", "Confidence", "Artist MBID", "Explanation") +
                      CsvLine("Daft Punk", "needs-review", "", "No ID");
        Check(ParsePriorAudit(songs, artists, source).Songs.Count == 1, "previous audit import");
        // Construct a complete CSV fixture instead of performing quote-sensitive
        // replacements inside CSV records. This also tests column mapping directly.
        var enriched = CsvLine("Source row", "YouTube artist", "YouTube song", "YouTube album",
                "YouTube link", "Confidence", "Song identity status (separate from exact recording)",
                "Deezer track ID", "External catalog evidence") +
            CsvLine("1", "Daft Punk", "One More Time", "Discovery", "", "unmatched",
                "unresolved", "12345", "one-catalog-supports-title-and-artist");
        Check(ParsePriorAudit(enriched, artists, source).Songs[0].DeezerTrackId == "12345", "provider IDs survive re-import");
        var fullSong = CsvLine("Source row", "YouTube artist", "YouTube song", "YouTube album", "YouTube link", "Confidence",
            "Exact title", "Artist credit agrees", "Album agrees", "Cross-song support", "Multiple recording versions",
            "Album uniquely distinguishes version among retrieved candidates", "Targeted album query corroborates recording",
            "MB search score (NOT confidence probability)", "Deezer ISRC", "ISRC status", "ISRC matching candidate count",
            "Identifier recovery decision") +
            CsvLine("1", "Daft Punk", "One More Time", "Discovery", "", "high", "True", "True", "True",
                "True", "True", "True", "True", "98", "USZZ12345678", "multiple-recordings-share-isrc", "3", "needs-review");
        var loaded = ParsePriorAudit(fullSong, artists, source).Songs[0];
        Check(loaded.AlbumMatch && loaded.MultipleRecordingVersions && loaded.AlbumSearchConfirmed &&
            loaded.AlbumIdentifiesVersion && loaded.CrossSongSupport && loaded.EvidenceScore == 98 &&
            loaded.IsrcMatchingCandidates == 3 && loaded.Isrc == "USZZ12345678" &&
            loaded.RecoveryDecision == "needs-review", "all source evidence and ISRC fields round-trip");
        var countedArtistCsv = CsvLine("Artist", "Artist MBID", "Confidence", "Explanation",
            "Unique source songs", "High-confidence song titles", "Unresolved source titles",
            "Artist ID recovery status", "Artist ID recovery source", "Lidarr MBID candidate") +
            CsvLine("Daft Punk", "056e4f3e-d505-4dad-8ec1-d04f521cbb56", "high", "validated",
                "1", "1", "0", "lidarr-mbid-corroborated", "independent-mbid-check",
                "056e4f3e-d505-4dad-8ec1-d04f521cbb56");
        var artistRoundtrip = ParsePriorAudit(fullSong, countedArtistCsv, source).Artists[0];
        Check(artistRoundtrip.SourceSongs == 1 && artistRoundtrip.VerifiedSongs == 1 &&
            artistRoundtrip.ArtistIdRecoverySource == "independent-mbid-check" &&
            artistRoundtrip.ArtistIdRecoveryStatus == "lidarr-mbid-corroborated", "artist evidence round-trip");
        var eroded = ParsePriorAudit(enriched, artists, source);
        // A baseline with unchanged IDs restores only lost positive facts, not IDs or status.
        var older = ParsePriorAudit(fullSong, artists, source);
        Check(RestoreBaselineEvidence(eroded, older) == 1 && eroded.Songs[0].AlbumMatch &&
            eroded.Songs[0].MultipleRecordingVersions && eroded.Songs[0].EvidenceScore == 98,
            "baseline evidence repair without a fresh verification");
        bool rejects = false;
        try { ParsePriorAudit(songs.Replace("One More Time", "Other Title"), artists, source); }
        catch (InvalidOperationException) { rejects = true; }
        Check(rejects, "source/audit mismatch must be rejected");
        Console.WriteLine("PASS: Provider fixtures, version/artist safety checks, and prior-audit import validation");
    }
}
