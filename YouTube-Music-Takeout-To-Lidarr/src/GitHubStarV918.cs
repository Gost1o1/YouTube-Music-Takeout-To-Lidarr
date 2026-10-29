using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

sealed partial class DesktopServer
{
    // A GitHub App is the recommended deployment: grant Starring (user/write)
    // and Metadata (repository/read); enable Device Flow. No app secret is used.
    // Set GOST1O1_GITHUB_CLIENT_ID before launching the process.
    const string StarOwner = "Gost1o1";
    const string StarRepository = "YouTube-Music-Takeout-To-Lidarr";
    static readonly Uri DeviceCodeEndpoint = new("https://github.com/login/device/code");
    static readonly Uri DeviceTokenEndpoint = new("https://github.com/login/oauth/access_token");
    static readonly Uri StarEndpoint = new($"https://api.github.com/user/starred/{StarOwner}/{StarRepository}");

    sealed record DeviceAuthorization(string DeviceCode, string UserCode,
        string VerificationUri, DateTimeOffset Expires, int IntervalSeconds,
        DateTimeOffset NextPoll);

    readonly SemaphoreSlim githubGate = new(1, 1);
    DeviceAuthorization? githubDevice;
    string? githubAccessToken; // Kept only in process memory, NEVER sent to the browser.
    bool githubStarConfirmed;
    readonly HttpClient githubHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(18) };

    string GitHubClientId => Environment.GetEnvironmentVariable("GOST1O1_GITHUB_CLIENT_ID")?.Trim() ?? "";
    bool GitHubConfigured => Regex.IsMatch(GitHubClientId, "^(?:Iv1\\.|Ov23li\\.)[A-Za-z0-9]{8,100}$");
    static string GitHubJsonString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() ?? "" : "";
    static int GitHubJsonInt(JsonElement data, string name, int fallback) =>
        data.TryGetProperty(name, out var field) && field.TryGetInt32(out var result)
            ? result : fallback;

    HttpRequestMessage GitHubRequest(HttpMethod method, Uri url, string? bearer = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.ParseAdd("YouTubeMusicToLidarr/0.9.1.8");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (bearer != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return req;
    }
    async Task<JsonDocument> GitHubDevicePost(Uri uri, Dictionary<string, string> form)
    {
        using var req = GitHubRequest(HttpMethod.Post, uri);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new FormUrlEncodedContent(form);
        using var response = await githubHttp.SendAsync(req);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("GitHub authorization service returned HTTP " + (int)response.StatusCode + ". Try again later.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    async Task<object> BeginGitHubDeviceAuthorization()
    {
        if (!GitHubConfigured)
            throw new InvalidOperationException("In-app stars need a registered GitHub App client ID. See GITHUB-DEVICE-SETUP-v0.9.1.8.md.");
        await githubGate.WaitAsync();
        try
        {
            githubDevice = null;
            githubAccessToken = null;
            githubStarConfirmed = false;
            using var doc = await GitHubDevicePost(DeviceCodeEndpoint,
                new() { ["client_id"] = GitHubClientId });
            var data = doc.RootElement;
            var deviceCode = GitHubJsonString(data, "device_code");
            var userCode = GitHubJsonString(data, "user_code");
            var uri = GitHubJsonString(data, "verification_uri");
            if (deviceCode.Length < 10 || userCode.Length < 4 || uri != "https://github.com/login/device")
                throw new InvalidOperationException("GitHub returned incomplete device authorization information.");
            int interval = Math.Clamp(GitHubJsonInt(data, "interval", 5), 5, 120);
            int lifetime = Math.Clamp(GitHubJsonInt(data, "expires_in", 900), 60, 900);
            githubDevice = new(deviceCode, userCode, uri, DateTimeOffset.UtcNow.AddSeconds(lifetime),
                interval, DateTimeOffset.UtcNow.AddSeconds(interval));
            return new { status = "authorization_required", userCode, verificationUri = uri,
                expiresInSeconds = lifetime, pollAfterSeconds = interval };
        }
        finally { githubGate.Release(); }
    }

    async Task<object> PollGitHubAuthorization()
    {
        await githubGate.WaitAsync();
        try
        {
            if (githubAccessToken != null) return new { status = "authorized" };
            if (githubDevice == null) return new { status = "not_started" };
            var flow = githubDevice;
            var now = DateTimeOffset.UtcNow;
            if (now >= flow.Expires)
            { githubDevice = null; return new { status = "expired" }; }
            if (now < flow.NextPoll)
                return new { status = "pending", pollAfterSeconds = Math.Max(1, (int)Math.Ceiling((flow.NextPoll - now).TotalSeconds)) };
            // Update guard before the outgoing HTTP call so browser spam can't speed up polling.
            githubDevice = flow with { NextPoll = now.AddSeconds(flow.IntervalSeconds) };
            using var doc = await GitHubDevicePost(DeviceTokenEndpoint, new() {
                ["client_id"] = GitHubClientId,
                ["device_code"] = flow.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            var result = doc.RootElement;
            var token = GitHubJsonString(result, "access_token");
            if (!string.IsNullOrEmpty(token))
            {
                githubAccessToken = token;
                githubDevice = null;
                return new { status = "authorized" };
            }
            var error = GitHubJsonString(result, "error");
            if (error == "authorization_pending")
                return new { status = "pending", pollAfterSeconds = flow.IntervalSeconds };
            if (error == "slow_down")
            {
                int increased = Math.Min(120, flow.IntervalSeconds + 5);
                githubDevice = flow with { IntervalSeconds = increased, NextPoll = DateTimeOffset.UtcNow.AddSeconds(increased) };
                return new { status = "pending", pollAfterSeconds = increased };
            }
            githubDevice = null;
            return new { status = "failed", error = error is "access_denied" or "expired_token" ? error : "authorization_failed" };
        }
        finally { githubGate.Release(); }
    }

    async Task<object> StarGitHubRepository()
    {
        await githubGate.WaitAsync();
        try
        {
            if (githubAccessToken == null)
                throw new InvalidOperationException("Please authorize GitHub before starring the project.");
            // Deliberate single explicit user action. NEVER called from the OAuth poll.
            using var req = GitHubRequest(HttpMethod.Put, StarEndpoint, githubAccessToken);
            req.Content = new ByteArrayContent(Array.Empty<byte>()); // GitHub requires Content-Length: 0.
            using var resp = await githubHttp.SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.NoContent)
            {
                githubStarConfirmed = true;
                return new { starred = true, repository = StarOwner + "/" + StarRepository };
            }
            if (resp.StatusCode == HttpStatusCode.Unauthorized) githubAccessToken = null;
            throw new InvalidOperationException(resp.StatusCode == HttpStatusCode.Forbidden
                ? "GitHub denied permission. Configure the GitHub App with Starring (write) user permission and authorize it again."
                : "GitHub did not confirm the star (HTTP " + (int)resp.StatusCode + ").");
        }
        finally { githubGate.Release(); }
    }
    object GitHubStatus() => new { configured = GitHubConfigured,
        authorized = githubAccessToken != null, starred = githubStarConfirmed,
        repository = StarOwner + "/" + StarRepository };

    static void GitHubStarSelfTests()
    {
        using var device = JsonDocument.Parse("{\"device_code\":\"long-device-code-abc\",\"user_code\":\"ABCD-EFGH\",\"interval\":5,\"expires_in\":900}");
        if (GitHubJsonString(device.RootElement, "user_code") != "ABCD-EFGH" ||
            GitHubJsonInt(device.RootElement, "interval", 10) != 5)
            throw new Exception("GitHub device-flow JSON parsing regression.");
        using var pending = JsonDocument.Parse("{\"error\":\"authorization_pending\"}");
        if (GitHubJsonString(pending.RootElement, "error") != "authorization_pending")
            throw new Exception("GitHub device-flow pending response regression.");
        if (StarEndpoint.ToString() != "https://api.github.com/user/starred/Gost1o1/YouTube-Music-Takeout-To-Lidarr")
            throw new Exception("GitHub repository owner/name regression.");
        Console.WriteLine("PASS: v0.9.1.8 GitHub device-flow parsing and exact star endpoint");
    }
}
