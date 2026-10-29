using System.Net;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // Candidate scores are *evidence rankings*, never calibrated probabilities.
    // Every HTTP response is researched anew for this session; no persistent match cache.
    sealed class CorroborationCandidate
    {
        public string Stage = "", SourceArtist = "", SourceAlbum = "", SourceSongs = "",
            CandidateId = "", CandidateName = "", CandidateType = "", NameRelation = "",
            ArtistCreditMbids = "", RecordingMbids = "", ReleaseMbids = "",
            MatchedTitles = "", ConflictingTitles = "", SourceRows = "",
            Decision = "", Reason = "", RequestStatus = "", BestAlternative = "";
        public int Score, TitleMatches, UniqueSourceTitles, RankMargin;
        public bool ArtistIdValidated, VersionConflict, Changed;
    }
    readonly List<CorroborationCandidate> corroboration = new();
    void SaveCorroboration(CorroborationCandidate candidate)
    {
        lock (verificationGate) corroboration.Add(candidate);
    }
    static bool ExactCorroborationTitle(string source, string candidate)
    {
        var title = EvaluateRecoveryTitle(source, candidate);
        // Version descriptions that cannot be reconciled must never be accepted.
        return title.Eligible && title.SourceBase.Length > 0;
    }
    static string[] CandidateIds(string value) => (value ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(ValidMbid).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    static string JsonName(JsonElement element) => RecoverProp(element, "name");

    // A specific MusicBrainz recording search corroborates an artist ID only when
    // the returned recording actually credits that MBID, not merely a matching name.
    static bool RecordingSearchHasCredit(string json, string sourceTitle, string artistId, out string recordings)
    {
        recordings = "";
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("recordings", out var list) || list.ValueKind != JsonValueKind.Array)
            return false;
        var matches = list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object &&
            ValidMbid(RecoverProp(x, "id")) && ExactCorroborationTitle(sourceTitle, RecoverProp(x, "title")) &&
            HasExpectedCredit(x, "", artistId)).Select(x => RecoverProp(x, "id"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        recordings = string.Join(" | ", matches);
        return matches.Length > 0;
    }
    static int ArtistScore(bool alias, int titleMatches, bool lidarrMatches) =>
        (alias ? 30 : 0) + Math.Min(titleMatches, 3) * 20 + (lidarrMatches ? 10 : 0);

    async Task RunCandidateArtistChecksAsync(EvidenceAudit audit, HttpClient mb, RequestPacer pace, CancellationToken ct)
    {
        // Limit potentially expensive searches to candidates with meaningful names
        // and at most four artist identities. Wide same-name collisions stay in review.
        foreach (var artist in audit.Artists.Where(a => a.Status != "high").ToArray())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
            var candidateIds = CandidateIds(string.Join(" | ", new[] { artist.ArtistIdCandidates, artist.LidarrMbIdCandidate, artist.ArtistId }));
            if (candidateIds.Length == 0 || artist.SourceName.Trim() is "" or "-" || candidateIds.Length > 4) continue;
            var source = audit.Songs.Where(s => s.Artist.Equals(artist.SourceName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(s => Normalize(s.Song)).Where(g => g.Key.Length > 0)
                .Select(g => g.First()).OrderBy(s => s.Row).Take(3).ToArray();
            if (source.Length == 0) continue;
            var rankings = new List<CorroborationCandidate>();
            foreach (string id in candidateIds)
            {
                ct.ThrowIfCancellationRequested();
                var record = new CorroborationCandidate { Stage = "artist-id", SourceArtist = artist.SourceName,
                    SourceSongs = string.Join(" | ", source.Select(s => s.Song)), UniqueSourceTitles = source.Length,
                    SourceRows = string.Join(" | ", source.Select(s => s.Row)), CandidateId = id,
                    ArtistIdValidated = ValidMbid(artist.ArtistId) && artist.ArtistId.Equals(id, StringComparison.OrdinalIgnoreCase) };
                bool lidarrMatches = CandidateIds(artist.LidarrMbIdCandidate).Contains(id, StringComparer.OrdinalIgnoreCase);
                var lookup = await RecoveryGetAsync(mb,
                    "https://musicbrainz.org/ws/2/artist/" + id + "?inc=aliases&fmt=json", pace, ct);
                record.RequestStatus = "artist:" + lookup.Status;
                if (lookup.Body == null)
                {
                    record.Decision = "provider-unavailable"; record.Reason = "Artist lookup unavailable";
                    SaveCorroboration(record); continue;
                }
                using (var doc = JsonDocument.Parse(lookup.Body))
                {
                    record.CandidateName = RecoverProp(doc.RootElement, "name");
                    bool alias = ArtistNameOrAlias(doc.RootElement, artist.SourceName);
                    record.NameRelation = alias ? (Normalize(record.CandidateName) == Normalize(artist.SourceName) ? "canonical" : "alias") : "unrelated-name";
                    if (!alias)
                    {
                        record.Decision = "name-or-alias-unverified";
                        record.Reason = "Matching recording titles cannot establish the identity of a different artist name";
                        SaveCorroboration(record); rankings.Add(record); continue;
                    }
                }
                var idsFound = new List<string>();
                var supportingTitles = new List<string>();
                foreach (var song in source)
                {
                    string query = "arid:" + id + " AND recording:\"" + EscapeMb(song.Song) + "\"";
                    var url = "https://musicbrainz.org/ws/2/recording?query=" + Uri.EscapeDataString(query) + "&limit=20&fmt=json";
                    var result = await RecoveryGetAsync(mb, url, pace, ct);
                    record.RequestStatus += ";recording:" + result.Status;
                    if (result.Body != null && RecordingSearchHasCredit(result.Body, song.Song, id, out var recorded))
                    {
                        supportingTitles.Add(song.Song); idsFound.AddRange(CandidateIds(recorded));
                    }
                }
                record.TitleMatches = supportingTitles.Count;
                record.MatchedTitles = string.Join(" | ", supportingTitles);
                record.RecordingMbids = string.Join(" | ", idsFound.Distinct(StringComparer.OrdinalIgnoreCase));
                record.Score = ArtistScore(true, record.TitleMatches, lidarrMatches);
                record.Decision = "candidate-needs-ranking";
                record.Reason = "Artist canonical/alias + exact recording-title searches with credited artist MBIDs";
                rankings.Add(record); SaveCorroboration(record);
            }
            var sorted = rankings.OrderByDescending(r => r.Score).ThenByDescending(r => r.TitleMatches).ToArray();
            if (sorted.Length == 0) continue;
            var best = sorted[0]; var margin = best.Score - (sorted.Length > 1 ? sorted[1].Score : 0);
            foreach (var ranked in rankings)
            {
                ranked.RankMargin = ranked == best ? margin : ranked.Score - best.Score;
                ranked.BestAlternative = sorted.Length > 1 ? sorted[1].CandidateId : "none";
            }
            // At least two *independent* exact recordings, or an existing Lidarr MBID
            // plus one recording, are necessary. A sole exact name is never sufficient.
            bool isLidarrId = CandidateIds(artist.LidarrMbIdCandidate)
                .Contains(best.CandidateId, StringComparer.OrdinalIgnoreCase);
            bool noConflict = !ValidMbid(artist.ArtistId) || artist.ArtistId.Equals(best.CandidateId, StringComparison.OrdinalIgnoreCase);
            bool validated = noConflict && (best.NameRelation is "canonical" or "alias") &&
                (best.TitleMatches >= 2 || (isLidarrId && best.TitleMatches >= 1)) &&
                (sorted.Length == 1 || margin >= 20) && best.TitleMatches > 0;
            if (validated)
            {
                artist.ArtistId = best.CandidateId;
                artist.Status = "high";
                artist.ArtistIdRecoveryStatus = "corroborated-with-recording-credit";
                artist.ArtistIdRecoverySource = "MusicBrainz artist alias/canonical name + MBID-credited distinct recording searches";
                artist.Reason = "Artist identity corroborated independently; unresolved source recording versions remain separate";
                best.Decision = "artist-id-corroborated"; best.Changed = true;
                lock (verificationGate) recovery.ArtistsResolved++;
            }
            else
            {
                best.Decision = "candidate-only-needs-review";
                best.Reason = "No independent corroboration, ambiguous candidate ranking or an existing MBID conflict";
            }
            lock (verificationGate) { recovery.Total++; recovery.Phase = "Artist candidate corroboration: " + artist.SourceName; }
            RecordRecovery(new RecoveryDiagnostic { Entity = "artist", Artist = artist.SourceName,
                Provider = "MusicBrainz", Action = "candidate-recording-corroboration",
                ProposedArtistMbid = best.CandidateId,
                CandidateCount = rankings.Count.ToString(),
                Outcome = best.Decision, Changed = best.Changed.ToString().ToLowerInvariant(),
                Validation = "score-not-probability=" + best.Score + ";rank-margin=" + margin +
                    ";distinct-recording-titles=" + best.TitleMatches + ";lidarr-id-corroboration=" + isLidarrId,
                Notes = "Unconfirmed artist candidates remain review-only. Candidate score CSV contains individual IDs." });
            }
            catch (JsonException)
            {
                lock (verificationGate) recovery.Total++;
                RecordRecovery(new RecoveryDiagnostic { Entity = "artist", Artist = artist.SourceName,
                    Provider = "MusicBrainz", Action = "candidate-recording-corroboration",
                    Outcome = "invalid-provider-json", Validation = "No identity inferred from incomplete JSON" });
            }
        }
    }

    static string[] MusicBrainzTrackIds(JsonElement release, string title, string artistId)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var found = new List<string>();
        foreach (var medium in media.EnumerateArray())
        {
            if (medium.ValueKind != JsonValueKind.Object) continue;
            if (!medium.TryGetProperty("tracks", out var tracks) || tracks.ValueKind != JsonValueKind.Array) continue;
            foreach (var track in tracks.EnumerateArray())
            {
                if (track.ValueKind != JsonValueKind.Object) continue;
                if (!track.TryGetProperty("recording", out var rec) || rec.ValueKind != JsonValueKind.Object) continue;
                if (!ExactCorroborationTitle(title, RecoverProp(rec, "title"))) continue;
                if (!HasExpectedCredit(rec, "", artistId)) continue;
                var id = RecoverProp(rec, "id");
                if (ValidMbid(id)) found.Add(id);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    async Task RunWholeAlbumChecksAsync(EvidenceAudit audit, HttpClient mb, RequestPacer pace, CancellationToken ct)
    {
        var eligible = audit.Songs.Where(s => s.SongIdentityStatus != "artist-and-song-supported" &&
                !string.IsNullOrWhiteSpace(s.Album) && s.Album.Trim() != "-")
            .GroupBy(s => (Artist: Normalize(s.Artist), Album: Normalize(s.Album)))
            .Where(g => g.Key.Artist.Length > 0 && g.Key.Album.Length > 0 &&
                g.Select(s => Normalize(s.Song)).Distinct().Count() >= 2)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Artist).Take(24).ToArray();
        foreach (var group in eligible)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
            var songs = group.GroupBy(s => Normalize(s.Song)).Select(g => g.First()).ToArray();
            var source = songs[0];
            var artist = audit.Artists.FirstOrDefault(a => a.SourceName.Equals(source.Artist, StringComparison.OrdinalIgnoreCase));
            if (artist == null || artist.Status != "high" || !ValidMbid(artist.ArtistId)) continue;
            var query = "arid:" + artist.ArtistId + " AND releasegroup:\"" + EscapeMb(source.Album) + "\"";
            var url = "https://musicbrainz.org/ws/2/release-group?query=" + Uri.EscapeDataString(query) + "&limit=10&fmt=json";
            var get = await RecoveryGetAsync(mb, url, pace, ct);
            if (get.Body == null) continue;
            using var doc = JsonDocument.Parse(get.Body);
            if (!doc.RootElement.TryGetProperty("release-groups", out var groups) || groups.ValueKind != JsonValueKind.Array) continue;
            var candidates = groups.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object &&
                    ValidMbid(RecoverProp(x, "id")))
                .Select(x => (Id: RecoverProp(x, "id"), Title: RecoverProp(x, "title"),
                    Relationship: AlbumTitleRelationship(source.Album, RecoverProp(x, "title"))))
                .Where(x => x.Relationship is "exact-normalized-title" or "volume-notation-variant" or "unicode-glyph-variant" or "edition-or-suffix-variant" or "ep-suffix-variant")
                .Take(4).ToArray();
            if (candidates.Length == 0) continue;
            var evaluated = new List<CorroborationCandidate>();
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var row = new CorroborationCandidate { Stage = "album-group", SourceArtist = source.Artist,
                    SourceAlbum = source.Album, SourceSongs = string.Join(" | ", songs.Select(s => s.Song)),
                    SourceRows = string.Join(" | ", songs.Select(s => s.Row)), UniqueSourceTitles = songs.Length,
                    CandidateId = candidate.Id, CandidateName = candidate.Title, CandidateType = "MusicBrainz release-group",
                    NameRelation = candidate.Relationship, ArtistIdValidated = true };
                if (candidate.Relationship != "exact-normalized-title")
                {
                    row.Decision = "edition-or-title-alias-review-only";
                    row.Reason = "Related album name is insufficient to identify a specific release or recording version";
                    SaveCorroboration(row); evaluated.Add(row); continue;
                }
                var rel = await RecoveryGetAsync(mb, "https://musicbrainz.org/ws/2/release?release-group=" + candidate.Id + "&limit=100&fmt=json", pace, ct);
                row.RequestStatus = "releases:" + rel.Status;
                if (rel.Body == null) { row.Decision = "release-list-unavailable"; SaveCorroboration(row); evaluated.Add(row); continue; }
                using var releasesDoc = JsonDocument.Parse(rel.Body);
                if (!releasesDoc.RootElement.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array) continue;
                // Do not assert exclusivity if there are uninspected releases.
                var releaseIds = releases.EnumerateArray().Where(r => ValidMbid(RecoverProp(r, "id")))
                    .Select(r => RecoverProp(r, "id")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                bool exhaustive = releaseIds.Length > 0 && releaseIds.Length <= 3 &&
                    (!releasesDoc.RootElement.TryGetProperty("count", out var rc) ||
                     rc.ValueKind != JsonValueKind.Number || (rc.TryGetInt32(out int count) && count == releaseIds.Length));
                if (releaseIds.Length == 0) continue;
                var perSong = songs.ToDictionary(s => s.Row, s => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                foreach (var releaseId in releaseIds.Take(3))
                {
                    var response = await RecoveryGetAsync(mb, "https://musicbrainz.org/ws/2/release/" + releaseId + "?inc=recordings+artist-credits&fmt=json", pace, ct);
                    row.RequestStatus += ";release:" + response.Status;
                    if (response.Body == null) { exhaustive = false; continue; }
                    using var releaseDoc = JsonDocument.Parse(response.Body);
                    foreach (var song in songs)
                        foreach (string found in MusicBrainzTrackIds(releaseDoc.RootElement, song.Song, artist.ArtistId))
                            perSong[song.Row].Add(found);
                    row.ReleaseMbids = row.ReleaseMbids.Length == 0 ? releaseId : row.ReleaseMbids + " | " + releaseId;
                }
                var all = perSong.Where(x => x.Value.Count == 1).Select(x => x.Key).ToHashSet();
                row.TitleMatches = all.Count;
                row.VersionConflict = songs.Any(song => ValidMbid(song.RecordingId) &&
                    perSong[song.Row].Count > 0 && !perSong[song.Row].Contains(song.RecordingId));
                row.MatchedTitles = string.Join(" | ", songs.Where(x => all.Contains(x.Row)).Select(x => x.Song));
                row.RecordingMbids = string.Join(" | ", perSong.Values.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase));
                row.Score = (candidate.Relationship == "exact-normalized-title" ? 30 : 0) + 20 * row.TitleMatches;
                row.Decision = exhaustive ? "release-track-evidence-reviewed" : "partial-release-inventory-review-only";
                row.Reason = exhaustive ? "Complete release-group inventory within bounded scope; artist MBID credited on recordings" :
                    "More releases may exist; checked tracks do not prove uniqueness across all editions";
                SaveCorroboration(row); evaluated.Add(row);
                // Promote only if all releases were checked, the release group is uniquely
                // identified by exact title, and at least two distinct tracks are supported.
                // Never replace an existing, disagreeing recording ID.
                if (candidates.Length == 1 && exhaustive && row.TitleMatches >= 2)
                {
                    foreach (var song in songs)
                    {
                        if (!perSong.TryGetValue(song.Row, out var ids) || ids.Count != 1) continue;
                        var id = ids.Single();
                        if (ValidMbid(song.RecordingId) && !song.RecordingId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                        if (song.SongIdentityStatus == "artist-and-song-supported") continue;
                        song.RecordingId = id; song.ArtistId = artist.ArtistId;
                        song.Status = "artist-supported/version-review";
                        song.SongIdentityStatus = "artist-and-song-supported";
                        song.RecoveryDecision = "whole-album-mbid-track-corroboration";
                        song.Reason = "Unique credited recording on checked releases in an exact-title release group; exact audio master not proven";
                        row.Changed = true;
                        lock (verificationGate) recovery.SongsRecovered++;
                    }
                    if (row.Changed) row.Decision = "whole-album-provisional-song-identity";
                }
            }
            lock (verificationGate) { recovery.Total++; recovery.Phase = "Album-level corroboration: " + source.Artist + " — " + source.Album; }
            RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = source.Artist, Album = source.Album,
                Provider = "MusicBrainz", Action = "release-group-tracklist-corroboration",
                CandidateCount = evaluated.Count.ToString(),
                Outcome = evaluated.Any(x => x.Changed) ? "album-supported-source-rows" : "album-candidates-for-review",
                Validation = "album-group candidates do not establish audio master or permit Lidarr writes",
                Notes = "All candidate scores and release/recording IDs in companion diagnostic CSV" });
            }
            catch (JsonException)
            {
                lock (verificationGate) recovery.Total++;
                RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = group.First().Artist,
                    Album = group.First().Album, Provider = "MusicBrainz",
                    Action = "release-group-tracklist-corroboration",
                    Outcome = "invalid-provider-json", Validation = "No release or track evidence inferred from malformed JSON" });
            }
        }
    }
    // Distinguish a supported *song identity* from an exact-master claim. A single
    // source track can be provisionally mapped only when an entire, small MusicBrainz
    // release-group inventory was checked and all compatible credited tracks use
    // the same recording MBID. An ISRC or a title match alone never suffices.
    static bool SafeSingleReleaseIdentity(bool oneExactAlbumGroup, bool fullReleaseInventory,
        string existingRecordingId, IReadOnlyCollection<string> matchingRecordingIds)
    {
        if (!oneExactAlbumGroup || !fullReleaseInventory || matchingRecordingIds.Count != 1) return false;
        string recoveredId = matchingRecordingIds.First();
        return ValidMbid(recoveredId) &&
            (!ValidMbid(existingRecordingId) || existingRecordingId.Equals(recoveredId, StringComparison.OrdinalIgnoreCase));
    }

    // The previous whole-album analysis deliberately required two unresolved tracks.
    // That missed singles and isolated soundtrack songs. This bounded follow-up is
    // intentionally *not* a general title search: it requires a verified artist MBID
    // and a matching MusicBrainz album/release-group, with credited tracks inspected.
    async Task RunTargetedSingleReleaseChecksAsync(EvidenceAudit audit, HttpClient mb,
        RequestPacer pace, CancellationToken ct)
    {
        var artists = audit.Artists.Where(a => a.Status == "high" && ValidMbid(a.ArtistId))
            .ToDictionary(a => a.SourceName, a => a.ArtistId, StringComparer.OrdinalIgnoreCase);
        var eligible = audit.Songs.Where(s => s.SongIdentityStatus != "artist-and-song-supported" &&
                artists.ContainsKey(s.Artist) && !string.IsNullOrWhiteSpace(s.Album) &&
                s.Album.Trim() != "-" && Normalize(s.Song).Length > 0)
            .GroupBy(s => (Artist: Normalize(s.Artist), Album: Normalize(s.Album)))
            .Select(g => new { Group = g.ToArray(), External = g.Count(x =>
                x.ExternalStatus is "both-catalogs-support-title-and-artist" or "one-catalog-supports-title-and-artist") })
            .Where(g => g.Group.Length > 0)
            .OrderByDescending(g => g.External)
            .ThenByDescending(g => g.Group.Length)
            // Cap *album cohorts*, not song rows. The present 2,505-song fixture
            // contains 148 such cohorts; wider catalogs remain bounded.
            .Take(160).ToArray();
        foreach (var cohort in eligible)
        {
            ct.ThrowIfCancellationRequested();
            var sample = cohort.Group[0];
            string artistId = artists[sample.Artist];
            var distinctSongs = cohort.Group.GroupBy(x => Normalize(x.Song))
                .Where(x => x.Key.Length > 0).Select(x => x.First()).ToArray();
            lock (verificationGate) { recovery.Total++; recovery.Phase =
                "Targeted release checks: " + sample.Artist + " — " + sample.Album; }
            try
            {
                string query = "arid:" + artistId + " AND releasegroup:\"" + EscapeMb(sample.Album) + "\"";
                var search = await RecoveryGetAsync(mb,
                    "https://musicbrainz.org/ws/2/release-group?query=" + Uri.EscapeDataString(query) +
                    "&limit=100&fmt=json", pace, ct);
                var groups = new List<(string Id, string Title)>();
                bool groupSearchComplete = false;
                if (search.Body != null)
                {
                    using var found = JsonDocument.Parse(search.Body);
                    groupSearchComplete = found.RootElement.TryGetProperty("count", out var groupCount) &&
                        groupCount.ValueKind == JsonValueKind.Number &&
                        groupCount.TryGetInt32(out var groupTotal) && groupTotal <= 100;
                    if (found.RootElement.TryGetProperty("release-groups", out var list) &&
                        list.ValueKind == JsonValueKind.Array)
                        foreach (var x in list.EnumerateArray())
                        {
                            if (x.ValueKind != JsonValueKind.Object) continue;
                            string id = RecoverProp(x, "id"), title = RecoverProp(x, "title");
                            if (ValidMbid(id) && AlbumTitleRelationship(sample.Album, title) == "exact-normalized-title" &&
                                !groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                                groups.Add((id, title));
                        }
                }
                var trace = new CorroborationCandidate {
                    Stage = "targeted-song-to-release", SourceArtist = sample.Artist,
                    SourceAlbum = sample.Album, SourceSongs = string.Join(" | ", distinctSongs.Select(x => x.Song)),
                    SourceRows = string.Join(" | ", cohort.Group.Select(x => x.Row)),
                    UniqueSourceTitles = distinctSongs.Length, ArtistIdValidated = true,
                    CandidateType = "MusicBrainz release-group/credited release tracks",
                    NameRelation = groups.Count == 1 && groupSearchComplete ? "exact-normalized-title" : "missing-or-ambiguous-release-group",
                    RequestStatus = "release-group:" + search.Status
                };
                if (groups.Count != 1 || !groupSearchComplete)
                {
                    trace.Decision = !groupSearchComplete ? "release-group-search-incomplete" :
                        groups.Count == 0 ? "no-exact-release-group" : "ambiguous-release-groups";
                    trace.Reason = "A complete unique exact-title MusicBrainz release-group search is required before recording recovery";
                    trace.BestAlternative = string.Join(" | ", groups.Take(5).Select(x => x.Id));
                    SaveCorroboration(trace);
                    RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = sample.Artist,
                        Album = sample.Album, Provider = "MusicBrainz", Action = "targeted-song-release-reconciliation",
                        Outcome = trace.Decision, CandidateCount = groups.Count.ToString(),
                        Validation = "No MusicBrainz recording IDs accepted without a unique release-group" });
                    continue;
                }
                trace.CandidateId = groups[0].Id;
                trace.CandidateName = groups[0].Title;
                var browse = await RecoveryGetAsync(mb,
                    "https://musicbrainz.org/ws/2/release?release-group=" + groups[0].Id +
                    "&limit=100&fmt=json", pace, ct);
                trace.RequestStatus += ";releases:" + browse.Status;
                if (browse.Body == null)
                {
                    trace.Decision = "release-inventory-unavailable"; SaveCorroboration(trace);
                    RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = sample.Artist,
                        Album = sample.Album, Provider = "MusicBrainz", Action = "targeted-song-release-reconciliation",
                        Outcome = trace.Decision });
                    continue;
                }
                string[] releaseIds;
                bool exhaustive;
                using (var doc = JsonDocument.Parse(browse.Body))
                {
                    releaseIds = doc.RootElement.TryGetProperty("releases", out var releases) &&
                        releases.ValueKind == JsonValueKind.Array ?
                        releases.EnumerateArray().Where(x => ValidMbid(RecoverProp(x, "id")))
                            .Select(x => RecoverProp(x, "id")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() :
                        Array.Empty<string>();
                    exhaustive = releaseIds.Length is > 0 and <= 4 &&
                        doc.RootElement.TryGetProperty("count", out var count) &&
                        count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int total) &&
                        total == releaseIds.Length;
                }
                var perSong = distinctSongs.ToDictionary(x => x.Row,
                    _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                foreach (var releaseId in releaseIds.Take(4))
                {
                    var result = await RecoveryGetAsync(mb,
                        "https://musicbrainz.org/ws/2/release/" + releaseId +
                        "?inc=recordings+artist-credits&fmt=json", pace, ct);
                    trace.RequestStatus += ";tracklist:" + result.Status;
                    if (result.Body == null) { exhaustive = false; continue; }
                    trace.ReleaseMbids = trace.ReleaseMbids.Length == 0 ? releaseId :
                        trace.ReleaseMbids + " | " + releaseId;
                    using var releaseDoc = JsonDocument.Parse(result.Body);
                    foreach (var song in distinctSongs)
                        foreach (var recordingId in MusicBrainzTrackIds(releaseDoc.RootElement, song.Song, artistId))
                            perSong[song.Row].Add(recordingId);
                }
                trace.TitleMatches = distinctSongs.Count(x => perSong[x.Row].Count > 0);
                trace.MatchedTitles = string.Join(" | ", distinctSongs.Where(x => perSong[x.Row].Count > 0).Select(x => x.Song));
                trace.ConflictingTitles = string.Join(" | ", distinctSongs.Where(x => perSong[x.Row].Count == 0).Select(x => x.Song));
                trace.RecordingMbids = string.Join(" | ", perSong.Values.SelectMany(x => x)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                trace.VersionConflict = distinctSongs.Any(x => ValidMbid(x.RecordingId) &&
                    perSong[x.Row].Count > 0 && !perSong[x.Row].Contains(x.RecordingId));
                trace.Score = 30 + 20 * trace.TitleMatches;
                trace.Decision = exhaustive ? "release-track-evidence-examined" : "partial-release-inventory";
                trace.Reason = exhaustive ?
                    "All releases in this bounded release-group examined; exact artist MBID and compatible title required" :
                    "Incomplete or large release inventory; never infer uniqueness or replace a recording MBID";
                // Never promote based on partial response, multiple recording IDs, or a
                // previously contradictory recording ID. No exact-audio claims are made.
                foreach (var song in distinctSongs)
                {
                    var ids = perSong[song.Row];
                    if (!SafeSingleReleaseIdentity(true, exhaustive, song.RecordingId, ids)) continue;
                    string id = ids.Single();
                    foreach (var sourceRow in cohort.Group.Where(x => Normalize(x.Song) == Normalize(song.Song)))
                    {
                        if (ValidMbid(sourceRow.RecordingId) &&
                            !sourceRow.RecordingId.Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                        sourceRow.RecordingId = id;
                        sourceRow.ArtistId = artistId;
                        sourceRow.Status = "artist-supported/version-review";
                        sourceRow.SongIdentityStatus = "artist-and-song-supported";
                        sourceRow.RecoveryDecision = "targeted-musicbrainz-release-track-corroboration";
                        sourceRow.Reason = "Unique credited recording across checked exact-album releases; audio master unverified";
                        trace.Changed = true;
                        lock (verificationGate) recovery.SongsRecovered++;
                    }
                }
                if (trace.Changed) trace.Decision = "targeted-release-track-identity-recovered";
                SaveCorroboration(trace);
                RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = sample.Artist,
                    Album = sample.Album, Provider = "MusicBrainz", Action = "targeted-song-release-reconciliation",
                    ProposedArtistMbid = artistId, CandidateCount = perSong.Count.ToString(),
                    Outcome = trace.Decision, CandidateRecordingMbids = trace.RecordingMbids,
                    Validation = "Provisional song identities only; original recording MBID conflicts block promotion",
                    Notes = trace.Reason });
            }
            catch (JsonException)
            {
                RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = sample.Artist,
                    Album = sample.Album, Provider = "MusicBrainz", Action = "targeted-song-release-reconciliation",
                    Outcome = "invalid-provider-json", Validation = "No identities inferred from invalid response" });
            }
        }
    }

    static byte[] BuildCandidateScoresCsv(IEnumerable<CorroborationCandidate> candidates)
    {
        var sb = new StringBuilder();
        sb.Append(CsvLine("Stage", "Source artist", "Source album", "Source songs", "Source rows",
            "Candidate entity ID", "Candidate name", "Candidate type", "Canonical/alias/album relation",
            "Source title count", "Credited matching title count", "Score (NOT probability)", "Lead over next candidate",
            "Next candidate ID", "Validated artist MBID", "Recording MBID candidates", "Release MBIDs examined",
            "Matched titles", "Conflicting titles", "Request HTTP statuses", "Decision", "Decision reason",
            "Version conflict", "Source audit changed", "Lidarr write authorized"));
        foreach (var c in candidates) sb.Append(CsvLine(c.Stage, c.SourceArtist, c.SourceAlbum,
            c.SourceSongs, c.SourceRows, c.CandidateId, c.CandidateName, c.CandidateType,
            c.NameRelation, c.UniqueSourceTitles, c.TitleMatches, c.Score, c.RankMargin,
            c.BestAlternative, c.ArtistIdValidated, c.RecordingMbids, c.ReleaseMbids,
            c.MatchedTitles, c.ConflictingTitles, c.RequestStatus, c.Decision, c.Reason,
            c.VersionConflict, c.Changed, "NO"));
        return new UTF8Encoding(true).GetBytes(sb.ToString());
    }
    async Task SendCandidateScoresCsv(HttpListenerContext ctx)
    {
        byte[] csv;
        lock (verificationGate) csv = BuildCandidateScoresCsv(corroboration);
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        ctx.Response.Headers.Add("Content-Disposition", "attachment; filename=musicbrainz-candidate-scoring-audit.csv");
        ctx.Response.Headers.Add("Cache-Control", "no-store");
        ctx.Response.ContentLength64 = csv.Length;
        await ctx.Response.OutputStream.WriteAsync(csv);
        ctx.Response.Close();
    }
    static void CatalogCorroborationSelfTests()
    {
        static void Check(bool ok, string reason) { if (!ok) throw new Exception("Catalog corroboration test: " + reason); }
        Check(ArtistScore(true, 2, false) > ArtistScore(true, 1, false), "two recordings outrank one");
        Check(AlbumTitleRelationship("Prism (Deluxe)", "Prism") == "edition-or-suffix-variant",
            "deluxe cannot silently be the same edition");
        Check(!ExactCorroborationTitle("Song (Live)", "Song"), "studio and live must differ");
        Check(!ExactCorroborationTitle("Song (Live in Denver)", "Song (Live in London)"), "live venues must differ");
        const string artistId = "056e4f3e-d505-4dad-8ec1-d04f521cbb56";
        const string song = "{\"recordings\":[{\"id\":\"8ea78fd8-9029-4b81-82c3-92c63f5c1c1e\",\"title\":\"One More Time\",\"artist-credit\":[{\"artist\":{\"id\":\"056e4f3e-d505-4dad-8ec1-d04f521cbb56\",\"name\":\"Daft Punk\"}}]}]}";
        Check(RecordingSearchHasCredit(song, "One More Time", artistId, out _), "artist recording credit valid");
        Check(!RecordingSearchHasCredit(song, "One More Time", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", out _), "same title with conflicting artist must reject");
        const string release = "{\"media\":[{\"tracks\":[{\"recording\":{\"id\":\"8ea78fd8-9029-4b81-82c3-92c63f5c1c1e\",\"title\":\"One More Time\",\"artist-credit\":[{\"artist\":{\"id\":\"056e4f3e-d505-4dad-8ec1-d04f521cbb56\"}}]}}]}]}";
        using var doc = JsonDocument.Parse(release);
        Check(MusicBrainzTrackIds(doc.RootElement, "One More Time", artistId).Length == 1, "release track corroboration");
        Check(MusicBrainzTrackIds(doc.RootElement, "Wrong Title", artistId).Length == 0, "unrelated title rejected");
        Check(SafeSingleReleaseIdentity(true, true, "", new[] { "8ea78fd8-9029-4b81-82c3-92c63f5c1c1e" }), "unique complete album recording can be proposed");
        Check(!SafeSingleReleaseIdentity(true, false, "", new[] { "8ea78fd8-9029-4b81-82c3-92c63f5c1c1e" }), "partial album inventory cannot establish identity");
        Check(!SafeSingleReleaseIdentity(false, true, "", new[] { "8ea78fd8-9029-4b81-82c3-92c63f5c1c1e" }), "ambiguous album group must be held");
        Check(!SafeSingleReleaseIdentity(true, true, "", new[] { "8ea78fd8-9029-4b81-82c3-92c63f5c1c1e", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }), "multiple recordings cannot be promoted");
        Check(!SafeSingleReleaseIdentity(true, true, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", new[] { "8ea78fd8-9029-4b81-82c3-92c63f5c1c1e" }), "existing conflicting recording must remain intact");
        var data = Encoding.UTF8.GetString(BuildCandidateScoresCsv(new[] { new CorroborationCandidate { CandidateId = artistId } }));
        Check(data.Contains("Lidarr write authorized") && data.Contains("NO"), "CSV exports do not authorize writes");
        Console.WriteLine("PASS: candidate rankings, artist credits, bounded single-release lookup safeguards, version conflicts and scoring CSV");
    }
}
