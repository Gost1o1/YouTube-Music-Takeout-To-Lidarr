using System.Net;
using System.Text;
using System.Text.Json;

sealed partial class DesktopServer
{
    // Preserves user-supplied missing metadata for a future import workflow.
    // This is NOT a Lidarr or MusicBrainz write and does not download media.
    sealed class ManualFallbackEntry
    {
        public string ReviewKey { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Song { get; set; } = "";
        public string Album { get; set; } = "";
        public string YouTubeUrl { get; set; } = "";
        public string Kind { get; set; } = "";
        public string SaveMode { get; set; } = "manual";
        public string Reason { get; set; } = "";
        public DateTimeOffset UpdatedUtc { get; set; }
    }
    readonly object manualFallbackLock = new();
    string ManualFallbackFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "MusicTakeoutToolkit", "manual-metadata-v1.json");
    static bool UsefulManualField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        return trimmed.Length > 0 && !new[] { "-", "unknown", "[unknown]", "[no artist]", "no artist",
            "name unavailable", "artist unknown", "song title unavailable", "artist identity" }
            .Contains(trimmed, StringComparer.OrdinalIgnoreCase);
    }
    static bool CompleteManualMetadata(string artist, string song) =>
        UsefulManualField(artist) && UsefulManualField(song);

    // Automatic filing requires all four source fields, unlike the editable
    // manual form where Album and YouTube URL remain optional.
    static bool CompleteAutomaticMetadata(ReviewItem item) =>
        CompleteManualMetadata(item.Artist, item.Song) &&
        UsefulManualField(item.Album) && SafeYouTubeVideo(item.YouTubeUrl);

    List<ManualFallbackEntry> ReadManualFallbacks()
    {
        // Caller holds manualFallbackLock. Old v1 files without SaveMode remain manual.
        if (!File.Exists(ManualFallbackFile)) return new();
        var data = File.ReadAllText(ManualFallbackFile, Encoding.UTF8);
        if (data.Length > 3_000_000) throw new InvalidOperationException("Saved manual metadata is unexpectedly large.");
        return JsonSerializer.Deserialize<List<ManualFallbackEntry>>(data) ?? new();
    }
    void WriteManualFallbacks(List<ManualFallbackEntry> entries)
    {
        // Caller holds manualFallbackLock. No partially written JSON on interruption.
        if (entries.Count > 3000) throw new InvalidOperationException("Manual metadata storage is full.");
        var file = ManualFallbackFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, file, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    // Uses the current Takeout's audit fields only. An automatic entry is NOT a
    // verified MusicBrainz identity, a Lidarr album, or a downloaded music file.
    void AutoSaveNoCandidateEntries(IEnumerable<ReviewItem> items)
    {
        lock (manualFallbackLock)
        {
            var saved = ReadManualFallbacks();
            var existing = saved.Select(x => x.ReviewKey).ToHashSet(StringComparer.Ordinal);
            bool changed = false;
            foreach (var item in items)
            {
                if (item.Candidates.Count != 0 || !CompleteAutomaticMetadata(item) ||
                    !existing.Add(item.Key)) continue;
                saved.Add(new ManualFallbackEntry { ReviewKey = item.Key, Kind = item.Kind, SaveMode = "auto", Reason = "no-usable-candidate",
                    Artist = item.Artist.Trim(), Song = item.Song.Trim(), Album = item.Album.Trim(),
                    YouTubeUrl = SafeYouTubeVideo(item.YouTubeUrl) ? item.YouTubeUrl : "",
                    UpdatedUtc = DateTimeOffset.UtcNow });
                changed = true;
            }
            if (changed) WriteManualFallbacks(saved);
        }
    }
    bool AutoSaveManualFallback(string key, string reason = "no-usable-candidate")
    {
        // A single identical display name is not artist verification. It merely
        // allows source metadata to be set aside rather than requiring a click.
        if (reason is not ("no-usable-candidate" or "sole-exact-artist-name-unverified" or "identical-artist-names-unverified"
            or "sole-exact-song-title-unverified" or "sole-parenthetical-song-title-unverified"))
            throw new InvalidOperationException("Unknown automatic metadata classification.");
        lock (verificationGate)
        {
            var item = BuildReviewQueue().FirstOrDefault(x => x.Key == key)
                ?? throw new InvalidOperationException("Review item no longer belongs to this music library.");
            if ((reason is "sole-exact-song-title-unverified" or "sole-parenthetical-song-title-unverified")
                && item.Kind != "song")
                throw new InvalidOperationException("Song title metadata filing cannot be applied to an artist.");
            if (!CompleteAutomaticMetadata(item))
                throw new InvalidOperationException("Automatic filing requires source artist, song, album and a valid YouTube link.");
            lock (manualFallbackLock)
            {
                var saved = ReadManualFallbacks();
                if (saved.Any(x => x.ReviewKey == key)) return false; // idempotent; never overwrite a correction
                saved.Add(new ManualFallbackEntry { ReviewKey = key, Kind = item.Kind, SaveMode = "auto", Reason = reason,
                    Artist = item.Artist.Trim(), Song = item.Song.Trim(), Album = item.Album.Trim(),
                    YouTubeUrl = SafeYouTubeVideo(item.YouTubeUrl) ? item.YouTubeUrl : "",
                    UpdatedUtc = DateTimeOffset.UtcNow });
                WriteManualFallbacks(saved);
                return true;
            }
        }
    }
    void SaveManualFallback(string key, string artist, string song, string album, string url)
    {
        static string Clean(string s, int max) => (s ?? "").Trim().Length <= max
            ? (s ?? "").Trim() : throw new InvalidOperationException("Manual metadata exceeds the allowed length.");
        artist = Clean(artist, 220); song = Clean(song, 260); album = Clean(album, 260);
        url = Clean(url, 240);
        if (!CompleteManualMetadata(artist, song))
            throw new InvalidOperationException("Enter an artist name and song title before saving.");
        if (url.Length != 0 && !SafeYouTubeVideo(url))
            throw new InvalidOperationException("Enter a valid YouTube watch URL or leave it empty.");
        lock (verificationGate)
        {
            var record = BuildReviewQueue().FirstOrDefault(x => x.Key == key)
                ?? throw new InvalidOperationException("Review entry is no longer part of the current audit.");
            lock (manualFallbackLock)
            {
                var entries = ReadManualFallbacks();
                entries.RemoveAll(x => x.ReviewKey == key);
                entries.Add(new ManualFallbackEntry { ReviewKey = key, Kind = record.Kind, SaveMode = "manual", Reason = "user-correction",
                    Artist = artist, Song = song, Album = album, YouTubeUrl = url, UpdatedUtc = DateTimeOffset.UtcNow });
                WriteManualFallbacks(entries);
            }
        }
    }
    // Filter against the active audit so metadata from a prior Takeout is not
    // presented as though it had been collected during the current scan.
    List<ManualFallbackEntry> CurrentManualFallbacks(IReadOnlyCollection<ReviewItem> items)
    {
        var keys = items.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        lock (manualFallbackLock)
            return ReadManualFallbacks().Where(x => x.SaveMode != "review" && keys.Contains(x.ReviewKey))
                .OrderBy(x => x.Artist, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Song, StringComparer.OrdinalIgnoreCase).ToList();
    }
    void RestoreManualFallback(string key)
    {
        lock (verificationGate)
        {
            if (!BuildReviewQueue().Any(x => x.Key == key))
                throw new InvalidOperationException("This saved entry is no longer in the current audit.");
            lock (manualFallbackLock)
            {
                var saved = ReadManualFallbacks();
                var entry = saved.FirstOrDefault(x => x.ReviewKey == key && x.SaveMode != "review")
                    ?? throw new InvalidOperationException("This entry is not currently saved for manual import.");
                // Keep the information and a durable opt-out; otherwise the next
                // queue refresh would immediately auto-file the entry again.
                entry.SaveMode = "review";
                entry.Reason = "user-restored-for-editing";
                entry.UpdatedUtc = DateTimeOffset.UtcNow;
                WriteManualFallbacks(saved);
            }
        }
    }
    HashSet<string> RestoredManualFallbackKeys()
    {
        lock (manualFallbackLock) return ReadManualFallbacks()
            .Where(x => x.SaveMode == "review").Select(x => x.ReviewKey)
            .ToHashSet(StringComparer.Ordinal);
    }
    ManualFallbackEntry? GetManualFallback(string key)
    {
        lock (manualFallbackLock)
            return ReadManualFallbacks().FirstOrDefault(x => x.ReviewKey == key);
    }
    static void ManualFallbackSelfTests()
    {
        if (SafeYouTubeVideo("https://malicious.invalid/watch?v=abcdefghijk") ||
            !SafeYouTubeVideo("https://music.youtube.com/watch?v=abcdefghijk"))
            throw new Exception("Manual metadata URL safety invalid.");
        if (Normalize("Dick Dale & His Del-Tones") != Normalize("Dick Dale and His Del-Tones") ||
            Normalize("Sebastian Böhm") != Normalize("Sebastian Bohm"))
            throw new Exception("Review artist-name normalization regression.");
        if (!CompleteManualMetadata("Axwell /\\ Ingrosso", "More Than You Know") ||
            CompleteManualMetadata("-", "More Than You Know") || CompleteManualMetadata("Axwell /\\ Ingrosso", "") ||
            CompleteManualMetadata("[unknown]", "More Than You Know"))
            throw new Exception("Automatic metadata must require an artist and song title.");
        var entry = JsonSerializer.Deserialize<ManualFallbackEntry>(JsonSerializer.Serialize(new ManualFallbackEntry
            { SaveMode = "auto", Kind = "artist", Artist = "Axwell /\\ Ingrosso", Song = "More Than You Know" }));
        if (entry == null || entry.SaveMode != "auto" || entry.Kind != "artist")
            throw new Exception("Automatic-manual metadata provenance must survive persistence.");
        if (new ManualFallbackEntry { SaveMode = "auto", Reason = "sole-exact-artist-name-unverified" }.Reason
            != "sole-exact-artist-name-unverified")
            throw new Exception("Automatic metadata provenance was lost.");
        Console.WriteLine("PASS: v0.9.1.14 automatic metadata eligibility, provenance, normalizations, and URL safety");
    }
}
