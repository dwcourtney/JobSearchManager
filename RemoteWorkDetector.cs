using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Conservatively checks remote-designated postings for current-job obligations
/// that materially conflict with ordinary work-from-home expectations.
/// </summary>
public sealed class RemoteWorkDetector
{
    public const int CurrentAnalysisVersion = 3;
    private readonly RemoteWorkRules rules;
    public RemoteWorkDetector() : this(RemoteWorkRules.Default) { }
    internal RemoteWorkDetector(RemoteWorkRules rules) => this.rules = rules;
    public string RulesetVersion => rules.Version;
    public string RulesetFingerprint => rules.Fingerprint;

    public RemoteWorkAnalysis Analyze(string title, string primaryLocation,
        IReadOnlyList<string> additionalLocations, string descriptionHtml)
    {
        try { return Execute(title, primaryLocation, additionalLocations, descriptionHtml); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException($"Remote-work rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex);
        }
    }

    private RemoteWorkAnalysis Execute(
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
        var isRemoteDesignated = rules.Pattern("RemoteDesignationPattern").IsMatch(metadata) ||
            rules.Pattern("ExplicitRemoteRolePattern").IsMatch(plainDescription);
        if (!isRemoteDesignated)
        {
            return Empty(false, "not-remote-designated");
        }

        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return Empty(true, "description-unavailable");
        }

        var separatedHtml = rules.Pattern("BlockEndPattern").Replace(descriptionHtml, ". ");
        var text = JobAnalysis.HtmlToPlainText(separatedHtml);
        var sentences = rules.Pattern("SentenceSplitPattern").Split(text)
            .Select(NormalizeEvidence)
            .Where(sentence => sentence.Length > 0)
            .ToArray();
        var signals = new List<RemoteWorkSignal>();

        foreach (var sentence in sentences)
        {
            var historicalOnly = rules.Pattern("HistoricalExperiencePattern").IsMatch(sentence) &&
                !rules.Pattern("CurrentObligationPattern").IsMatch(sentence);
            if (!historicalOnly)
            {
                foreach (var rule in rules.Rules.SignalRules)
                {
                    if (rule.UnlessContains is not null &&
                        sentence.Contains(rule.UnlessContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (rules.Pattern(rule.PatternId).IsMatch(sentence))
                    {
                        if (rule.OverridePatternId is not null && rules.Pattern(rule.OverridePatternId).IsMatch(sentence))
                        {
                            signals.Add(new RemoteWorkSignal(
                                rule.Category,
                                rule.OverrideConcernLevel!,
                                rule.OverrideReason!,
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

    private void AddTravelSignal(List<RemoteWorkSignal> signals, string sentence)
    {
        foreach (Match match in rules.Pattern("TravelPercentagePattern").Matches(sentence))
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
            var band = rules.Rules.TravelBands.First(band => maximum <= band.MaximumPercent);
            var range = maximum == minimum ? $"{maximum}%" : $"{minimum}-{maximum}%";
            signals.Add(new RemoteWorkSignal(
                band.Category,
                band.ConcernLevel,
                rules.Rules.TravelReasonTemplate.Replace("{range}", range, StringComparison.Ordinal),
                NormalizeEvidence(sentence)));
        }

        if (rules.Pattern(rules.Rules.FrequentTravel.PatternId).IsMatch(sentence))
        {
            signals.Add(new RemoteWorkSignal(
                rules.Rules.FrequentTravel.Category,
                rules.Rules.FrequentTravel.ConcernLevel,
                rules.Rules.FrequentTravel.Reason,
                NormalizeEvidence(sentence)));
        }
    }

    private void AddSignal(List<RemoteWorkSignal> signals, RemoteWorkRules.SignalRule rule, string sentence) =>
        signals.Add(new RemoteWorkSignal(
            rule.Category,
            rule.ConcernLevel,
            rule.Reason,
            NormalizeEvidence(sentence)));

    private RemoteWorkAnalysis Empty(bool remote, string status) => new(
        remote, "none", null, [], status, CurrentAnalysisVersion);

    private string NormalizeEvidence(string value)
    {
        var normalized = rules.Pattern("WhitespacePattern").Replace(value, " ").Trim(' ', '.', ';', '\u2022');
        return normalized.Length <= 300 ? normalized : normalized[..297] + "...";
    }

}
