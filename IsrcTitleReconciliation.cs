using System.Text.RegularExpressions;

sealed partial class DesktopServer
{
    // Compares titles for ID-recovery evidence. This NEVER establishes exact-master confidence.
    // Only trailing artist-credit formatting and clearly equivalent version descriptions can be reconciled.
    // Any studio/live, acoustic, remix, edit, demo, instrumental etc. difference is a hard hold.
    sealed record RecoveryTitleEvidence(string Decision, string SourceBase, string CandidateBase,
        string SourceVersion, string CandidateVersion, bool Eligible)
    {
        public string Explanation => $"title={Decision};source-base={SourceBase};mb-base={CandidateBase};" +
            $"source-version={SourceVersion};mb-version={CandidateVersion}";
    }

    static readonly Regex VersionBracket = new(@"[\(\[]([^\)\]]+)[\)\]]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex FeatureEnding = new(@"(?i)(?:\s*(?:\(|\[)\s*(?:feat\.?|ft\.?|featuring)\s+[^\)\]]+[\)\]]|\s+[-–—]\s*(?:feat\.?|ft\.?|featuring)\s+.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly Regex FeatureName = new(@"(?i)(?:feat\.?|ft\.?|featuring)\s+([^\)\]]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static string ExtractTrailingFeaturedArtist(string input)
    {
        var ending = FeatureEnding.Match(input ?? "");
        if (!ending.Success) return "";
        var name = FeatureName.Match(ending.Value);
        if (!name.Success) return "";
        return name.Groups[1].Value.Trim().TrimEnd(')', ']', ' ');
    }
    static readonly Regex VersionEnding = new(@"\s+[-–—]\s*(.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    static readonly (string Key, string Expression)[] VersionIndicators =
    {
        ("live", @"\b(live|unplugged|concert)\b"),
        ("remix", @"\b(remix|mix by|rework|bootleg)\b"),
        ("remaster", @"\b(remaster(?:ed)?)\b"),
        ("acoustic", @"\bacoustic\b"),
        ("instrumental", @"\binstrumental\b"),
        ("demo", @"\bdemo\b"),
        ("radio-edit", @"\b(radio edit|radio version|radio mix)\b"),
        ("extended", @"\b(extended|long version)\b"),
        ("sped-up", @"\b(sped up|speed up|nightcore)\b"),
        ("slowed", @"\b(slowed|slowed down)\b"),
        ("re-recorded", @"\b(re.recorded|taylor.s version)\b"),
        ("cover", @"\bcover\b"),
    };
    static string RecoveryNormalize(string value) => Normalize(value ?? "");
    static bool IsVersionTerm(string text) => VersionIndicators.Any(x => Regex.IsMatch(text, x.Expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    static string[] VersionTags(string text) => VersionIndicators.Where(x => Regex.IsMatch(text, x.Expression,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Select(x => x.Key).ToArray();

    static (string Base, string Version, string[] Tags) RecoveryTitleParts(string input)
    {
        var work = FeatureEnding.Replace(input ?? "", "").Trim();
        var versions = new List<string>();
        // Only remove bracketed annotations that *actually* describe an audio version.
        // "Stronger (What Doesn't Kill You)" and "Last Friday Night (T.G.I.F.)" remain intact.
        work = VersionBracket.Replace(work, match =>
        {
            if (!IsVersionTerm(match.Groups[1].Value)) return match.Value;
            versions.Add(match.Groups[1].Value);
            return "";
        });
        var dash = VersionEnding.Match(work);
        if (dash.Success && IsVersionTerm(dash.Groups[1].Value))
        {
            versions.Add(dash.Groups[1].Value);
            work = work[..dash.Index];
        }
        var tags = versions.SelectMany(VersionTags).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray();
        return (RecoveryNormalize(work), RecoveryNormalize(string.Join(" ", versions)), tags);
    }

    static RecoveryTitleEvidence EvaluateRecoveryTitle(string sourceTitle, string candidateTitle)
    {
        var a = RecoveryTitleParts(sourceTitle);
        var b = RecoveryTitleParts(candidateTitle);
        var featA = RecoveryNormalize(ExtractTrailingFeaturedArtist(sourceTitle));
        var featB = RecoveryNormalize(ExtractTrailingFeaturedArtist(candidateTitle));
        var tagA = string.Join("+", a.Tags);
        var tagB = string.Join("+", b.Tags);
        if (a.Base.Length == 0 || b.Base.Length == 0 || a.Base != b.Base)
            return new("base-title-mismatch", a.Base, b.Base, tagA, tagB, false);
        if (tagA != tagB)
            return new("version-type-conflict", a.Base, b.Base, tagA, tagB, false);
        if (a.Version != b.Version)
            return new("version-details-uncertain", a.Base, b.Base, tagA, tagB, false);
        if (featA.Length > 0 && featB.Length > 0 && featA != featB)
            return new("featured-artist-conflict", a.Base, b.Base, tagA, tagB, false);
        if ((featA.Length > 0) != (featB.Length > 0))
            return new("featured-artist-credit-not-checked", a.Base, b.Base, tagA, tagB, false);
        if (RecoveryNormalize(sourceTitle) == RecoveryNormalize(candidateTitle))
            return new("exact-normalized-title", a.Base, b.Base, tagA, tagB, true);
        if (a.Tags.Length > 0)
            return new("equivalent-qualified-version", a.Base, b.Base, tagA, tagB, true);
        return new("equivalent-featuring-annotation", a.Base, b.Base, tagA, tagB, true);
    }

    static void RecoveryTitleSelfTests()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception("Title recovery self-test failed: " + message); }
        Check(!EvaluateRecoveryTitle("Song (feat. Someone)", "Song").Eligible, "feature credit must be corroborated");
        Check(EvaluateRecoveryTitle("Song (Live)", "Song - Live").Eligible, "equivalent live descriptors");
        Check(!EvaluateRecoveryTitle("Song (Live)", "Song").Eligible, "live/studio conflict blocked");
        Check(!EvaluateRecoveryTitle("Song (Acoustic)", "Song (Live)").Eligible, "acoustic/live conflict blocked");
        Check(!EvaluateRecoveryTitle("Song (Live at Berlin)", "Song (Live at Paris)").Eligible, "different live venues blocked");
        Check(!EvaluateRecoveryTitle("Song (Remix)", "Song (Radio Edit)").Eligible, "different versions blocked");
        Check(EvaluateRecoveryTitle("Stronger (What Doesn't Kill You)", "Stronger (What Doesn't Kill You)").Eligible, "non-version parentheses preserved");
        Check(!EvaluateRecoveryTitle("Stronger (What Doesn't Kill You)", "Stronger").Eligible, "substantive subtitle not removed");
        Check(!EvaluateRecoveryTitle("Song (Unplugged)", "Song (Live)").Eligible, "version detail uncertain even within live class");
        Check(EvaluateRecoveryTitle("Song (feat. Person)", "Song [ft. Person]").Eligible, "featuring punctuation normalized");
        Check(!EvaluateRecoveryTitle("Song (feat. Person)", "Song (feat. Other)").Eligible, "different featured artists blocked");
    }
}
