using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Native .NET application: no PowerShell runtime, command shell, VBScript or script-policy changes.
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
    readonly object verificationGate = new();
    VerifyState verification = new();
    CancellationTokenSource? verificationCancellation;

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
        Console.WriteLine("YouTube Music to Lidarr — .NET v0.8.5 (confidence audit)");
        Console.WriteLine("No PowerShell scripts are run. No changes to Windows execution policy.");
        Console.WriteLine("Read-only: import, inspect, and Lidarr inventory only. No downloads or imports.");
        Console.WriteLine("Local address: " + address);
        Console.WriteLine("Press Ctrl+C to close the app.");
        Log("Listening on " + address);
        using var stop = new CancellationTokenSource();
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
        lidarrKey = "";
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
            ctx.Response.Headers.Add("Content-Security-Policy", "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'");
            ctx.Response.Headers.Add("Cache-Control", "no-store");
            ctx.Response.Headers.Add("X-Content-Type-Options", "nosniff");
            ctx.Response.Headers.Add("Referrer-Policy", "no-referrer");
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close(); return;
        }
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || req.HttpMethod != "POST") { await Respond(ctx, 404, new { error = "Not found." }); return; }
        if (req.Headers["X-Local-Session"] != token) { await Respond(ctx, 403, new { error = "Invalid session." }); return; }
        try
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            if (body.Length > 65536) throw new InvalidOperationException("Request too large.");
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            string Value(string name) => json.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            switch (path)
            {
                case "/api/heartbeat": await Respond(ctx, 200, new { ok = true }); return;
                case "/api/state":
                    Track[] current; string selected, err; bool active;
                    lock (gate) { current = tracks.ToArray(); selected = archive; err = analysisError; active = analyzing; }
                    await Respond(ctx, 200, new { archive = selected, analyzing = active, analysisError = err, summary = current.Length == 0 ? null : new { songs = current.Length, artists = current.Select(t => t.Artist).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(), warning = "Source artist strings are not yet verified identities." }, tracks = current.Select(t => new { t.Artist, t.Song, t.YouTubeUrl }), lidarrUrl, lidarrConnected = connected }); return;
                case "/api/scan":
                    var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    var files = Directory.Exists(downloads) ? Directory.GetFiles(downloads, "*.zip").Where(p => Path.GetFileName(p).StartsWith("takeout", StringComparison.OrdinalIgnoreCase)).OrderByDescending(File.GetLastWriteTimeUtc) : Enumerable.Empty<string>();
                    string supplied = Value("path");
                    string? selectedFile = !string.IsNullOrWhiteSpace(supplied) && ContainsMusicCsv(supplied) ? supplied : files.FirstOrDefault(ContainsMusicCsv);
                    lock (gate) { archive = selectedFile ?? ""; }
                    await Respond(ctx, 200, new { found = selectedFile != null, archive = selectedFile ?? "", message = selectedFile is null ? "No YouTube Music Takeout ZIP with 'music library songs.csv' found in Downloads. Move your Takeout ZIP there." : "" }); return;
                case "/api/analyze":
                    if (string.IsNullOrWhiteSpace(archive)) throw new InvalidOperationException("Scan for a Takeout ZIP first.");
                    lock (verificationGate) { if (verification.Running) throw new InvalidOperationException("Cannot change Takeout data while verification is running."); verification = new VerifyState(); latestAudit = null; }
                    lock (gate) { tracks.Clear(); analysisError = ""; analyzing = true; }
                    _ = Task.Run(() => { try { var imported = ParseMusicZip(archive); lock (gate) { tracks.Clear(); tracks.AddRange(imported); } Log($"Imported {imported.Count} Takeout song rows."); } catch (Exception ex) { lock (gate) { analysisError = ex.Message; } Log("Takeout error: " + ex); } finally { lock (gate) { analyzing = false; } } });
                    await Respond(ctx, 200, new { started = true }); return;
                case "/api/lidarr/check":
                    var checkUrl = ValidateLidarr(Value("url"));
                    using (var check = await http.GetAsync(checkUrl + "/api/v1/system/status"))
                        await Respond(ctx, 200, new { found = true, url = checkUrl, requiresApiKey = check.StatusCode == HttpStatusCode.Unauthorized || check.StatusCode == HttpStatusCode.Forbidden });
                    return;
                case "/api/lidarr/connect":
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
                        lidarrArtists = artistJson.RootElement.EnumerateArray().Select(x => new LidarrArtist(x.TryGetProperty("artistName", out var n) ? n.GetString() ?? "" : "", x.TryGetProperty("foreignArtistId", out var i) ? i.GetString() ?? "" : "")).ToList();
                        lidarrUrl = uri; lidarrKey = key; connected = true;
                        Log($"Lidarr inventory returned {lidarrArtists.Count} artists from {uri}; first names: {string.Join(", ", lidarrArtists.Take(3).Select(x => x.artistName))}");
                        await Respond(ctx, 200, new { connected = true, artists = lidarrArtists.Count, inventoryWarning = lidarrArtists.Count <= 1 ? "Lidarr reports one or fewer artists. Please confirm this is the correct Lidarr instance." : "", artistRecords = lidarrArtists });
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
                        if (verification.Running) throw new InvalidOperationException("Verification is already running.");
                        verificationCancellation?.Dispose();
                        verificationCancellation = new CancellationTokenSource();
                        latestAudit = null; verification = new VerifyState { Started = true, Running = true, Total = source.Length, TotalArtists = source.Select(x => x.Artist.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Phase = "Starting fresh MusicBrainz lookups..." };
                        var ct = verificationCancellation.Token;
                        _ = Task.Run(() => VerifyTracksAsync(source, ct));
                    }
                    await Respond(ctx, 200, new { started = true, total = source.Length }); return;
                case "/api/audit/artists.csv": await SendAuditCsv(ctx, "artists"); return;
                case "/api/audit/songs.csv": await SendAuditCsv(ctx, "songs"); return;
                case "/api/audit/summary":
                    EvidenceAudit? a;
                    lock (verificationGate) a = latestAudit;
                    await Respond(ctx, 200, new { available = a != null,
                        artists = a?.Artists.Count ?? 0, artistHighConfidence = a?.Artists.Count(x => x.Status == "high") ?? 0,
                        artistsNeedingReview = a?.Artists.Count(x => x.Status != "high") ?? 0,
                        songs = a?.Songs.Count ?? 0, songHighConfidence = a?.Songs.Count(x => x.Status == "high") ?? 0,
                        songsNeedingReview = a?.Songs.Count(x => x.Status != "high") ?? 0,
                        catalogCoverage = "Partial. MusicBrainz-wide artist catalog is not enumerated; artist-wide extra-song totals cannot be inferred from recording search results." }); return;
                case "/api/verify/status":
                    VerifyState snapshot;
                    lock (verificationGate) snapshot = verification.Copy();
                    await Respond(ctx, 200, new { started = snapshot.Started, running = snapshot.Running, completed = snapshot.Completed, failed = snapshot.Failed, cancelled = snapshot.Cancelled, checkedCount = snapshot.Checked, total = snapshot.Total, totalArtists = snapshot.TotalArtists, resolvedArtists = snapshot.ResolvedArtists, verifiedRecordings = snapshot.VerifiedRecordings, ambiguous = snapshot.Ambiguous, unmatched = snapshot.Unmatched, lookupFailures = snapshot.LookupFailures, requests = snapshot.Requests, secondPassDone = snapshot.ProcessedSecondPass, secondPassTotal = snapshot.SecondPassTotal, batchesDone = snapshot.BatchesCompleted, batchesTotal = snapshot.TotalBatches, retries = snapshot.Retries, httpErrors = snapshot.HttpErrors, artistHighConfidence = snapshot.ArtistHighConfidence, artistNeedsReview = snapshot.ArtistNeedsReview, songHighConfidence = snapshot.SongHighConfidence, songNeedsReview = snapshot.SongNeedsReview, phase = snapshot.Phase, errorDetail = snapshot.Error }); return;
                default: await Respond(ctx, 404, new { error = "Unknown API action." }); return;
            }
        }
        catch (Exception ex) { Log("API error " + path + ": " + ex.Message); await Respond(ctx, 400, new { error = ex.Message }); }
    }

    static string Normalize(string name) => Regex.Replace(name.ToLowerInvariant(), "[^\\p{L}\\p{N}]", "");
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
            result.Add(new Track(Get(artist), song, url, Get(album)));
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
    record Track(string Artist, string Song, string YouTubeUrl, string Album);
    record LidarrArtist(string artistName, string foreignArtistId);
}
