using System.Net;
using System.Text.Json;

sealed partial class DesktopServer
{
    // This is a review-queue preflight, not a MusicBrainz verification pass.
    // Artist cases use conservative identity-name checks. Song cases with no
    // raw recording candidates preserve complete Takeout metadata immediately.
    readonly object autoClassificationGate = new();
    bool autoClassificationRunning;
    bool autoClassificationFinished;
    int autoClassificationDone;
    int autoClassificationTotal;
    string autoClassificationError = "";
    string autoClassificationSignature = "";

    object AutomaticReviewStatus()
    {
        lock (autoClassificationGate)
            return new { running = autoClassificationRunning, finished = autoClassificationFinished,
                completed = autoClassificationDone, total = autoClassificationTotal,
                error = autoClassificationError, sourceOnly = true };
    }

    object StartAutomaticReviewClassification()
    {
        ReviewItem[] snapshot;
        lock (verificationGate)
        {
            snapshot = BuildReviewQueue().Where(x =>
                x.Disposition == "unreviewed" && x.EvidenceStatus == "current" &&
                CompleteAutomaticMetadata(x) &&
                ((x.Kind == "artist" && x.Candidates.Count <= 12) ||
                 (x.Kind == "song" && x.Candidates.Count == 0))).ToArray();
        }
        // A different audit must never inherit the old preflight completion.
        var signature = string.Join("|", snapshot.Select(x => x.Key + ":" + x.Song + ":" + x.Album + ":" + x.YouTubeUrl +
            ":" + string.Join(",", x.Candidates.Select(c => c.Id))));
        lock (autoClassificationGate)
        {
            if (autoClassificationRunning) return AutomaticReviewStatus();
            if (autoClassificationFinished && autoClassificationSignature == signature)
                return AutomaticReviewStatus();
            autoClassificationSignature = signature;
            autoClassificationRunning = true;
            autoClassificationFinished = false;
            autoClassificationDone = 0;
            autoClassificationTotal = snapshot.Length;
            autoClassificationError = "";
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var mb = CreateMbClient();
                foreach (var item in snapshot)
                {
                    try
                    {
                        // Restore-to-review prevents reclassification, and neither a
                        // stored manual correction nor a prior automatic save is overwritten.
                        bool exists;
                        lock (manualFallbackLock) exists = ReadManualFallbacks().Any(x => x.ReviewKey == item.Key);
                        if (!exists)
                        {
                            if (item.Candidates.Count == 0)
                                AutoSaveManualFallback(item.Key, "no-usable-candidate");
                            else
                            {
                                var names = new List<string>();
                                foreach (var candidate in item.Candidates)
                                {
                                    string candidateName;
                                    // Scored candidates retain real catalog names. Reuse
                                    // those before asking MusicBrainz again.
                                    lock (verificationGate)
                                    {
                                        candidateName = corroboration.FirstOrDefault(c =>
                                            c.Stage == "artist-id" && c.CandidateId.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase) &&
                                            c.SourceArtist.Equals(item.Artist, StringComparison.OrdinalIgnoreCase))?.CandidateName ?? "";
                                    }
                                    if (!UsefulManualField(candidateName))
                                    {
                                        try
                                        {
                                            var endpoint = "https://musicbrainz.org/ws/2/artist/" + candidate.Id + "?fmt=json";
                                            var response = await UnifiedMusicBrainzGetAsync(mb, endpoint, sharedMbPacer, CancellationToken.None);
                                            if (response.Body != null)
                                            {
                                                using var doc = JsonDocument.Parse(response.Body);
                                                candidateName = SafeProperty(doc.RootElement, "name");
                                            }
                                        }
                                        catch (HttpRequestException) { }
                                        catch (TaskCanceledException) { }
                                        catch (JsonException) { }
                                    }
                                    // An unknown candidate identity cannot be considered
                                    // equivalent to any other, so leave it for review.
                                    if (!UsefulManualField(candidateName)) { names.Clear(); break; }
                                    names.Add(candidateName);
                                }
                                if (AllArtistCandidatesSameName(item, names))
                                    AutoSaveManualFallback(item.Key, item.Candidates.Count == 1
                                        ? "sole-exact-artist-name-unverified" : "identical-artist-names-unverified");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Leave the entry in review; a failed lookup cannot prove identity.
                        lock (autoClassificationGate) autoClassificationError =
                            "Some candidate checks could not finish: " + ex.GetType().Name;
                    }
                    finally
                    {
                        lock (autoClassificationGate) autoClassificationDone++;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (autoClassificationGate)
                    autoClassificationError = "Review preflight failed: " + ex.GetType().Name;
            }
            finally
            {
                lock (autoClassificationGate)
                {
                    autoClassificationRunning = false;
                    autoClassificationFinished = true;
                }
            }
        });
        return AutomaticReviewStatus();
    }

    static bool AllArtistCandidatesSameName(ReviewItem item, IReadOnlyList<string> names) =>
        item.Kind == "artist" && item.Candidates.Count is >= 1 and <= 12 &&
        names.Count == item.Candidates.Count &&
        names.All(n => UsefulManualField(n) && Normalize(item.Artist) == Normalize(n));
    static bool SingleExactArtistCandidate(ReviewItem item, string candidateName) =>
        AllArtistCandidatesSameName(item, new[] { candidateName });

    static void AutomaticReviewClassificationSelfTests()
    {
        var candidate = new ReviewChoice("0068ae6c-7150-42fd-b0ec-111111111111", "MusicBrainz artist candidate", "");
        var single = new ReviewItem { Kind = "artist", Artist = "HOT SPOT", Song = "MDMA", Album = "Hardstyle Remixes",
            YouTubeUrl = "https://www.youtube.com/watch?v=abcdefghijk" };
        single.Candidates.Add(candidate);
        if (!CompleteAutomaticMetadata(single) || !SingleExactArtistCandidate(single, "Hot Spot") ||
            SingleExactArtistCandidate(single, "Hot Spot Tribute"))
            throw new Exception("Single-name local metadata classification failed.");
        single.Candidates.Add(candidate with { Id = "0068ae6c-7150-42fd-b0ec-222222222222" });
        if (!AllArtistCandidatesSameName(single, new[] { "Hot Spot", "HOT SPOT" }) ||
            AllArtistCandidatesSameName(single, new[] { "Hot Spot", "Different Artist" }))
            throw new Exception("Repeated catalog artist names must be filed as unverified, but different artists must remain for review.");
        single.Candidates.RemoveAt(1);
        single.Album = "";
        if (CompleteAutomaticMetadata(single))
            throw new Exception("Incomplete source metadata was automatically filed.");
        var song = new ReviewItem { Kind = "song", Artist = "MAX", Song = "The Other Side",
            Album = "The Greatest Showman: Reimagined", YouTubeUrl = "https://www.youtube.com/watch?v=abcdefghijk" };
        if (!CompleteAutomaticMetadata(song) || song.Candidates.Count != 0 ||
            AllArtistCandidatesSameName(song, Array.Empty<string>()))
            throw new Exception("No-candidate songs with complete source metadata must be eligible for source-only filing.");
        song.Album = "";
        if (CompleteAutomaticMetadata(song))
            throw new Exception("Missing song source fields must require manual completion.");
        Console.WriteLine("PASS: pre-review artist-name checks, no-candidate song metadata, incomplete-field protection");
    }
}
