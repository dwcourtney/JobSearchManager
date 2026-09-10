using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Conservatively identifies current-job deployment, rotation, relocation, or
/// extended-presence obligations that structured location metadata can conceal.
/// Location names alone are never sufficient evidence.
/// </summary>
public sealed class ExtendedLocationRequirementDetector
{
    public const int CurrentAnalysisVersion = 4;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex BlockEndPattern = CreateRegex(
        @"</(?:p|li|div|h[1-6])\s*>|<br\s*/?>");
    private static readonly Regex SentenceSplitPattern = CreateRegex(
        @"(?<=[.!?])\s+(?=[A-Z0-9#*])");
    private static readonly Regex WhitespacePattern = CreateRegex(@"\s+");
    private readonly ExtendedLocationRules rules;
    public ExtendedLocationRequirementDetector() : this(ExtendedLocationRules.Default) { }
    internal ExtendedLocationRequirementDetector(ExtendedLocationRules rules) => this.rules = rules;
    public string RulesetVersion => rules.Version;
    public string RulesetFingerprint => rules.Fingerprint;

    public ExtendedLocationRequirementAnalysis Analyze(string title, string primaryLocation,
        IReadOnlyList<string> additionalLocations, string descriptionHtml)
    {
        try { return Execute(title, primaryLocation, additionalLocations, descriptionHtml); }
        catch (RegexMatchTimeoutException ex)
        { throw new InvalidOperationException($"Extended-location rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex); }
    }

    internal sealed record ObservationSet(string Title, string PrimaryLocation, IReadOnlyList<string> AdditionalLocations, string Text, string? EmptyStatus, ExtendedLocationRequirementSignal[] Candidates);

    private ExtendedLocationRequirementAnalysis Execute(string title, string primaryLocation, IReadOnlyList<string> additionalLocations, string descriptionHtml) =>
        Summarize(Extract(title, primaryLocation, additionalLocations, descriptionHtml));

    internal ObservationSet Extract(
        string title,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations,
        string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new(title, primaryLocation, additionalLocations, "", "description-unavailable", []);
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
            var historicalOnly = rules.Pattern("HistoricalPattern").IsMatch(sentence) &&
                !rules.Pattern("CurrentObligationPattern").IsMatch(sentence);
            if (historicalOnly)
            {
                continue;
            }

            AddExtendedAwayDurationSignal(signals, sentence);

            foreach (var rule in rules.Rules.SignalRules)
            {
                AddIfMatch(signals, rule, sentence);
            }
        }

        return new(title, primaryLocation, additionalLocations, text, null, signals.ToArray());
    }

    internal ExtendedLocationRequirementAnalysis Summarize(ObservationSet extracted)
    {
        if (extracted.EmptyStatus is not null) return Empty(extracted.EmptyStatus);
        var title = extracted.Title;
        var primaryLocation = extracted.PrimaryLocation;
        var additionalLocations = extracted.AdditionalLocations;
        var text = extracted.Text;
        var ordered = extracted.Candidates
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
              rules.Rules.UnspecifiedDestination;
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

    private void AddIfMatch(
        List<ExtendedLocationRequirementSignal> signals,
        ExtendedLocationRules.SignalRule rule,
        string sentence)
    {
        if (!rules.Pattern(rule.PatternId).IsMatch(sentence))
        {
            return;
        }
        signals.Add(new ExtendedLocationRequirementSignal(
            rule.Category,
            rule.Confidence,
            rule.Reason,
            NormalizeEvidence(sentence)));
    }

    private void AddExtendedAwayDurationSignal(
        List<ExtendedLocationRequirementSignal> signals,
        string sentence)
    {
        if (!(rules.Pattern("AwayPresencePattern").IsMatch(sentence) || rules.Pattern("LongDurationTravelPresencePattern").IsMatch(sentence)) ||
            !rules.Pattern("DefiniteObligationPattern").IsMatch(sentence) ||
            (rules.Pattern("ConditionalOnlyPattern").IsMatch(sentence) &&
             !rules.Pattern("ExplicitObligationPattern").IsMatch(sentence)))
        {
            return;
        }

        foreach (Match match in rules.Pattern("DurationPattern").Matches(sentence))
        {
            if (!TryDurationDays(match, out var days) || days < rules.Rules.Duration.MinimumDays)
            {
                continue;
            }

            var contextStart = Math.Max(0, match.Index - 150);
            var contextLength = Math.Min(sentence.Length - contextStart, match.Length + 300);
            var context = sentence.Substring(contextStart, contextLength);
            if (!(rules.Pattern("AwayPresencePattern").IsMatch(context) || rules.Pattern("LongDurationTravelPresencePattern").IsMatch(context)))
            {
                continue;
            }

            signals.Add(new ExtendedLocationRequirementSignal(
                rules.Rules.Duration.Category,
                rules.Rules.Duration.Confidence,
                rules.Rules.Duration.Reason,
                EvidenceAround(sentence, match.Index, match.Length)));
            return;
        }
    }

    private bool TryDurationDays(Match match, out int days)
    {
        days = 0;
        if (!TryNumber(match.Groups["value"].Value, out var value))
        {
            return false;
        }

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var multiplier = rules.Rules.Duration.UnitPrefixes.FirstOrDefault(item => unit.StartsWith(item.Key, StringComparison.Ordinal));
        days = value * (multiplier.Key is null ? rules.Rules.Duration.DefaultDaysPerUnit : multiplier.Value);
        return true;
    }

    private bool TryNumber(string value, out int number)
    {
        if (int.TryParse(value, out number))
        {
            return true;
        }

        number = rules.Rules.Duration.Numbers.GetValueOrDefault(value.ToLowerInvariant());
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

    private string? FindDestination(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return rules.Rules.Destinations.FirstOrDefault(destination => rules.Pattern(destination.PatternId).IsMatch(value))?.Name;
    }

    private string BuildSummary(
        ExtendedLocationRequirementSignal signal,
        string destination) => rules.Rules.Summaries.GetValueOrDefault(signal.Category,
            signal.Confidence == "strong" ? rules.Rules.StrongFallbackSummary : rules.Rules.QuestionableFallbackSummary)
            .Replace("{destination}", destination, StringComparison.Ordinal);

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
