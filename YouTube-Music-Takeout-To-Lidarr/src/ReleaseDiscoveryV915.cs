using System.Net;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // v0.9.1.6: Search-count parser fix and recording-first diagnostics. No Lidarr writes.
    // MusicBrainz permits release(-group) search and paginated browse. Paging offsets
    // MUST advance by the actual number of releases returned, not the requested page size.
    sealed class MbGroupChoice
    {
        public string Id = "", Title = "", Discovery = "", Relation = "different-title";
        public int Weight;
    }
    sealed class MbReleaseChoice
    {
        public string Id = "", Title = "", Status = "", Country = "", Date = "";
    }
    sealed class MbReleaseInventory
    {
        public List<MbReleaseChoice> Releases = new();
        public int AdvertisedCount, Pages, LastHttpStatus;
        public bool Complete, Truncated, ProviderFailure;
    }
    const int MbGroupPageSize = 50;
    const int MbReleasePageSize = 25;
    const int MbMaxReleasePages = 4;
    const int MbMaxExaminedReleases = 18;
    const int MbMaxGroupCandidatesToInspect = 2;

    static int AlbumEvidenceWeight(string relation) => relation switch
    {
        "exact-normalized-title" => 100,
        "volume-notation-variant" => 82,
        "unicode-glyph-variant" => 78,
        "ep-suffix-variant" => 65,
        "edition-or-suffix-variant" => 62,
        "release-title-index-hit-review" => 45,
        "indexed-alias-hit-review" => 42,
        _ => 0
    };

    static void AddMbGroup(List<MbGroupChoice> groups, string id, string title, string sourceAlbum, string discovery)
    {
        if (!ValidMbid(id) || string.IsNullOrWhiteSpace(title)) return;
        var relation = AlbumTitleRelationship(sourceAlbum, title);
        int weight = AlbumEvidenceWeight(relation);
        if (weight == 0)
        {
            // Lucene's alias/release indexes are discovery signals only. The
            // group title can differ completely, so retain the returned MBID
            // for human review, never for automatic recording promotion.
            if (discovery == "release-title") relation = "release-title-index-hit-review";
            else if (discovery == "releasegroup-alias-or-release") relation = "indexed-alias-hit-review";
            else return;
            weight = AlbumEvidenceWeight(relation);
        }
        var existing = groups.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (weight > existing.Weight)
            {
                existing.Title = title; existing.Relation = relation; existing.Weight = weight;
            }
            if (!existing.Discovery.Contains(discovery, StringComparison.Ordinal)) existing.Discovery += "+" + discovery;
            return;
        }
        groups.Add(new MbGroupChoice { Id = id, Title = title, Relation = relation, Weight = weight, Discovery = discovery });
    }

    static (int Total, int Returned) ReadGroupCandidates(string json, string sourceAlbum,
        List<MbGroupChoice> output, bool releaseSearch, string discovery)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string arrayName = releaseSearch ? "releases" : "release-groups";
        // MusicBrainz JSON SEARCH endpoints (release and release-group) use
        // top-level `count`. Their BROWSE counterparts use `release-count`
        // and `release-group-count`. Do NOT use those browse keys here.
        if (!root.TryGetProperty(arrayName, out var list) || list.ValueKind != JsonValueKind.Array) return (-1, 0);
        int returned = list.GetArrayLength();
        int total = root.TryGetProperty("count", out var num) && num.TryGetInt32(out int n) && n >= 0 ? n : -1;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (releaseSearch)
            {
                if (item.TryGetProperty("release-group", out var group) && group.ValueKind == JsonValueKind.Object)
                    AddMbGroup(output, RecoverProp(group, "id"), RecoverProp(group, "title"), sourceAlbum, discovery);
            }
            else AddMbGroup(output, RecoverProp(item, "id"), RecoverProp(item, "title"), sourceAlbum, discovery);
        }
        return (total, returned);
    }

    static (int Total, int Returned) ReadBrowsedGroups(string json, string sourceAlbum, List<MbGroupChoice> output)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("release-groups", out var items) || items.ValueKind != JsonValueKind.Array) return (-1, 0);
        int total = root.TryGetProperty("release-group-count", out var num) && num.TryGetInt32(out int n) ? n : -1;
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object)
                AddMbGroup(output, RecoverProp(item, "id"), RecoverProp(item, "title"), sourceAlbum, "artist-catalog-browse");
        return (total, items.GetArrayLength());
    }

    // Only the MusicBrainz documented search fields are used. Lucene queries always
    // anchor searches to a validated artist MBID; title similarity merely discovers
    // possibilities and is never used as proof of a recording/master identity.
    async Task<(MbGroupChoice[] Groups, bool Complete, string Status)> DiscoverReleaseGroupsV915Async(
        HttpClient mb, RequestPacer pace, string artistId, string sourceAlbum, CancellationToken ct)
    {
        var groups = new List<MbGroupChoice>();
        var status = new StringBuilder();
        bool complete = true;
        async Task Search(string entity, string query, string discovery)
        {
            // Search results have top-level `count` and can be paged with
            // offset. Cap at 3 pages per strategy; partial discovery may
            // suggest review but cannot promote a recording ID.
            const int maxSearchPages = 3;
            int offset = 0, advertisedTotal = -1;
            for (int pageNo = 0; pageNo < maxSearchPages; pageNo++)
            {
                ct.ThrowIfCancellationRequested();
                string url = "https://musicbrainz.org/ws/2/" + entity + "?query=" + Uri.EscapeDataString(query) +
                    "&limit=" + MbGroupPageSize + "&offset=" + offset + "&fmt=json";
                var response = await RecoveryGetAsync(mb, url, pace, ct);
                status.Append(discovery).Append('[').Append(offset).Append("]=")
                    .Append(response.Status).Append(';');
                if (response.Body == null) { complete = false; return; }
                var result = ReadGroupCandidates(response.Body, sourceAlbum, groups, entity == "release", discovery);
                if (result.Total < 0 || (advertisedTotal >= 0 && advertisedTotal != result.Total) ||
                    result.Returned > result.Total - offset)
                {
                    status.Append(discovery).Append(":invalid-search-count;");
                    complete = false;
                    return;
                }
                advertisedTotal = result.Total;
                offset += result.Returned;
                status.Append(discovery).Append(":count=").Append(advertisedTotal)
                    .Append(":read=").Append(offset).Append(';');
                if (offset >= advertisedTotal) return;
                if (result.Returned == 0) { complete = false; return; }
            }
            // Result count exceeds the bounded query window. Never infer
            // that the inspected candidates are the full set.
            status.Append(discovery).Append(":search-page-cap;");
            complete = false;
        }
        // Stage 1: original direct title search; preserves prior fast-path behavior.
        await Search("release-group", "arid:" + artistId + " AND releasegroup:\"" + EscapeMb(sourceAlbum) + "\"", "releasegroup-title");
        if (groups.Any(x => x.Relation == "exact-normalized-title"))
            return (groups.OrderByDescending(x => x.Weight).ThenBy(x => x.Id).ToArray(), complete, status.ToString());

        // Stage 2: MB release-group aliases and titles of individual editions.
        await Search("release-group", "arid:" + artistId + " AND (alias:\"" + EscapeMb(sourceAlbum) +
            "\" OR release:\"" + EscapeMb(sourceAlbum) + "\")", "releasegroup-alias-or-release");
        if (groups.Any(x => x.Relation == "exact-normalized-title"))
            return (groups.OrderByDescending(x => x.Weight).ThenBy(x => x.Id).ToArray(), complete, status.ToString());

        // Stage 3: direct release-index lookup, whose results include a release-group ID.
        await Search("release", "arid:" + artistId + " AND release:\"" + EscapeMb(sourceAlbum) + "\"", "release-title");
        if (groups.Count > 0)
            return (groups.OrderByDescending(x => x.Weight).ThenBy(x => x.Id).ToArray(), complete, status.ToString());

        // Stage 4: bounded browse of artist's release groups for punctuation and
        // unusual Unicode mismatches. A partial browse is ALWAYS review-only.
        int offset = 0;
        for (int page = 0; page < 2; page++)
        {
            var browse = await RecoveryGetAsync(mb,
                "https://musicbrainz.org/ws/2/release-group?artist=" + artistId +
                "&limit=100&offset=" + offset + "&fmt=json", pace, ct);
            status.Append("artist-browse[").Append(offset).Append("]=").Append(browse.Status).Append(';');
            if (browse.Body == null) { complete = false; break; }
            var result = ReadBrowsedGroups(browse.Body, sourceAlbum, groups);
            if (result.Total < 0) { complete = false; break; }
            offset += result.Returned;
            if (offset >= result.Total) break;
            if (result.Returned == 0 || page == 1) { complete = false; break; }
        }
        return (groups.OrderByDescending(x => x.Weight).ThenBy(x => x.Id).ToArray(), complete, status.ToString());
    }

    static (int Count, MbReleaseChoice[] Items) ParseReleaseBrowseV915(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("release-count", out var count) || !count.TryGetInt32(out int n) ||
            !root.TryGetProperty("releases", out var releases) || releases.ValueKind != JsonValueKind.Array)
            return (-1, Array.Empty<MbReleaseChoice>());
        var list = new List<MbReleaseChoice>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;
            var id = RecoverProp(release, "id");
            if (!ValidMbid(id)) continue;
            list.Add(new MbReleaseChoice { Id = id, Title = RecoverProp(release, "title"),
                Status = RecoverProp(release, "status"), Country = RecoverProp(release, "country"),
                Date = RecoverProp(release, "date") });
        }
        return (n, list.ToArray());
    }

    async Task<MbReleaseInventory> BrowseReleaseInventoryV915Async(HttpClient mb, RequestPacer pace,
        string groupId, CancellationToken ct)
    {
        var result = new MbReleaseInventory();
        int offset = 0;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int page = 0; page < MbMaxReleasePages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var response = await RecoveryGetAsync(mb,
                "https://musicbrainz.org/ws/2/release?release-group=" + groupId +
                "&limit=" + MbReleasePageSize + "&offset=" + offset + "&fmt=json", pace, ct);
            result.LastHttpStatus = response.Status;
            if (response.Body == null) { result.ProviderFailure = true; break; }
            var chunk = ParseReleaseBrowseV915(response.Body);
            if (chunk.Count < 0 || (page > 0 && chunk.Count != result.AdvertisedCount))
            { result.ProviderFailure = true; break; }
            result.AdvertisedCount = chunk.Count;
            result.Pages++;
            foreach (var r in chunk.Items) if (ids.Add(r.Id)) result.Releases.Add(r);
            // NB: the API may return fewer than the requested page size because
            // it also limits the total number of tracks in each response.
            offset += chunk.Items.Length;
            if (offset >= chunk.Count) { result.Complete = !result.ProviderFailure; break; }
            if (chunk.Items.Length == 0) { result.ProviderFailure = true; break; }
        }
        result.Truncated = !result.Complete;
        return result;
    }

    static MbReleaseChoice[] PrioritizeReleasesV915(string sourceAlbum, IEnumerable<MbReleaseChoice> releases)
    {
        return releases.OrderByDescending(r => AlbumEvidenceWeight(AlbumTitleRelationship(sourceAlbum, r.Title)))
            .ThenByDescending(r => r.Status.Equals("Official", StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.Date, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Recording-first *discovery*, not recording acceptance: a source album
    // may have no searchable release group (single, soundtrack, compilation,
    // or incomplete MusicBrainz release catalog). Search one source title
    // against the independently validated artist MBID and export candidates.
    // Search results can include alternate masters, so never write an MBID
    // to the audit on this fallback path.
    async Task RecordFirstCandidateV916Async(HttpClient mb, RequestPacer pace,
        string artistId, string sourceArtist, string sourceAlbum,
        IReadOnlyList<SongAudit> songs, CancellationToken ct)
    {
        // Reuse the existing verified artist-credit/title parser and avoid
        // broad artist-only searches. One bounded query per missing album.
        var source = songs.FirstOrDefault();
        if (source == null) return;
        string query = "arid:" + artistId + " AND recording:\"" + EscapeMb(source.Song) + "\"";
        var requestUrl = "https://musicbrainz.org/ws/2/recording?query=" + Uri.EscapeDataString(query) +
            "&limit=15&fmt=json";
        var result = await RecoveryGetAsync(mb, requestUrl, pace, ct);
        var initialStatus = result.Status;
        // Normal recovery already retries transient 5xx responses three times.
        // If 503 persists, make ONE additional rate-paced, bounded attempt; then
        // preserve the provider-unavailable diagnostic instead of claiming no match.
        if (result.Body == null && result.Status == 503)
        {
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            result = await RecoveryGetAsync(mb, requestUrl, pace, ct);
        }
        var candidate = new CorroborationCandidate {
            Stage = "v0916-recording-first-review", SourceArtist = sourceArtist,
            SourceAlbum = sourceAlbum, SourceSongs = source.Song,
            SourceRows = string.Join(" | ", songs.Select(x => x.Row)),
            CandidateType = "MusicBrainz recording search", ArtistIdValidated = true,
            RequestStatus = "recording-search:" + initialStatus +
                (initialStatus == 503 ? ";503-retry:" + result.Status : ""),
            Decision = "recording-first-review-only",
            Reason = "No related release group. Artist-credit and version-compatible title evidence is provisional; recording ID cannot be assigned from a search result alone."
        };
        if (result.Body == null)
        {
            candidate.Decision = "recording-first-provider-unavailable";
        }
        else
        {
            bool hasMatch = RecordingSearchHasCredit(result.Body, source.Song, artistId, out var ids);
            candidate.RecordingMbids = ids;
            candidate.TitleMatches = hasMatch ? 1 : 0;
            candidate.MatchedTitles = hasMatch ? source.Song : "";
            candidate.ConflictingTitles = hasMatch ? "" : source.Song;
            candidate.Decision = hasMatch ? "recording-first-candidates-review-only" : "recording-first-no-credit-title-match";
        }
        SaveCorroboration(candidate);
    }

    async Task RunTargetedReleaseDiscoveryV915Async(EvidenceAudit audit, HttpClient mb,
        RequestPacer pace, CancellationToken ct)
    {
        var artists = audit.Artists.Where(a => a.Status == "high" && ValidMbid(a.ArtistId))
            .ToDictionary(a => a.SourceName, a => a.ArtistId, StringComparer.OrdinalIgnoreCase);
        var cohorts = audit.Songs.Where(s => s.SongIdentityStatus != "artist-and-song-supported" &&
                artists.ContainsKey(s.Artist) && !string.IsNullOrWhiteSpace(s.Album) &&
                s.Album.Trim() != "-" && Normalize(s.Song).Length > 0)
            .GroupBy(s => (Artist: Normalize(s.Artist), Album: Normalize(s.Album)))
            .Select(g => new { Songs = g.ToArray(), External = g.Count(x =>
                x.ExternalStatus is "both-catalogs-support-title-and-artist" or "one-catalog-supports-title-and-artist") })
            .OrderByDescending(g => g.External).ThenByDescending(g => g.Songs.Length)
            .Take(160).ToArray();
        foreach (var cohort in cohorts)
        {
            ct.ThrowIfCancellationRequested();
            var source = cohort.Songs[0];
            var artistId = artists[source.Artist];
            var unique = cohort.Songs.GroupBy(s => Normalize(s.Song)).Where(g => g.Key.Length > 0)
                .Select(g => g.First()).ToArray();
            lock (verificationGate)
            {
                recovery.Total++;
                recovery.Phase = "v0.9.1.5 release discovery: " + source.Artist + " — " + source.Album;
            }
            try
            {
                var discovered = await DiscoverReleaseGroupsV915Async(mb, pace, artistId, source.Album, ct);
                var choices = discovered.Groups;
                if (choices.Length == 0)
                {
                    SaveCorroboration(new CorroborationCandidate {
                        Stage = "v0915-release-discovery", SourceArtist = source.Artist, SourceAlbum = source.Album,
                        SourceRows = string.Join(" | ", cohort.Songs.Select(s => s.Row)),
                        SourceSongs = string.Join(" | ", unique.Select(s => s.Song)),
                        Decision = "no-album-relation-found", RequestStatus = discovered.Status,
                        Reason = "Exact, alias, edition and bounded artist-group searches yielded no related release group" });
                    RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = source.Artist,
                        Album = source.Album, Provider = "MusicBrainz", Action = "v0915-release-discovery",
                        Outcome = "no-album-relation-found", Validation = discovered.Status });
                    await RecordFirstCandidateV916Async(mb, pace, artistId, source.Artist,
                        source.Album, cohort.Songs, ct);
                    continue;
                }
                int topWeight = choices[0].Weight;
                bool uniqueTop = choices.Count(x => x.Weight == topWeight) == 1;
                int examined = 0;
                foreach (var choice in choices.Take(MbMaxGroupCandidatesToInspect))
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new CorroborationCandidate {
                        Stage = "v0915-release-discovery", SourceArtist = source.Artist,
                        SourceAlbum = source.Album, SourceSongs = string.Join(" | ", unique.Select(x => x.Song)),
                        SourceRows = string.Join(" | ", cohort.Songs.Select(x => x.Row)),
                        CandidateId = choice.Id, CandidateName = choice.Title, CandidateType = "MusicBrainz release group",
                        NameRelation = choice.Relation, Score = choice.Weight, UniqueSourceTitles = unique.Length,
                        ArtistIdValidated = true, BestAlternative = choices.FirstOrDefault(x => x.Id != choice.Id)?.Id ?? "",
                        RankMargin = choice.Weight - (choices.Where(x => x.Id != choice.Id).Select(x => x.Weight).DefaultIfEmpty(0).Max()),
                        RequestStatus = "discover:" + discovered.Status + ";method:" + choice.Discovery
                    };
                    var inventory = await BrowseReleaseInventoryV915Async(mb, pace, choice.Id, ct);
                    row.RequestStatus += ";browse-pages:" + inventory.Pages + ";browse-http:" + inventory.LastHttpStatus;
                    row.Reason = "Releases=" + inventory.Releases.Count + "/" + inventory.AdvertisedCount +
                        "; page-complete=" + inventory.Complete + "; title-relation=" + choice.Relation;
                    var prioritized = PrioritizeReleasesV915(source.Album, inventory.Releases);
                    var selected = prioritized.Take(MbMaxExaminedReleases).ToArray();
                    bool allInspected = inventory.Complete && !inventory.ProviderFailure &&
                        selected.Length == inventory.Releases.Count && selected.Length > 0;
                    var matches = unique.ToDictionary(x => x.Row, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    int failed = 0;
                    foreach (var release in selected)
                    {
                        var result = await RecoveryGetAsync(mb,
                            "https://musicbrainz.org/ws/2/release/" + release.Id +
                            "?inc=recordings+artist-credits&fmt=json", pace, ct);
                        if (result.Body == null) { failed++; continue; }
                        row.ReleaseMbids += (row.ReleaseMbids.Length == 0 ? "" : " | ") + release.Id;
                        using var releaseDoc = JsonDocument.Parse(result.Body);
                        foreach (var song in unique)
                            foreach (var id in MusicBrainzTrackIds(releaseDoc.RootElement, song.Song, artistId))
                                matches[song.Row].Add(id);
                    }
                    allInspected = allInspected && failed == 0;
                    row.TitleMatches = unique.Count(s => matches[s.Row].Count > 0);
                    row.MatchedTitles = string.Join(" | ", unique.Where(s => matches[s.Row].Count > 0).Select(s => s.Song));
                    row.ConflictingTitles = string.Join(" | ", unique.Where(s => matches[s.Row].Count == 0).Select(s => s.Song));
                    row.RecordingMbids = string.Join(" | ", matches.Values.SelectMany(x => x)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                    row.VersionConflict = unique.Any(s => ValidMbid(s.RecordingId) &&
                        matches[s.Row].Count > 0 && !matches[s.Row].Contains(s.RecordingId));
                    row.Reason += "; tracklists-inspected=" + (selected.Length - failed) + "/" + selected.Length +
                        "; competing-groups=" + choices.Length + "; complete-evidence=" + allInspected;
                    bool exactTop = uniqueTop && choice.Id == choices[0].Id &&
                        choice.Relation == "exact-normalized-title" && discovered.Complete;
                    if (!exactTop)
                    {
                        row.Decision = !uniqueTop ? "multiple-related-release-groups-review-only" :
                            choice.Relation != "exact-normalized-title" ? "related-edition-needs-approval" :
                            "incomplete-discovery-review-only";
                    }
                    else if (!allInspected)
                        row.Decision = "partial-release-inventory-review-only";
                    else row.Decision = "fully-checked-exact-release-group";
                    if (exactTop && allInspected)
                    {
                        foreach (var song in unique)
                        {
                            var candidates = matches[song.Row];
                            if (!SafeSingleReleaseIdentity(true, true, song.RecordingId, candidates)) continue;
                            string recording = candidates.Single();
                            foreach (var sourceRow in cohort.Songs.Where(x => Normalize(x.Song) == Normalize(song.Song)))
                            {
                                if (ValidMbid(sourceRow.RecordingId) &&
                                    !sourceRow.RecordingId.Equals(recording, StringComparison.OrdinalIgnoreCase)) continue;
                                sourceRow.RecordingId = recording;
                                sourceRow.ArtistId = artistId;
                                sourceRow.Status = "artist-supported/version-review";
                                sourceRow.SongIdentityStatus = "artist-and-song-supported";
                                sourceRow.RecoveryDecision = "v0915-complete-album-track-credit-recovery";
                                sourceRow.Reason = "Unique credited recording in a fully examined exact release group; master not proven";
                                row.Changed = true;
                                lock (verificationGate) recovery.SongsRecovered++;
                            }
                        }
                    }
                    if (row.Changed) row.Decision = "provisional-identities-recovered";
                    SaveCorroboration(row);
                    examined++;
                }
                RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = source.Artist,
                    Album = source.Album, Provider = "MusicBrainz", Action = "v0915-release-discovery",
                    CandidateCount = choices.Length.ToString(),
                    Outcome = "candidate-groups=" + choices.Length + "; inspected=" + examined,
                    ProposedArtistMbid = artistId,
                    Validation = "Only a unique exact-title group with FULL release inventory can add provisional MBIDs; edition variants require user review",
                    Notes = discovered.Status });
            }
            catch (JsonException)
            {
                RecordRecovery(new RecoveryDiagnostic { Entity = "album", Artist = source.Artist,
                    Album = source.Album, Provider = "MusicBrainz", Action = "v0915-release-discovery",
                    Outcome = "invalid-provider-json", Validation = "Malformed MusicBrainz response; no recovery" });
            }
        }
    }

    static void ReleaseDiscoveryV915SelfTests()
    {
        static void Check(bool value, string message)
        { if (!value) throw new Exception("v0.9.1.5 release discovery self-test: " + message); }
        const string one = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var choices = new List<MbGroupChoice>();
        AddMbGroup(choices, one, "Bangarang", "Bangarang EP", "release-alias");
        Check(choices.Count == 1 && choices[0].Relation == "ep-suffix-variant", "EP suffix suggests review only");
        AddMbGroup(choices, one, "Bangarang", "Bangarang EP", "release-index");
        Check(choices.Count == 1, "repeated release-group IDs deduplicated");
        Check(AlbumEvidenceWeight("exact-normalized-title") > AlbumEvidenceWeight("edition-or-suffix-variant"),
            "exact album name ranks above edition similarity");
        Check(AlbumEvidenceWeight("different-title") == 0, "unrelated album cannot be suggested by browse");
        var indexed = new List<MbGroupChoice>();
        AddMbGroup(indexed, one, "A different release-group name", "MTV Unplugged (Live)", "release-title");
        Check(indexed.Count == 1 && indexed[0].Relation == "release-title-index-hit-review",
            "verified artist release title search remains human-review only when group title differs");
        const string groupJson = "{\"count\":1,\"release-groups\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"Album\"}]}";
        var groupHits = new List<MbGroupChoice>();
        var groupPage = ReadGroupCandidates(groupJson, "Album", groupHits, false, "releasegroup-title");
        Check(groupPage.Total == 1 && groupPage.Returned == 1 &&
            groupHits.Count == 1 && groupHits[0].Relation == "exact-normalized-title",
            "parse complete release-group search response");
        const string releaseIndex = "{\"count\":1,\"releases\":[{\"title\":\"Album (Deluxe)\",\"release-group\":{\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\",\"title\":\"Album\"}}]}";
        var directHits = new List<MbGroupChoice>();
        var directPage = ReadGroupCandidates(releaseIndex, "Album (Deluxe)", directHits, true, "release-title");
        Check(directPage.Total == 1 && directHits.Count == 1 && directHits[0].Relation == "edition-or-suffix-variant",
            "release-index lookup retains parent release-group as edition review");
        const string oldIncorrectSearch = "{\"release-group-count\":1,\"release-groups\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"Album\"}]}";
        Check(ReadGroupCandidates(oldIncorrectSearch, "Album", new List<MbGroupChoice>(), false,
            "releasegroup-title").Total == -1, "browse-specific count is not a valid search count");
        var browseGroups = new List<MbGroupChoice>();
        Check(ReadBrowsedGroups(oldIncorrectSearch, "Album", browseGroups).Total == 1 &&
            browseGroups.Count == 1, "release-group BROWSE still uses release-group-count");
        const string partialSearch = "{\"count\":51,\"release-groups\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"Album\"}]}";
        var partial = ReadGroupCandidates(partialSearch, "Album", new List<MbGroupChoice>(), false,
            "releasegroup-title");
        Check(partial.Total == 51 && partial.Returned == 1,
            "search response reports remaining pages correctly");
        // Regression cases from the v0.9.1.5 candidate audit. The source
        // previously reported complete 1/1 release inventories but rejected
        // both identities because the SEARCH count field was misread.
        const string closureGroup = "{\"count\":1,\"release-groups\":[{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"I'm Gone\"}]}";
        var closure = new List<MbGroupChoice>();
        var closurePage = ReadGroupCandidates(closureGroup, "I'm Gone", closure, false, "releasegroup-title");
        Check(closurePage.Total == 1 && closurePage.Returned == 1 && closure.Count == 1 &&
            closure[0].Relation == "exact-normalized-title" &&
            SafeSingleReleaseIdentity(true, true, "", new[] { "1c979b61-8f7c-4657-924d-c8818b18f396" }),
            "Closure / I'm Gone: complete search + unique compatible recording can be provisionally supported");
        const string samGroup = "{\"count\":1,\"release-groups\":[{\"id\":\"cccccccc-cccc-cccc-cccc-cccccccccccc\",\"title\":\"Ready Set Let's Go\"}]}";
        var sam = new List<MbGroupChoice>();
        var samPage = ReadGroupCandidates(samGroup, "Ready Set Let's Go", sam, false, "releasegroup-title");
        Check(samPage.Total == 1 && samPage.Returned == 1 && sam.Count == 1 &&
            sam[0].Relation == "exact-normalized-title" &&
            SafeSingleReleaseIdentity(true, true, "", new[] { "0027b2fb-3fdd-43af-83cf-692a812ed664" }),
            "Sam Tinnesz / Ready Set Let's Go: corrected search completeness permits safe provisional candidate");
        const string pages = "{\"release-count\":7,\"releases\":[{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"title\":\"Album\"},{\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"Album (Deluxe)\"}]}";
        var parsed = ParseReleaseBrowseV915(pages);
        Check(parsed.Count == 7 && parsed.Items.Length == 2, "advance browse offset by actual returned count, not limit");
        var ranked = PrioritizeReleasesV915("Album", parsed.Items);
        Check(ranked[0].Title == "Album", "related album editions ranked but not considered identical");
        Check(!SafeSingleReleaseIdentity(true, false, "", new[] { one }),
            "incomplete release inventory cannot promote an identity");
        Check(!SafeSingleReleaseIdentity(false, true, "", new[] { one }),
            "similar release edition cannot promote a recording");
        Check(!SafeSingleReleaseIdentity(true, true, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", new[] { one }),
            "existing MBID conflict must remain unresolved");
        Check(!SafeSingleReleaseIdentity(true, true, "", new[] { one, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" }),
            "multiple version MBIDs block auto-acceptance");
        Console.WriteLine("PASS: v0.9.1.6 search-versus-browse counts, search paging fixtures, release safeguards");
    }
}
