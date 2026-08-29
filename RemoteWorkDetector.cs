using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Conservatively checks remote-designated postings for current-job obligations
/// that materially conflict with ordinary work-from-home expectations.
/// </summary>
public sealed class RemoteWorkDetector
{
    public const int CurrentAnalysisVersion = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private sealed record Rule(
        string Category,
        string ConcernLevel,
        string Reason,
        Regex Pattern);

    private static readonly Regex RemoteDesignationPattern = CreateRegex(
        @"\b(?:remote|telework(?:er)?|telecommut(?:e|er|ing)|work(?:ing)?\s+(?:from|at)\s+home|" +
        @"home[- ]based|WFH|virtual)\b");
    private static readonly Regex ExplicitRemoteRolePattern = CreateRegex(
        @"\bthis\s+(?:position|role)\s+is\s+(?:a\s+)?(?:U\.?S\.?\s+)?(?:fully\s+)?remote(?:[- ]telework)?\b|" +
        @"\bthis\s+is\s+(?:a\s+)?(?:U\.?S\.?\s+)?remote[- ]telework\s+role\b|" +
        @"\bthis\s+position\b.{0,140}\bfollows\s+a\s+remote\s+work\s+schedule\b");
    private static readonly Regex BlockEndPattern = CreateRegex(
        @"</(?:p|li|div|h[1-6])\s*>|<br\s*/?>");
    private static readonly Regex SentenceSplitPattern = CreateRegex(
        @"(?<=[.!?])\s+(?=[A-Z0-9#*])");
    private static readonly Regex WhitespacePattern = CreateRegex(@"\s+");
    private static readonly Regex HistoricalExperiencePattern = CreateRegex(
        @"\b(?:prior|previous|demonstrated|relevant)?\s*experience\s+(?:working|supporting|performing|conducting|at|in|with)\b|" +
        @"\b(?:familiarity|knowledge|background)\s+(?:with|of|in)\b");
    private static readonly Regex CurrentObligationPattern = CreateRegex(
        @"\b(?:must|required|requires?|will|shall|responsib(?:le|ilities)|you(?:'|\u2019)?ll|this\s+(?:position|role)|" +
        @"provide|perform|conduct|support|lead|assist|serve|deploys?)\b");
    private static readonly Regex TravelPercentagePattern = CreateRegex(
        @"\b(?<minimum>\d{1,3})\s*%\s*(?:(?:-|\u2013|\u2014|to)\s*(?<maximum>\d{1,3})\s*%)?\s+travel\b|" +
        @"\btravel\b[^.!?]{0,30}?\b(?<minimum>\d{1,3})\s*%\s*(?:(?:-|\u2013|\u2014|to)\s*(?<maximum>\d{1,3})\s*%)?");
    private static readonly Regex FrequentTravelPattern = CreateRegex(
        @"\b(?:requires?\s+(?:the\s+ability\s+to\s+)?travel\s+frequently|frequent\s+travel|" +
        @"travel\s+frequently|extensive\s+travel)\b");
    private static readonly Regex TentativePresencePattern = CreateRegex(
        @"\b(?:a\s+possibility|periodic(?:ally)?|may\s+be\s+required|as[- ]needed\s+basis)\b");

    private static readonly Rule[] Rules =
    [
        new("scheduled-onsite", "strong",
            "scheduled onsite attendance",
            CreateRegex(@"\b(?:work|be|spend|report|in[- ]person).{0,70}\b(?:one|two|three|four|five|[1-5])\s*(?:-|\u2013)?\s*days?\s+(?:a|per)\s+week\b.{0,70}\b(?:on[- ]?site|in[- ]person|office|facility)\b|" +
                        @"\b(?:one|two|three|four|five|[1-5])\s*(?:-|\u2013)?\s*days?\s+(?:a|per)\s+week\b.{0,70}\b(?:on[- ]?site|in[- ]person|office|facility)\b")),
        new("onsite-duty", "strong",
            "onsite duties",
            CreateRegex(@"\b(?:provide|perform|serve\s+as|lead|maintain).{0,45}\bon[- ]?site\b|" +
                        @"\b(?:work|be|report|role\s+(?:deploys|requires)|position\s+requires).{0,65}\b(?:on[- ]?site|in[- ]person)\b|" +
                        @"\bon[- ]?site\b.{0,80}\b(?:support|activities|work|monitoring|consultation|deployment|duration)\b")),
        new("field-deployment", "strong",
            "field deployment or operational testing",
            CreateRegex(@"\b(?:conduct|perform|support|assist|lead|manage).{0,55}\b(?:field\s+(?:deployment|testing|operations?|integration)|operational\s+(?:testing|validation))\b|" +
                        @"\bfield\s+deployment\s+support\b")),
        new("physical-installation", "strong",
            "physical installation or activation work",
            CreateRegex(@"\b(?:support|perform|lead|manage|responsible\s+for).{0,65}\b(?:installation\s+and\s+activation|installation\s+of\s+(?:physical\s+)?(?:sensing\s+systems?|equipment)|" +
                        @"delivery\s+and\s+removal\s+of\s+.+?equipment|deploy(?:ment|ing)\s+and\s+activation)\b")),
        new("operational-site", "strong",
            "work at an operational site or facility",
            CreateRegex(@"\b(?:work|serve|deploy|support|assist|conduct|perform).{0,70}\b(?:Army|DoD|military)\s+(?:installations?|depots?|arsenals?)\b|" +
                        @"\b(?:work|serve|deploy|support|assist|conduct|perform).{0,70}\b(?:operational\s+test\s+ranges?|manufacturing\s+facilit(?:y|ies)|airport\s+locations?|laborator(?:y|ies))\b")),
        new("commuting-area", "questionable",
            "a commuting-distance or local-residency requirement",
            CreateRegex(@"\b(?:must|required\s+to|ideal(?:ly)?\s+candidate\s+(?:would|should)\s+be|candidates?\s+must)\b.{0,100}\b(?:within\s+(?:a\s+)?(?:reasonable\s+)?commuting\s+distance|" +
                        @"within\s+\d{1,3}\s+miles?|within\s+driving\s+distance|\d{1,2}\s*hour\s+radius|greater\s+.+?\s+area)\b|" +
                        @"\b(?:candidates?\s+)?(?:will\s+need|need)\s+to\s+be\b.{0,70}\bcommutable\s+distance\b|" +
                        @"\bmust\s+live\s+within\s+driving\s+distance\b|" +
                        @"\bif\s+you\s+live\s+within\s+(?:a\s+)?reasonable\s+commute\b.{0,160}\bonsite\s+presence\b"))
    ];

    public RemoteWorkAnalysis Analyze(
        string title,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations,
        string descriptionHtml)
    {
        var metadata = string.Join(" ", new[] { title, primaryLocation }
            .Concat(additionalLocations ?? []));
        var plainDescription = string.IsNullOrWhiteSpace(descriptionHtml)
            ? ""
            : JobAnalysis.HtmlToPlainText(descriptionHtml);
        var isRemoteDesignated = RemoteDesignationPattern.IsMatch(metadata) ||
            ExplicitRemoteRolePattern.IsMatch(plainDescription);
        if (!isRemoteDesignated)
        {
            return Empty(false, "not-remote-designated");
        }

        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return Empty(true, "description-unavailable");
        }

        var separatedHtml = BlockEndPattern.Replace(descriptionHtml, ". ");
        var text = JobAnalysis.HtmlToPlainText(separatedHtml);
        var sentences = SentenceSplitPattern.Split(text)
            .Select(NormalizeEvidence)
            .Where(sentence => sentence.Length > 0)
            .ToArray();
        var signals = new List<RemoteWorkSignal>();

        foreach (var sentence in sentences)
        {
            var historicalOnly = HistoricalExperiencePattern.IsMatch(sentence) &&
                !CurrentObligationPattern.IsMatch(sentence);
            if (!historicalOnly)
            {
                foreach (var rule in Rules)
                {
                    if (rule.Category == "commuting-area" &&
                        sentence.Contains("airport", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (rule.Pattern.IsMatch(sentence))
                    {
                        if (rule.Category == "onsite-duty" && TentativePresencePattern.IsMatch(sentence))
                        {
                            signals.Add(new RemoteWorkSignal(
                                rule.Category,
                                "questionable",
                                "periodic or as-needed onsite attendance",
                                NormalizeEvidence(sentence)));
                        }
                        else
                        {
                            AddSignal(signals, rule, sentence);
                        }
                    }
                }

                AddTravelSignal(signals, sentence);
            }
        }

        var ordered = signals
            .DistinctBy(signal => new { signal.Category, signal.Evidence })
            .OrderByDescending(signal => signal.ConcernLevel == "strong")
            .ThenBy(signal => signal.Category, StringComparer.Ordinal)
            .Take(4)
            .ToArray();
        var level = ordered.Any(signal => signal.ConcernLevel == "strong")
            ? "strong"
            : ordered.Any(signal => signal.ConcernLevel == "questionable")
                ? "questionable"
                : "none";
        var reasons = ordered.Select(signal => signal.Reason)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var summary = reasons.Length == 0
            ? null
            : string.Join("; ", reasons).TrimEnd('.') + ".";

        return new RemoteWorkAnalysis(
            true,
            level,
            summary,
            ordered,
            ordered.Length > 0 ? "parsed" : "no-concern-detected",
            CurrentAnalysisVersion);
    }

    public JobRecord AnalyzeJob(JobRecord job) => job with
    {
        RemoteWork = Analyze(
            job.Title,
            job.PrimaryLocation,
            job.AdditionalLocations,
            job.DescriptionHtml)
    };

    private static void AddTravelSignal(List<RemoteWorkSignal> signals, string sentence)
    {
        foreach (Match match in TravelPercentagePattern.Matches(sentence))
        {
            if (!int.TryParse(match.Groups["minimum"].Value, out var minimum) || minimum > 100)
            {
                continue;
            }
            var maximum = match.Groups["maximum"].Success &&
                int.TryParse(match.Groups["maximum"].Value, out var parsedMaximum)
                    ? parsedMaximum
                    : minimum;
            if (maximum > 100 || maximum < minimum)
            {
                continue;
            }
            var category = maximum <= 10
                ? "occasional-travel"
                : maximum < 50 ? "moderate-travel" : "substantial-travel";
            var concern = maximum <= 10
                ? "informational"
                : maximum < 50 ? "questionable" : "strong";
            var range = maximum == minimum ? $"{maximum}%" : $"{minimum}-{maximum}%";
            signals.Add(new RemoteWorkSignal(
                category,
                concern,
                $"{range} travel",
                NormalizeEvidence(sentence)));
        }

        if (FrequentTravelPattern.IsMatch(sentence))
        {
            signals.Add(new RemoteWorkSignal(
                "frequent-travel",
                "questionable",
                "frequent travel",
                NormalizeEvidence(sentence)));
        }
    }

    private static void AddSignal(List<RemoteWorkSignal> signals, Rule rule, string sentence) =>
        signals.Add(new RemoteWorkSignal(
            rule.Category,
            rule.ConcernLevel,
            rule.Reason,
            NormalizeEvidence(sentence)));

    private static RemoteWorkAnalysis Empty(bool remote, string status) => new(
        remote, "none", null, [], status, CurrentAnalysisVersion);

    private static string NormalizeEvidence(string value)
    {
        var normalized = WhitespacePattern.Replace(value, " ").Trim(' ', '.', ';', '\u2022');
        return normalized.Length <= 300 ? normalized : normalized[..297] + "...";
    }

    private static Regex CreateRegex(string pattern) =>
        new(pattern, Options, RegexTimeout);
}
