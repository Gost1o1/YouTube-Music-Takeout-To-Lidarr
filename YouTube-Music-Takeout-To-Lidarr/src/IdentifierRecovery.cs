using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

sealed partial class DesktopServer
{
    // No persistent match cache. Recovery operates on this session's audit only.
    sealed class IdentifierRecoveryState
    {
        public bool Running, Done, Failed;
        public int Total, Checked, ArtistsResolved, ArtistCandidates, IsrcFound, SongsRecovered, Conflicts, TitleFormatCandidates, VersionConflicts, ArtistCreditCandidates;
        public string Phase = "Not started", Error = "";
        public DateTimeOffset StartedAt;
        public List<RecoveryDiagnostic> Events = new();
        public IdentifierRecoveryState Copy() => (IdentifierRecoveryState)MemberwiseClone();
    }
    sealed class RecoveryDiagnostic
    {
        public string DateUtc = "", Entity = "", SourceRow = "", Artist = "", Song = "", Album = "",
            Provider = "", Action = "", RequestResource = "", HttpStatus = "", DurationMs = "",
            ExistingArtistMbid = "", LidarrArtistMbid = "", ProposedArtistMbid = "",
            PreviousRecordingMbid = "", CandidateRecordingMbids = "", Isrc = "",
            CandidateCount = "", Outcome = "", Validation = "", Changed = "false",
            ExternalEvidence = "", Notes = "";
    }
    IdentifierRecoveryState recovery = new();
    static bool ValidMbid(string value) => Guid.TryParseExact(value, "D", out _);
    static bool ValidIsrc(string value) => Regex.IsMatch(value, "^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}$", RegexOptions.CultureInvariant);
    // Artist-credit is not included by default on an ISRC lookup. A missing credit must never be mistaken for a mismatched artist.
    static string MusicBrainzIsrcLookupUrl(string isrc) => "https://musicbrainz.org/ws/2/isrc/" + isrc + "?inc=artist-credits&fmt=json";
    static string RecoverProp(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : "";

    static bool ArtistNameOrAlias(JsonElement artist, string name)
    {
        if (Normalize(name).Length == 0) return false;
        if (Normalize(RecoverProp(artist, "name")) == Normalize(name)) return true;
        if (artist.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
            foreach (var alias in aliases.EnumerateArray())
                if (Normalize(RecoverProp(alias, "name")) == Normalize(name)) return true;
        return false;
    }
    // A song's recording may have several credited artists. An existing resolved
    // artist MBID is stronger than a name-only match and blocks same-name collisions.
    static bool HasExpectedCredit(JsonElement recording, string artistName, string expectedMbid)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits) || credits.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind != JsonValueKind.Object || !credit.TryGetProperty("artist", out var ar)) continue;
            var id = RecoverProp(ar, "id");
            if (expectedMbid.Length > 0 ? id.Equals(expectedMbid, StringComparison.OrdinalIgnoreCase)
                : Normalize(RecoverProp(ar, "name")) == Normalize(artistName)) return true;
        }
        return false;
    }
    static (string Isrc, string Title, string Artist) ParseDeezerDetail(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _))
            throw new JsonException("Unexpected Deezer track detail response");
        var credit = root.TryGetProperty("artist", out var ar) && ar.ValueKind == JsonValueKind.Object
            ? RecoverProp(ar, "name") : "";
        return (RecoverProp(root, "isrc").Replace("-", "").ToUpperInvariant(), RecoverProp(root, "title"), credit);
    }
    sealed record IsrcCandidate(string Id, string Title, bool CreditMatch, bool NameOnlyCreditMatch,
        RecoveryTitleEvidence TitleEvidence, string CreditNames, string CreditMbids)
    {
        public bool TitleMatch => TitleEvidence.Eligible;
    }
    static string CreditNames(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        return string.Join(" | ", arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => RecoverProp(x, "name") is { Length: > 0 } named ? named :
                x.TryGetProperty("artist", out var artist) ? RecoverProp(artist, "name") : "")
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }
    static string CreditMbids(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        return string.Join(" | ", arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object &&
            x.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object)
            .Select(x => RecoverProp(x.GetProperty("artist"), "id"))
            .Where(ValidMbid).Distinct(StringComparer.OrdinalIgnoreCase));
    }
    static bool HasNamedCredit(JsonElement recording, string artistName)
    {
        if (Normalize(artistName).Length == 0) return false;
        if (!recording.TryGetProperty("artist-credit", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
        return arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Any(x =>
            Normalize(RecoverProp(x, "name")) == Normalize(artistName) ||
            (x.TryGetProperty("artist", out var artist) && artist.ValueKind == JsonValueKind.Object &&
             Normalize(RecoverProp(artist, "name")) == Normalize(artistName)));
    }
    static RecoveryTitleEvidence CorroborateFeaturedArtist(string sourceTitle, string mbTitle, JsonElement recording)
    {
        var evidence = EvaluateRecoveryTitle(sourceTitle, mbTitle);
        if (evidence.Decision != "featured-artist-credit-not-checked") return evidence;
        var feature = ExtractTrailingFeaturedArtist(sourceTitle);
        if (feature.Length == 0) feature = ExtractTrailingFeaturedArtist(mbTitle);
        // One title may omit its "feat." suffix; never presume it is the same
        // feature unless the identified MB recording explicitly credits the performer.
        return HasNamedCredit(recording, feature)
            ? evidence with { Decision = "featured-artist-verified-in-mb-credits", Eligible = true }
            : evidence with { Decision = "featured-artist-not-in-mb-credits", Eligible = false };
    }
    static List<IsrcCandidate> ParseMbIsrc(string body, string title, string artist, string expectedMbid)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("recordings", out var recordings) || recordings.ValueKind != JsonValueKind.Array)
            throw new JsonException("MusicBrainz ISRC response missing recordings array");
        return recordings.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => new IsrcCandidate(RecoverProp(x, "id"), RecoverProp(x, "title"),
                HasExpectedCredit(x, artist, expectedMbid), HasNamedCredit(x, artist),
                CorroborateFeaturedArtist(title, RecoverProp(x, "title"), x), CreditNames(x), CreditMbids(x)))
            .Where(x => ValidMbid(x.Id)).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(c => c.CreditMatch && c.TitleMatch)
                .ThenByDescending(c => c.CreditMatch).ThenByDescending(c => c.TitleMatch).First()).ToList();
    }
    async Task<(string? Body, int Status, long Elapsed)> RecoveryGetAsync(
        HttpClient client, string url, RequestPacer? pacer, CancellationToken ct)
    {
        if (pacer != null && url.StartsWith("https://musicbrainz.org/ws/2/", StringComparison.OrdinalIgnoreCase))
            return await UnifiedMusicBrainzGetAsync(client, url, pacer, ct);
        var watch = Stopwatch.StartNew();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (pacer != null) await pacer.Wait(ct);
            try
            {
                using var response = await client.GetAsync(url, ct);
                int status = (int)response.StatusCode;
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500) && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct);
                    continue;
                }
                if (!response.IsSuccessStatusCode) return (null, status, watch.ElapsedMilliseconds);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Length > 4_000_000) return (null, 413, watch.ElapsedMilliseconds);
                return (body, status, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (HttpRequestException)
            {
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        return (null, 0, watch.ElapsedMilliseconds);
    }
    void RecordRecovery(RecoveryDiagnostic d)
    {
        d.DateUtc = DateTimeOffset.UtcNow.ToString("O");
        lock (verificationGate)
        {
            recovery.Events.Add(d);
            recovery.Checked++;
        }
    }
    async Task RunIdentifierRecoveryAsync(Track[] source, EvidenceAudit audit, CancellationToken ct)
    {
        using var mb = CreateMbClient();
        using var dz = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) };
        dz.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeMusicTakeoutToLidarr/0.9.0 (https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr)");
        var mbPacer = sharedMbPacer;
        try
        {
            // Stage A: validate Lidarr MBIDs against MusicBrainz's canonical artist
            // and alias list. Mere Lidarr name overlap is not proof of identity.
            foreach (var ar in audit.Artists.Where(x => x.Status != "high"))
            {
                try
                {
                ct.ThrowIfCancellationRequested();
                var d = new RecoveryDiagnostic { Entity = "artist", Artist = ar.SourceName,
                    Provider = "Lidarr+MusicBrainz", Action = "artist-identity-validation",
                    ExistingArtistMbid = ar.ArtistId,
                    ExternalEvidence = string.Join(";", audit.Songs.Where(x => x.Artist.Equals(ar.SourceName, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.ExternalStatus).Distinct()) };
                lock (verificationGate) recovery.Phase = $"Artist IDs: {ar.SourceName} ({recovery.Checked + 1}/{recovery.Total})";
                var matches = lidarrArtists.Where(x => Normalize(x.artistName) == Normalize(ar.SourceName) && ValidMbid(x.foreignArtistId))
                    .Select(x => x.foreignArtistId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                d.LidarrArtistMbid = string.Join(" | ", matches);
                ar.LidarrMbIdCandidate = d.LidarrArtistMbid;
                if (matches.Length == 1)
                {
                    var id = matches[0];
                    d.RequestResource = "musicbrainz:/artist/{mbid}?inc=aliases";
                    var get = await RecoveryGetAsync(mb, "https://musicbrainz.org/ws/2/artist/" + id + "?inc=aliases&fmt=json", mbPacer, ct);
                    d.HttpStatus = get.Status.ToString(); d.DurationMs = get.Elapsed.ToString();
                    if (get.Body != null)
                    {
                        using var doc = JsonDocument.Parse(get.Body);
                        bool validName = ArtistNameOrAlias(doc.RootElement, ar.SourceName);
                        bool noConflict = ar.ArtistId.Length == 0 || ar.ArtistId.Equals(id, StringComparison.OrdinalIgnoreCase);
                        bool supportingSong = audit.Songs.Any(x => x.Artist.Equals(ar.SourceName, StringComparison.OrdinalIgnoreCase) &&
                            (x.ArtistId.Equals(id, StringComparison.OrdinalIgnoreCase) && x.ExactTitle && x.ExactArtist));
                        bool externalTitles = audit.Songs.Any(x => x.Artist.Equals(ar.SourceName, StringComparison.OrdinalIgnoreCase) &&
                            x.ExternalStatus is "both-catalogs-support-title-and-artist" or "one-catalog-supports-title-and-artist");
                        d.ProposedArtistMbid = id;
                        d.Validation = $"canonical-or-alias={validName};existing-id-nonconflicting={noConflict};song-evidence={supportingSong};external-evidence={externalTitles}";
                        if (validName && noConflict && (supportingSong || externalTitles))
                        {
                            ar.ArtistId = id; ar.Status = "high"; ar.ArtistIdRecoveryStatus = "validated-existing-lidarr-id";
                            ar.ArtistIdRecoverySource = "Lidarr MBID + MusicBrainz canonical name/alias + supported source song";
                            ar.Reason = "Artist MBID independently validated with MusicBrainz; source song evidence corroborated";
                            d.Outcome = "artist-id-validated"; d.Changed = "true";
                            lock (verificationGate) recovery.ArtistsResolved++;
                        }
                        else
                        {
                            ar.ArtistIdRecoveryStatus = noConflict ? "needs-independent-song-evidence" : "conflicting-artist-id";
                            d.Outcome = ar.ArtistIdRecoveryStatus;
                            if (!noConflict) lock (verificationGate) recovery.Conflicts++;
                        }
                    }
                    else { d.Outcome = "musicbrainz-artist-lookup-unavailable"; ar.ArtistIdRecoveryStatus = d.Outcome; }
                }
                else if (matches.Length > 1)
                {
                    d.Outcome = "multiple-lidarr-identities-same-name"; ar.ArtistIdRecoveryStatus = d.Outcome;
                    lock (verificationGate) recovery.Conflicts++;
                }
                else
                {
                    // Direct MusicBrainz artist search: candidates only, not an
                    // automatic identity. Same-name artist collisions are possible.
                    // MusicBrainz search indexes artist aliases, and older performer names may be
                    // absent from canonical names. Search both fields, but keep hits as candidates.
                    var query = "artist:\"" + EscapeMb(ar.SourceName) + "\" OR alias:\"" + EscapeMb(ar.SourceName) + "\"";
                    d.RequestResource = "musicbrainz:/artist?query=artist-name";
                    var url = "https://musicbrainz.org/ws/2/artist?query=" + Uri.EscapeDataString(query) + "&limit=20&fmt=json";
                    var get = await RecoveryGetAsync(mb, url, mbPacer, ct);
                    d.HttpStatus = get.Status.ToString(); d.DurationMs = get.Elapsed.ToString();
                    if (get.Body != null)
                    {
                        using var doc = JsonDocument.Parse(get.Body);
                        if (doc.RootElement.TryGetProperty("artists", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                        {
                            // A search hit is a suggestion, not identity proof. Canonical-name
                            // matches are kept, as are alias hits for follow-up confirmation.
                            var ids = candidates.EnumerateArray().Where(x => ValidMbid(RecoverProp(x, "id")))
                                .Select(x => RecoverProp(x, "id"))
                                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
                            ar.ArtistIdCandidates = string.Join(" | ", ids); d.ProposedArtistMbid = ar.ArtistIdCandidates;
                            d.CandidateCount = ids.Length.ToString();
                            if (ids.Length > 0) lock (verificationGate) recovery.ArtistCandidates++;
                        }
                    }
                    d.Validation = "Search queried canonical names and aliases; candidate IDs have NOT been validated against source recordings";
                    d.Outcome = ar.ArtistIdCandidates.Length > 0 ? "candidate-only-review-required" : "no-confirmed-artist-candidate";
                    ar.ArtistIdRecoveryStatus = d.Outcome;
                }
                RecordRecovery(d);
                }
                catch (JsonException)
                {
                    ar.ArtistIdRecoveryStatus = "invalid-musicbrainz-json";
                    RecordRecovery(new RecoveryDiagnostic { Entity = "artist", Artist = ar.SourceName,
                        Provider = "MusicBrainz", Action = "artist-identity-validation",
                        Outcome = "invalid-musicbrainz-json" });
                }
            }
            // Stage A2: re-score the bounded ambiguous artist candidates against actual
            // artist-MBID-credited recordings. Never trust an alias alone.
            await RunCandidateArtistChecksAsync(audit, mb, mbPacer, ct);
            // Stage B: only Deezer-supported songs with a usable track ID and
            // unresolved exact recording can benefit from detailed ISRC lookup.
            foreach (var row in audit.Songs.Where(x => x.DeezerTrackId.Length > 0 && x.Status != "high"))
            {
                try
                {
                ct.ThrowIfCancellationRequested();
                var d = new RecoveryDiagnostic { Entity = "song", SourceRow = row.Row.ToString(), Artist = row.Artist,
                    Song = row.Song, Album = row.Album, Provider = "Deezer+MusicBrainz", Action = "isrc-lookup",
                    ExistingArtistMbid = row.ArtistId, PreviousRecordingMbid = row.RecordingId,
                    ExternalEvidence = row.ExternalStatus };
                lock (verificationGate) recovery.Phase = $"Track ISRC evidence: {row.Song} ({recovery.Checked + 1}/{recovery.Total})";
                if (!long.TryParse(row.DeezerTrackId, out var trackNumber) || trackNumber <= 0)
                {
                    d.Outcome = "invalid-deezer-track-id"; row.IsrcStatus = d.Outcome; RecordRecovery(d); continue;
                }
                d.RequestResource = "deezer:/track/{track-id}";
                var get = await RecoveryGetAsync(dz, "https://api.deezer.com/track/" + trackNumber, null, ct);
                d.HttpStatus = get.Status.ToString(); d.DurationMs = get.Elapsed.ToString();
                if (get.Body == null) { d.Outcome = "deezer-unavailable"; row.IsrcStatus = d.Outcome; RecordRecovery(d); continue; }
                var details = ParseDeezerDetail(get.Body);
                if (!CatalogTitleMatches(row.Song, details.Title) || !CatalogArtistMatches(row.Artist, details.Artist))
                {
                    d.Outcome = "deezer-track-artist-or-title-conflict"; row.IsrcStatus = d.Outcome;
                    lock (verificationGate) recovery.Conflicts++;
                    RecordRecovery(d); continue;
                }
                if (!ValidIsrc(details.Isrc))
                {
                    d.Outcome = "missing-or-invalid-isrc"; row.IsrcStatus = d.Outcome; RecordRecovery(d); continue;
                }
                d.Isrc = row.Isrc = details.Isrc;
                lock (verificationGate) recovery.IsrcFound++;
                d.RequestResource += " -> musicbrainz:/isrc/{isrc}";
                var mbGet = await RecoveryGetAsync(mb, MusicBrainzIsrcLookupUrl(details.Isrc), mbPacer, ct);
                d.HttpStatus += ";MB=" + mbGet.Status; d.DurationMs += ";MB=" + mbGet.Elapsed;
                if (mbGet.Body == null)
                {
                    d.Outcome = "musicbrainz-isrc-unavailable"; row.IsrcStatus = d.Outcome; RecordRecovery(d); continue;
                }
                var ar = audit.Artists.FirstOrDefault(x => x.SourceName.Equals(row.Artist, StringComparison.OrdinalIgnoreCase));
                if (ValidMbid(row.ArtistId) && ar is { Status: "high" } && ValidMbid(ar.ArtistId) &&
                    !row.ArtistId.Equals(ar.ArtistId, StringComparison.OrdinalIgnoreCase))
                {
                    d.Outcome = "song-artist-mbid-conflicts-with-validated-artist";
                    row.IsrcStatus = d.Outcome;
                    lock (verificationGate) recovery.Conflicts++;
                    RecordRecovery(d); continue;
                }
                string expectedArtistId = ValidMbid(row.ArtistId) ? row.ArtistId :
                    ar is { Status: "high" } && ValidMbid(ar.ArtistId) ? ar.ArtistId : "";
                // A provider response without artist credits is incomplete, not contradictory.
                // Never label its candidates as an artist mismatch or accept a recording.
                using (var mbJson = JsonDocument.Parse(mbGet.Body))
                {
                    if (mbJson.RootElement.TryGetProperty("recordings", out var mbRecs) &&
                        mbRecs.ValueKind == JsonValueKind.Array && mbRecs.GetArrayLength() > 0 &&
                        !mbRecs.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.Object &&
                            r.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array &&
                            credits.GetArrayLength() > 0))
                    {
                        d.Outcome = "musicbrainz-artist-credits-missing";
                        d.Validation = "No artist credits in returned recordings; no rejection or recovery inferred";
                        row.IsrcStatus = d.Outcome;
                        RecordRecovery(d);
                        continue;
                    }
                }
                var results = ParseMbIsrc(mbGet.Body, row.Song, row.Artist, expectedArtistId);
                d.CandidateRecordingMbids = row.IsrcCandidateRecordingIds = string.Join(" | ", results.Select(x => x.Id));
                var matching = results.Where(x => x.TitleMatch && x.CreditMatch).ToArray();
                d.CandidateCount = results.Count.ToString(); row.IsrcMatchingCandidates = matching.Length;
                // Per-candidate diagnostics retain the actual credited names/MBIDs and precise
                // version decision; a name-only credit cannot override a conflicting MBID.
                row.IsrcCandidateValidationDetails = string.Join(" | ", results.Select(c =>
                    $"{c.Id}:title={c.TitleEvidence.Decision};artist-mbid={(c.CreditMatch ? "agree" : "mismatch")};" +
                    $"artist-name={(c.NameOnlyCreditMatch ? "agree" : "mismatch")};" +
                    $"credit-names={c.CreditNames};credit-mbids={c.CreditMbids};" +
                    $"decision={(c.TitleMatch && c.CreditMatch ? "eligible" : "held")}"));
                lock (verificationGate)
                {
                    recovery.TitleFormatCandidates += results.Count(c => c.TitleMatch && c.TitleEvidence.Decision != "exact-normalized-title");
                    recovery.VersionConflicts += results.Count(c => c.TitleEvidence.Decision is "version-type-conflict" or "version-details-uncertain");
                    recovery.ArtistCreditCandidates += results.Count(c => c.TitleMatch && c.NameOnlyCreditMatch && !c.CreditMatch);
                }
                d.Validation = $"expected-artist-mbid={(expectedArtistId.Length > 0 ? expectedArtistId : "not-established")};" +
                    $"matching={matching.Length};total={results.Count};candidates={row.IsrcCandidateValidationDetails}";
                if (matching.Length == 1 && expectedArtistId.Length > 0 &&
                    (row.RecordingId.Length == 0 || row.RecordingId.Equals(matching[0].Id, StringComparison.OrdinalIgnoreCase)))
                {
                    // ISRC + exact title + artist MBID establishes a plausible
                    // recording identity. It does NOT prove the exact audio master.
                    row.RecordingId = matching[0].Id; row.ArtistId = expectedArtistId;
                    row.Status = "artist-supported/version-review";
                    row.SongIdentityStatus = "artist-and-song-supported";
                    row.Reason = "Artist/title verified with MBID and ISRC; exact audio version remains unproven";
                    row.RecoveryDecision = row.IsrcStatus = "isrc-musicbrainz-corroborated";
                    d.Outcome = "recording-identity-recovered-version-unresolved"; d.Changed = "true";
                    lock (verificationGate) recovery.SongsRecovered++;
                }
                else if (matching.Length > 1)
                {
                    d.Outcome = "multiple-recordings-share-isrc"; row.IsrcStatus = d.Outcome;
                }
                else if (matching.Length == 1 && row.RecordingId.Length > 0 &&
                    !row.RecordingId.Equals(matching[0].Id, StringComparison.OrdinalIgnoreCase))
                {
                    d.Outcome = "isrc-conflicts-with-existing-recording-id"; row.IsrcStatus = d.Outcome;
                    lock (verificationGate) recovery.Conflicts++;
                }
                else if (results.Any(x => x.TitleMatch && x.NameOnlyCreditMatch && !x.CreditMatch))
                {
                    // Known conflicting artist MBIDs must not be resolved by matching a name.
                    d.Outcome = "isrc-artist-name-agrees-mbid-unverified"; row.IsrcStatus = d.Outcome;
                }
                else if (results.Any(x => x.CreditMatch && (x.TitleEvidence.Decision is "version-type-conflict" or "version-details-uncertain")))
                {
                    d.Outcome = "isrc-version-conflict-needs-review"; row.IsrcStatus = d.Outcome;
                }
                else { d.Outcome = "isrc-no-compatible-musicbrainz-recording"; row.IsrcStatus = d.Outcome; }
                RecordRecovery(d);
                }
                catch (JsonException)
                {
                    row.IsrcStatus = "invalid-provider-json";
                    RecordRecovery(new RecoveryDiagnostic { Entity = "song", SourceRow = row.Row.ToString(),
                        Artist = row.Artist, Song = row.Song, Album = row.Album,
                        Provider = "Deezer+MusicBrainz", Action = "isrc-lookup", Outcome = "invalid-provider-json" });
                }
            }
            // Stage C: examine source album groups and MusicBrainz release-track credits.
            // This is bounded, read-only and conservative about edition differences.
            await RunWholeAlbumChecksAsync(audit, mb, mbPacer, ct);
            // Stage D: unlike the earlier cohort-only pass, inspect isolated source songs
            // with validated artist MBIDs and direct album/release-track evidence.
            await RunTargetedReleaseDiscoveryV915Async(audit, mb, mbPacer, ct);
            lock (verificationGate)
            {
                verification.ArtistHighConfidence = audit.Artists.Count(x => x.Status == "high");
                verification.ArtistNeedsReview = audit.Artists.Count(x => x.Status != "high");
                verification.SongHighConfidence = audit.Songs.Count(x => x.Status == "high");
                verification.SongNeedsReview = audit.Songs.Count(x => x.Status != "high");
                verification.SongIdentitySupported = audit.Songs.Count(x => x.SongIdentityStatus == "artist-and-song-supported");
                verification.VerifiedRecordings = audit.Songs.Count(x => x.Status is "high" or "artist-supported/version-review");
                verification.Ambiguous = audit.Songs.Count(x => x.Status == "ambiguous");
                verification.Unmatched = audit.Songs.Count(x => x.Status == "unmatched");
                recovery.Running = false; recovery.Done = true;
                recovery.Phase = "Identifier recovery completed; export diagnostics and both updated audits.";
            }
        }
        catch (OperationCanceledException)
        {
            lock (verificationGate) { recovery.Running = false; recovery.Failed = true; recovery.Error = "Interrupted by user"; }
        }
        catch (Exception ex)
        {
            Log("Identifier recovery error: " + ex.GetType().Name + ": " + ex.Message);
            lock (verificationGate) { recovery.Running = false; recovery.Failed = true; recovery.Error = ex.Message; }
        }
    }
    static byte[] BuildRecoveryDiagnosticsCsv(IdentifierRecoveryState state)
    {
        var sb = new StringBuilder();
        sb.Append(CsvLine("Timestamp UTC", "Entity", "Source row", "Source artist", "Source song", "Takeout album",
            "Providers", "Operation", "Sanitized API resources", "HTTP status", "Duration milliseconds",
            "Existing artist MBID", "Lidarr artist MBID", "Proposed artist MBID", "Prior recording MBID",
            "Returned recording MBIDs", "Deezer ISRC", "Candidate count", "Outcome", "Validation checks",
            "Evidence changed", "Prior external catalog evidence", "Notes"));
        foreach (var d in state.Events) sb.Append(CsvLine(d.DateUtc, d.Entity, d.SourceRow, d.Artist, d.Song,
            d.Album, d.Provider, d.Action, d.RequestResource, d.HttpStatus, d.DurationMs,
            d.ExistingArtistMbid, d.LidarrArtistMbid, d.ProposedArtistMbid, d.PreviousRecordingMbid,
            d.CandidateRecordingMbids, d.Isrc, d.CandidateCount, d.Outcome, d.Validation,
            d.Changed, d.ExternalEvidence, d.Notes));
        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }
    async Task SendRecoveryCsv(HttpListenerContext ctx)
    {
        byte[] content;
        lock (verificationGate) content = BuildRecoveryDiagnosticsCsv(recovery);
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=musicbrainz-identifier-recovery-debug.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = content.Length;
        await ctx.Response.OutputStream.WriteAsync(content);
        ctx.Response.Close();
    }
    static void IdentifierRecoverySelfTests()
    {
        static void Assert(bool value, string label) { if (!value) throw new Exception("Recovery self test: " + label); }
        Assert(MusicBrainzIsrcLookupUrl("USSM19902974").Contains("?inc=artist-credits&fmt=json", StringComparison.Ordinal),
            "ISRC lookup must include artist credits; omitting inc rejected valid songs in older versions");
        Assert(ValidIsrc("USSM19902974"), "valid ISRC");
        Assert(!ValidIsrc("WRO-NG"), "bad ISRC");
        var dz = ParseDeezerDetail("{\"title\":\"One More Time\",\"isrc\":\"USSM19902974\",\"artist\":{\"name\":\"Daft Punk\"}}");
        Assert(dz.Title == "One More Time" && dz.Isrc == "USSM19902974", "Deezer detail parsing");
        var good = "{\"recordings\":[{\"id\":\"8ea78fd8-9029-4b81-82c3-92c63f5c1c1e\",\"title\":\"One More Time\",\"artist-credit\":[{\"artist\":{\"id\":\"056e4f3e-d505-4dad-8ec1-d04f521cbb56\",\"name\":\"Daft Punk\"}}]}]}";
        var candidates = ParseMbIsrc(good, "One More Time", "Daft Punk", "056e4f3e-d505-4dad-8ec1-d04f521cbb56");
        Assert(candidates.Count == 1 && candidates[0].TitleMatch && candidates[0].CreditMatch, "ISRC positive credits");
        var missingCredits = "{\"recordings\":[{\"id\":\"8ea78fd8-9029-4b81-82c3-92c63f5c1c1e\",\"title\":\"One More Time\"}]}";
        Assert(!ParseMbIsrc(missingCredits, "One More Time", "Daft Punk", "056e4f3e-d505-4dad-8ec1-d04f521cbb56")[0].CreditMatch,
            "ISRC response without artist credits must never imply a match");
        Assert(!ParseMbIsrc(good, "One More Time", "Other", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")[0].CreditMatch,
            "Wrong artist must be rejected");
        Assert(!ParseMbIsrc(good, "One More Time", "Daft Punk", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")[0].CreditMatch,
            "Matching display name may NOT override a conflicting known MBID");
        Assert(!ParseMbIsrc(good, "One More Time (feat. Person)", "Daft Punk", "056e4f3e-d505-4dad-8ec1-d04f521cbb56")[0].TitleMatch,
            "Uncredited featured performer must not be silently ignored");
        var goodFeatured = "{\"recordings\":[{\"id\":\"8ea78fd8-9029-4b81-82c3-92c63f5c1c1e\",\"title\":\"One More Time\",\"artist-credit\":[{\"artist\":{\"id\":\"056e4f3e-d505-4dad-8ec1-d04f521cbb56\",\"name\":\"Daft Punk\"}},{\"artist\":{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Person\"}}]}]}";
        Assert(ParseMbIsrc(goodFeatured, "One More Time (feat. Person)", "Daft Punk", "056e4f3e-d505-4dad-8ec1-d04f521cbb56")[0].TitleMatch,
            "A real credited featured performer can explain title formatting");
        RecoveryTitleSelfTests();
        var alias = "{\"name\":\"The Artist\",\"aliases\":[{\"name\":\"Artist\"}]}";
        using var doc = JsonDocument.Parse(alias);
        Assert(ArtistNameOrAlias(doc.RootElement, "Artist"), "alias validation");
        Console.WriteLine("PASS: artist canonical/alias, Deezer ISRC, MusicBrainz credit checks");
    }
}
