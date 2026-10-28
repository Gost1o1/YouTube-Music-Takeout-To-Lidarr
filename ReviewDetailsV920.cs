using System.Net;
using System.Text.Json;

sealed partial class DesktopServer
{
    // Metadata and external previews are OPTIONAL, display-only evidence.
    // Never promote identity confidence, nor save downloaded media in this path.
    sealed record ReviewMediaResult(string Name, string ArtworkUrl, string PreviewUrl, string Album,
        string Credit, string Note, string Availability = "", string SourceTitle = "", string CatalogTrackId = "");
    readonly Dictionary<string, ReviewMediaResult> reviewMediaCache = new(StringComparer.Ordinal);
    readonly object reviewMediaGate = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ReviewMediaResult>> sourceInfoTasks = new();
    Task<ReviewMediaResult> CachedYouTubeInfoAsync(string url)
    {
        if (sourceInfoTasks.Count > 3000) sourceInfoTasks.Clear();
        return sourceInfoTasks.GetOrAdd(url ?? "", u => TryYouTubeSourceInfoAsync(u));
    }
    void InvalidateReviewMedia(string key)
    {
        lock (reviewMediaGate)
        {
            foreach (var k in reviewMediaCache.Keys.Where(k => k.StartsWith(key + ":", StringComparison.Ordinal)).ToArray())
                reviewMediaCache.Remove(k);
        }
    }
    static bool SafeAppleMediaUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        return u.Host.Equals("itunes.apple.com", StringComparison.OrdinalIgnoreCase) ||
            u.Host.EndsWith(".itunes.apple.com", StringComparison.OrdinalIgnoreCase) ||
            u.Host.Equals("mzstatic.com", StringComparison.OrdinalIgnoreCase) ||
            u.Host.EndsWith(".mzstatic.com", StringComparison.OrdinalIgnoreCase);
    }
    static string SafeProperty(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    async Task<ReviewMediaResult> FindAppleReviewMediaAsync(string artist, string title)
    {
        if (artist.Length == 0 || title.Length == 0 || artist.Length > 220 || title.Length > 260)
            return new ReviewMediaResult("", "", "", "", "", "No audio or artwork is available for this source.");
        var url = "https://itunes.apple.com/search?entity=song&limit=20&term=" + Uri.EscapeDataString(artist + " " + title);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(12) };
            using var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_000_000)
                return new ReviewMediaResult("", "", "", "", "", "Preview unavailable.");
            var json = await response.Content.ReadAsStringAsync();
            if (json.Length > 1_000_000) return new ReviewMediaResult("", "", "", "", "", "Preview unavailable.");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var list) || list.ValueKind != JsonValueKind.Array)
                return new ReviewMediaResult("", "", "", "", "", "Preview unavailable.");
            foreach (var item in list.EnumerateArray())
            {
                var itemTitle = SafeProperty(item, "trackName");
                var itemArtist = SafeProperty(item, "artistName");
                // Avoid representing an unrelated track as the source song.
                if (Normalize(itemTitle) != Normalize(title) || Normalize(itemArtist) != Normalize(artist)) continue;
                var cover = SafeProperty(item, "artworkUrl100");
                var preview = SafeProperty(item, "previewUrl");
                if (!SafeAppleMediaUrl(cover)) cover = "";
                if (!SafeAppleMediaUrl(preview)) preview = "";
                var appleId = item.TryGetProperty("trackId", out var trackId) &&
                    trackId.ValueKind == JsonValueKind.Number ? trackId.ToString() : "";
                return new ReviewMediaResult("", cover, preview, SafeProperty(item, "collectionName"), itemArtist,
                    "Apple catalog preview; not proof of the exact YouTube recording or album edition.", CatalogTrackId: appleId);
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (JsonException) { }
        return new ReviewMediaResult("", "", "", "", "", "No verified preview available for this song and artist.");
    }
    // YouTube oEmbed supplies public metadata without asking the browser to scrape
    // cross-origin YouTube HTML. Its failure does NOT prove a link is unlisted.
    static bool SafeYouTubeVideo(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase)) &&
        u.AbsolutePath == "/watch" &&
        System.Text.RegularExpressions.Regex.IsMatch(u.Query, @"(?:^|[?&])v=[A-Za-z0-9_-]{11}(?:&|$)");
    static bool SafeYouTubeThumbnail(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("i.ytimg.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("img.youtube.com", StringComparison.OrdinalIgnoreCase));
    async Task<ReviewMediaResult> TryYouTubeSourceInfoAsync(string? sourceUrl)
    {
        if (!SafeYouTubeVideo(sourceUrl))
            return new ReviewMediaResult("", "", "", "", "", "", "No accessible original YouTube link recorded.");
        var endpoint = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(sourceUrl!);
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(9) };
            using var response = await client.GetAsync(endpoint);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 100_000)
                return new ReviewMediaResult("", "", "", "", "", "", "YouTube metadata unavailable; the video may be private, removed, restricted or temporarily inaccessible.");
            var json = await response.Content.ReadAsStringAsync();
            if (json.Length > 100_000) return new ReviewMediaResult("", "", "", "", "", "", "YouTube metadata unavailable.");
            using var doc = JsonDocument.Parse(json);
            var picture = SafeProperty(doc.RootElement, "thumbnail_url");
            if (!SafeYouTubeThumbnail(picture)) picture = "";
            return new ReviewMediaResult("", picture, "", "", SafeProperty(doc.RootElement, "author_name"),
                "YouTube video metadata, not a verified song or artist identity.",
                "YouTube link available", SafeProperty(doc.RootElement, "title"));
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        catch (JsonException) { }
        return new ReviewMediaResult("", "", "", "", "", "", "YouTube metadata unavailable; this does not prove the video was unlisted.");
    }
    async Task SendReviewCandidateDetails(HttpListenerContext ctx, string key, string candidateId)
    {
        ReviewItem? item;
        ReviewChoice? choice = null;
        lock (verificationGate)
        {
            item = BuildReviewQueue().FirstOrDefault(x => x.Key == key);
            if (item != null && candidateId.Length > 0)
                choice = item.Candidates.FirstOrDefault(x => x.Id == candidateId);
        }
        if (item == null || (candidateId.Length > 0 && choice == null))
            throw new InvalidOperationException("That review candidate is no longer part of the current audit.");
        // The request can only select a UUID from THIS review row. It cannot
        // be used to fetch arbitrary sites or arbitrary MusicBrainz entities.
        if (candidateId.Length > 0 && !ValidMbid(candidateId))
            throw new InvalidOperationException("Invalid candidate identity.");
        var overrides = GetManualFallback(key);
        var cacheKey = key + ":" + candidateId;
        ReviewMediaResult? cached;
        lock (reviewMediaGate) reviewMediaCache.TryGetValue(cacheKey, out cached);
        if (cached != null) { await Respond(ctx, 200, cached); return; }
        string name = candidateId.Length > 0 && item.Kind == "artist" ? "" : overrides?.Artist ?? item.Artist;
        string title = overrides?.Song ?? item.Song;
        if (title is "" or "-")
        {
            var linked = await CachedYouTubeInfoAsync(overrides?.YouTubeUrl ?? item.YouTubeUrl);
            if (!string.IsNullOrWhiteSpace(linked.SourceTitle)) title = linked.SourceTitle;
        }
        if (candidateId.Length > 0)
        {
            // MusicBrainz artist or recording names are looked up, never
            // inferred from the UUID or a generic "candidate" field.
            var entity = item.Kind == "artist" ? "artist" : "recording";
            var endpoint = "https://musicbrainz.org/ws/2/" + entity + "/" + candidateId +
                (entity == "recording" ? "?inc=artist-credits&fmt=json" : "?inc=aliases&fmt=json");
            try
            {
                using var mb = CreateMbClient();
                var response = await UnifiedMusicBrainzGetAsync(mb, endpoint, sharedMbPacer, CancellationToken.None);
                if (response.Body != null)
                {
                    using var doc = JsonDocument.Parse(response.Body);
                    var entityName = SafeProperty(doc.RootElement, entity == "artist" ? "name" : "title");
                    if (entityName.Length > 0) name = entityName;
                    if (entity == "recording" && entityName.Length > 0) title = entityName;
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            catch (JsonException) { }
            // Fallback keeps descriptive audit evidence, never a made-up artist name.
            if (item.Kind == "artist" && name.Length == 0 && choice != null &&
                choice.Label != "MusicBrainz artist candidate" && choice.Label != "Lidarr artist candidate" &&
                choice.Label != "Prior artist MBID") name = choice.Label;
        }
        // When reviewing a recording, the artist comes from the Takeout; when
        // reviewing an artist, it uses the identified MB artist name where available.
        var lookupArtist = item.Kind == "artist" ? name : overrides?.Artist ?? item.Artist;
        var media = await FindAppleReviewMediaAsync(lookupArtist, title);
        if (candidateId.Length == 0)
        {
            var sourceInfo = await CachedYouTubeInfoAsync(overrides?.YouTubeUrl ?? item.YouTubeUrl);
            media = media with {
                ArtworkUrl = sourceInfo.ArtworkUrl.Length > 0 ? sourceInfo.ArtworkUrl : media.ArtworkUrl,
                Availability = sourceInfo.Availability, SourceTitle = overrides?.Song ?? sourceInfo.SourceTitle
            };
        }
        var result = media with {
            Name = candidateId.Length > 0 ? (item.Kind == "artist" ? name : title) : overrides?.Artist ?? item.Artist,
            Album = candidateId.Length == 0 && overrides != null ? overrides.Album : media.Album
        };
        lock (reviewMediaGate) { if (reviewMediaCache.Count > 300) reviewMediaCache.Clear(); reviewMediaCache[cacheKey] = result; }
        await Respond(ctx, 200, result);
    }
    static void ReviewMediaSelfTests()
    {
        if (!SafeYouTubeVideo("https://www.youtube.com/watch?v=abcdefghijk") ||
            SafeYouTubeVideo("https://evil.example/watch?v=abcdefghijk") ||
            !SafeYouTubeThumbnail("https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg"))
            throw new Exception("YouTube metadata URL whitelist regression.");
        if (!SafeAppleMediaUrl("https://is1-ssl.mzstatic.com/image/thumb/foo.png") ||
            SafeAppleMediaUrl("http://itunes.apple.com/song") || SafeAppleMediaUrl("https://mzstatic.com.evil.example/file"))
            throw new Exception("Review-media source URL restrictions are invalid.");
        Console.WriteLine("PASS: review-media source restrictions and candidate binding safeguards");
    }
}
