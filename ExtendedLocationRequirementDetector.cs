using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Conservatively identifies current-job deployment, rotation, relocation, or
/// extended-presence obligations that structured location metadata can conceal.
/// Location names alone are never sufficient evidence.
/// </summary>
public sealed class ExtendedLocationRequirementDetector
{
    public const int CurrentAnalysisVersion = 3;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private sealed record Rule(
        string Category,
        string Confidence,
        string Reason,
        Regex Pattern);

    private sealed record DestinationDefinition(string Name, Regex Pattern);

    private const string EvidenceDestination =
        @"(?:Guam|Antarctica|McMurdo(?:\s+Station)?|South\s+Pole(?:\s+Station)?|" +
        @"Kwajalein|Marshall\s+Islands?|Diego\s+Garcia|OCONUS|overseas|" +
        @"Germany|Japan|Iraq|Lebanon|Kazakhstan|Middle\s+East|Pacific\s+Islands?)";

    private static readonly DestinationDefinition[] Destinations =
    [
        new("Guam", CreateRegex(@"\bGuam\b")),
        new("Antarctica", CreateRegex(@"\b(?:Antarctica|Antarctic|McMurdo(?:\s+Station)?|South\s+Pole(?:\s+Station)?)\b")),
        new("Kwajalein", CreateRegex(@"\bKwajalein\b")),
        new("Diego Garcia", CreateRegex(@"\bDiego\s+Garcia\b")),
        new("Germany", CreateRegex(@"\bGermany\b")),
        new("Japan", CreateRegex(@"\bJapan\b")),
        new("Marshall Islands", CreateRegex(@"\bMarshall\s+Islands?\b")),
        new("Iraq", CreateRegex(@"\bIraq\b")),
        new("Lebanon", CreateRegex(@"\bLebanon\b")),
        new("Kazakhstan", CreateRegex(@"\bKazakhstan\b")),
        new("Middle East", CreateRegex(@"\bMiddle\s+East\b")),
        new("Pacific Islands", CreateRegex(@"\bPacific\s+Islands?\b")),
        new("OCONUS", CreateRegex(@"\bOCONUS\b")),
        new("Overseas", CreateRegex(@"\boverseas\b"))
    ];

    private static readonly Regex BlockEndPattern = CreateRegex(
        @"</(?:p|li|div|h[1-6])\s*>|<br\s*/?>");
    private static readonly Regex SentenceSplitPattern = CreateRegex(
        @"(?<=[.!?])\s+(?=[A-Z0-9#*])");
    private static readonly Regex WhitespacePattern = CreateRegex(@"\s+");
    private static readonly Regex HistoricalPattern = CreateRegex(
        @"\b(?:prior|previous|past|demonstrated|relevant)\s+(?:(?:OCONUS|overseas)\s+)?(?:(?:deployment|assignment)\s+)?experience\b|" +
        @"\bexperience\s+(?:supporting|with|in|at)\b");
    private static readonly Regex CurrentObligationPattern = CreateRegex(
        @"\b(?:must|required|requires?|will|shall|expected|position|role|employment|candidate|employee|" +
        @"willingness|ability|reside|relocate|work\s+will)\b");
    private static readonly Regex ConditionalOnlyPattern = CreateRegex(
        @"\b(?:may|might|could)\b|\bat\s+(?:the\s+)?discretion\b");
    private static readonly Regex DefiniteObligationPattern = CreateRegex(
        @"\b(?:must|required|requires?|will|shall|expected|willing|willingness|ability|" +
        @"position|role|employee|candidate|continuous|long[- ]term)\b");
    private static readonly Regex ExplicitObligationPattern = CreateRegex(
        @"\b(?:must|required|requires?|will|shall|expected)\b");
    private static readonly Regex AwayPresencePattern = CreateRegex(
        @"\b(?:deploy(?:ment|ed|ing)?|assignments?|rotations?|on[- ]?site|customer\s+site|" +
        @"field\s+(?:site|camp)|remote\s+site|reside|remain|stay|aboard|shipboard|at\s+sea|" +
        @"in[- ]country|temporary\s+duty|TDY|OCONUS|overseas|Antarct(?:ic|ica))\b");
    private static readonly Regex LongDurationTravelPresencePattern = CreateRegex(
        @"\b(?:willing|required|expected)\s+to\s+travel(?:\s+100\s*%)?\s+to\b|" +
        @"\bcontinuous\s+travel\s+assignments?\b");
    private static readonly Regex DurationPattern = CreateRegex(
        @"\b(?<value>\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)" +
        @"(?:\s*(?:-|–|—|to)\s*(?<upper>\d{1,3}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve))?" +
        @"(?:\s+consecutive)?\s*[- ]?\s*(?<unit>days?|weeks?|months?)\b");

    private static readonly Rule[] StrongRules =
    [
        new("explicit-job-location", "strong", "explicit unusual job location",
            CreateRegex($@"\b(?:job|work)\s+location\s+is\s+(?:the\s+)?[^.!?;]{{0,50}}\b{EvidenceDestination}\b|" +
                        $@"\bposition\s+is\s+located\s+(?:in|at)\s+[^.!?;]{{0,50}}\b{EvidenceDestination}\b|" +
                        $@"\bwork\s+will\s+be\s+performed\s+(?:in|at)\s+[^.!?;]{{0,50}}\b{EvidenceDestination}\b")),
        new("required-deployment", "strong", "required deployment",
            CreateRegex(@"\bdeployment\s+is\s+required(?:\s+in\s+this\s+(?:position|role))?\b|" +
                        @"\bdeployment\s+(?:to|in|at)\s+[^.!?;]{1,100}\s+is\s+required\b|" +
                        @"\b(?:must|required\s+to|will|shall)\s+deploy\b|" +
                        @"\bdeployment[- ]only\s+position\b|" +
                        @"\bemployment\s+is\s+only\s+provided\s+during\s+the\s+deployment\s+period\b|" +
                        @"\bthis\s+position\s+(?:includes|requires)\s+(?:an?\s+)?(?:international|overseas|OCONUS)\s+deployment\b")),
        new("recurring-deployment", "strong", "recurring overseas or OCONUS deployment",
            CreateRegex(@"\b(?:at\s+least\s+)?(?:annual|recurring|regular)\s+deployments?\b[^.!?]{0,90}\b(?:OCONUS|overseas|international|Antarct(?:ic|ica))\b")),
        new("extended-deployment", "strong", "extended deployment or site presence",
            CreateRegex(@"\b(?:ability\s+and\s+willingness|willingness\s+and\s+ability|must\s+be\s+willing\s+and\s+able)\s+to\s+deploy\b[^.!?]{0,100}\b(?:for\s+)?extended\s+periods?\b|" +
                        @"\bdeploy\b[^.!?]{0,100}\bfor\s+extended\s+periods?\b|" +
                        @"\bmust\s+be\s+willing\s+and\s+able\s+to\s+deploy\s+internationally\b|" +
                        @"\bmust\s+be\s+willing\s+and\s+able\s+to\s+deploy\b[^.!?]{0,120}\bup\s+to\s+\d{1,3}\s+consecutive\s+days\b|" +
                        @"\bposition\s+requires\s+extended\s+presence\s+(?:in|at)\b")),
        new("winter-over-assignment", "strong", "winter-over assignment",
            CreateRegex(@"\b(?:winter[- ]?over|winter\s+site\s+manager)\b")),
        new("long-term-away-assignment", "strong", "long-term overseas or OCONUS assignment",
            CreateRegex(@"\b(?:100\s*%\s+)?long[- ]term\s+(?:OCONUS|overseas|international)\s+assignments?\b|" +
                        @"\b(?:OCONUS|overseas|international)\s+assignments?\b[^.!?]{0,60}\blong[- ]term\b")),
        new("forward-deployed", "strong", "forward-deployed assignment",
            CreateRegex(@"\b(?:will\s+be|must\s+be|employee\s+(?:is|will\s+be))?\s*forward[- ]deployed\b")),
        new("oconus-assignment", "strong", "long-term OCONUS or overseas assignment",
            CreateRegex(@"\b(?:100\s*%\s+)?(?:long[- ]term\s+)?OCONUS\s+assignments?\b|" +
                        @"\b(?:100\s*%\s+)?(?:long[- ]term\s+)?overseas\s+assignments?\b|" +
                        @"\bthis\s+is\s+(?:an?\s+)?\(?international\s+assignment\)?\b|" +
                        @"\bposition\s+is\s+(?:an?\s+)?(?:international|overseas|OCONUS)\s+assignment\b|" +
                        @"\b(?:international|overseas|OCONUS)\s+assignment\b[^.!?]{0,80}\bno\s+remote\s+work\b|" +
                        @"\bno\s+remote\s+work\b[^.!?]{0,80}\b(?:international|overseas|OCONUS)\s+assignment\b")),
        new("rotation", "strong", "required rotational assignment",
            CreateRegex(@"\b(?:this\s+is\s+)?(?:an?\s+)?\d{1,3}(?:[- ]day)?\s+rotational\s+assignment\b|" +
                        @"\b(?:required|mandatory)\s+rotational\s+assignment\b|" +
                        @"\b\d{1,2}\s+weeks?\s+on\s*/\s*\d{1,2}\s+weeks?\s+off\b|" +
                        @"\b(?:international|overseas|OCONUS)\s+assignments?\b[^.!?]{0,80}\bup\s+to\s+\d{1,3}[- ]day\s+rotations?\b")),
        new("temporary-duty", "strong", "temporary-duty assignment",
            CreateRegex(@"\b(?:temporary\s+duty|TDY)\s+assignment\s+(?:in|to|at)\b")),
        new("required-unusual-relocation", "strong", "required relocation to an unusual location",
            CreateRegex($@"\b(?:must|required\s+to|will\s+need\s+to)\s+(?:reside\s+(?:in|at)|relocate\s+to)\s+[^.!?;]{{0,70}}\b{EvidenceDestination}\b"))
    ];

    private static readonly Rule[] QuestionableRules =
    [
        new("possible-deployment", "questionable", "possible deployment",
            CreateRegex($@"\bdeployment\s+to\s+[^.!?;]{{0,70}}\b{EvidenceDestination}\b[^.!?]{{0,60}}\b(?:may|might|could)\s+be\s+(?:necessary|required|expected)\b|" +
                        $@"\b(?:may|might|could)\s+(?:be\s+required\s+to\s+)?deploy\s+(?:to|in|at)\s+[^.!?;]{{0,70}}\b{EvidenceDestination}\b")),
        new("possible-extended-assignment", "questionable", "possible extended assignment",
            CreateRegex(@"\bmay\s+require\s+(?:an?\s+)?extended\s+(?:deployment|assignment|presence)\b"))
    ];

    public ExtendedLocationRequirementAnalysis Analyze(
        string title,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations,
        string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return Empty("description-unavailable");
        }

        var separatedHtml = BlockEndPattern.Replace(descriptionHtml, ". ");
        var text = JobAnalysis.HtmlToPlainText(separatedHtml);
        var sentences = new[] { NormalizeSentence(title) }
            .Concat(SentenceSplitPattern.Split(text)
            .Select(NormalizeSentence)
            .Where(sentence => sentence.Length > 0))
            .Where(sentence => sentence.Length > 0)
            .ToArray();
        var signals = new List<ExtendedLocationRequirementSignal>();

        foreach (var sentence in sentences)
        {
            var historicalOnly = HistoricalPattern.IsMatch(sentence) &&
                !CurrentObligationPattern.IsMatch(sentence);
            if (historicalOnly)
            {
                continue;
            }

            AddExtendedAwayDurationSignal(signals, sentence);

            foreach (var rule in StrongRules)
            {
                AddIfMatch(signals, rule, sentence);
            }
            foreach (var rule in QuestionableRules)
            {
                AddIfMatch(signals, rule, sentence);
            }
        }

        var ordered = signals
            .DistinctBy(signal => new { signal.Category, signal.Evidence })
            .OrderByDescending(signal => signal.Confidence == "strong")
            .ThenBy(signal => signal.Category, StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        var confidence = ordered.Any(signal => signal.Confidence == "strong")
            ? "strong"
            : ordered.Length > 0 ? "questionable" : "none";
        var primarySignal = ordered.FirstOrDefault();
        var destination = primarySignal is null
            ? null
            : FindDestination(primarySignal.Evidence) ??
              FindDestination(title) ??
              FindDestination(primaryLocation) ??
              additionalLocations.Select(FindDestination).FirstOrDefault(value => value is not null) ??
              FindDestination(text) ??
              "Destination not specified";
        var summary = primarySignal is null ? null : BuildSummary(primarySignal, destination!);

        return new ExtendedLocationRequirementAnalysis(
            confidence,
            destination,
            summary,
            ordered,
            ordered.Length > 0 ? "parsed" : "no-requirement-detected",
            CurrentAnalysisVersion);
    }

    public JobRecord AnalyzeJob(JobRecord job) => job with
    {
        ExtendedLocationRequirement = Analyze(
            job.Title,
            job.PrimaryLocation,
            job.AdditionalLocations,
            job.DescriptionHtml)
    };

    private static void AddIfMatch(
        List<ExtendedLocationRequirementSignal> signals,
        Rule rule,
        string sentence)
    {
        if (!rule.Pattern.IsMatch(sentence))
        {
            return;
        }
        signals.Add(new ExtendedLocationRequirementSignal(
            rule.Category,
            rule.Confidence,
            rule.Reason,
            NormalizeEvidence(sentence)));
    }

    private static void AddExtendedAwayDurationSignal(
        List<ExtendedLocationRequirementSignal> signals,
        string sentence)
    {
        if (!(AwayPresencePattern.IsMatch(sentence) || LongDurationTravelPresencePattern.IsMatch(sentence)) ||
            !DefiniteObligationPattern.IsMatch(sentence) ||
            (ConditionalOnlyPattern.IsMatch(sentence) &&
             !ExplicitObligationPattern.IsMatch(sentence)))
        {
            return;
        }

        foreach (Match match in DurationPattern.Matches(sentence))
        {
            if (!TryDurationDays(match, out var days) || days < 28)
            {
                continue;
            }

            var contextStart = Math.Max(0, match.Index - 150);
            var contextLength = Math.Min(sentence.Length - contextStart, match.Length + 300);
            var context = sentence.Substring(contextStart, contextLength);
            if (!(AwayPresencePattern.IsMatch(context) || LongDurationTravelPresencePattern.IsMatch(context)))
            {
                continue;
            }

            signals.Add(new ExtendedLocationRequirementSignal(
                "extended-away-duration",
                "strong",
                "mandatory extended away-from-home assignment",
                EvidenceAround(sentence, match.Index, match.Length)));
            return;
        }
    }

    private static bool TryDurationDays(Match match, out int days)
    {
        days = 0;
        if (!TryNumber(match.Groups["value"].Value, out var value))
        {
            return false;
        }

        days = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            var unit when unit.StartsWith("month", StringComparison.Ordinal) => value * 30,
            var unit when unit.StartsWith("week", StringComparison.Ordinal) => value * 7,
            _ => value
        };
        return true;
    }

    private static bool TryNumber(string value, out int number)
    {
        if (int.TryParse(value, out number))
        {
            return true;
        }

        number = value.ToLowerInvariant() switch
        {
            "one" => 1, "two" => 2, "three" => 3, "four" => 4,
            "five" => 5, "six" => 6, "seven" => 7, "eight" => 8,
            "nine" => 9, "ten" => 10, "eleven" => 11, "twelve" => 12,
            _ => 0
        };
        return number > 0;
    }

    private static string EvidenceAround(string sentence, int index, int length)
    {
        const int maximum = 300;
        if (sentence.Length <= maximum)
        {
            return sentence;
        }

        var start = Math.Max(0, index - ((maximum - length) / 2));
        start = Math.Min(start, sentence.Length - maximum);
        var excerpt = sentence.Substring(start, maximum).Trim(' ', '.', ';', '\u2022');
        return (start > 0 ? "…" : "") + excerpt +
            (start + maximum < sentence.Length ? "…" : "");
    }

    private static ExtendedLocationRequirementAnalysis Empty(string status) => new(
        "none", null, null, [], status, CurrentAnalysisVersion);

    private static string? FindDestination(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Destinations.FirstOrDefault(destination => destination.Pattern.IsMatch(value))?.Name;
    }

    private static string BuildSummary(
        ExtendedLocationRequirementSignal signal,
        string destination) => signal.Category switch
        {
            "explicit-job-location" => $"Job location is {destination}",
            "required-deployment" => "Deployment required",
            "recurring-deployment" => "Recurring deployment required",
            "extended-deployment" => "Extended site deployment required",
            "extended-away-duration" => "Extended away-from-home assignment required",
            "winter-over-assignment" => "Winter-over assignment required",
            "long-term-away-assignment" => "Long-term overseas assignment required",
            "forward-deployed" => "Forward-deployed assignment required",
            "oconus-assignment" => "Long-term overseas assignment required",
            "rotation" => "Rotational assignment required",
            "temporary-duty" => "Temporary-duty assignment required",
            "required-unusual-relocation" => "Relocation required",
            "possible-deployment" => "Deployment may be required",
            "possible-extended-assignment" => "Extended assignment may be required",
            _ => signal.Confidence == "strong"
                ? "Deployment or relocation required"
                : "Possible deployment or relocation"
        };

    private static string NormalizeEvidence(string value)
    {
        var normalized = NormalizeSentence(value);
        return normalized.Length <= 360 ? normalized : normalized[..357] + "…";
    }

    private static string NormalizeSentence(string value) =>
        WhitespacePattern.Replace(value ?? "", " ").Trim(' ', '.', ';', '\u2022');

    private static Regex CreateRegex(string pattern) =>
        new(pattern, Options, RegexTimeout);
}
