using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal static partial class JobAnalysis
{

    public static SalaryAnalysis AnalyzeSalary(string descriptionHtml) => AnalyzeSalary(descriptionHtml, SalaryRules.Default);

    internal static SalaryAnalysis AnalyzeSalary(string descriptionHtml, SalaryRules rules)
    {
        try { return ExecuteSalary(descriptionHtml, rules); }
        catch (RegexMatchTimeoutException ex)
        { throw new InvalidOperationException($"Salary rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex); }
    }

    internal sealed record SalaryObservation(FactualObservation Observation, SalaryAnalysis LegacyValue, int LegacyPriority,
        [property: System.Text.Json.Serialization.JsonIgnore] Exception? Error = null);
    internal sealed record SalaryObservationSet(string OriginalHtml, string[] Lines,
        SalaryObservation[] Observations, string EmptyStatus);

    private static SalaryAnalysis ExecuteSalary(string descriptionHtml, SalaryRules rules) =>
        SummarizeSalary(ExtractSalary(descriptionHtml, rules));

    internal static SalaryObservationSet ExtractSalary(string html, SalaryRules rules)
    {
        var text = HtmlToPlainText(html);
        var lines = HtmlToTextLines(html);
        var result = new List<SalaryObservation>();
        var choices = new[] { ("SpecificSalaryRegex", "specific-role-range", 0),
            ("UsdSalaryRangeRegex", "usd-pay-range", 2), ("StandardPayRangeRegex", "standard-pay-range", 3),
            ("CompensationRangeRegex", "compensation-range", 4), ("SeparatedSalaryBoundsRegex", "separate-salary-bounds", 5) };
        foreach (var (pattern, status, priority) in choices)
        {
            try { Add(rules.Pattern(pattern).Match(text), status, priority, pattern, text, -1, false); }
            catch (Exception ex) when (ex is RegexMatchTimeoutException or OverflowException)
            { AddError(ex, priority, pattern); }
        }
        try
        {
        for (var index = 0; index < lines.Length; index++)
        {
            if (!rules.Pattern("SummaryPayHeadingRegex").IsMatch(lines[index])) continue;
            Add(rules.Pattern("SummaryPayRangeRegex").Match(lines[index]), "summary-pay-range", 1,
                "SummaryPayRangeRegex", lines[index], index, true);
            for (var next = index + 1; next < lines.Length; next++)
            {
                if (rules.Pattern("SummaryPayHeadingRegex").IsMatch(lines[next])) break;
                var match = rules.Pattern("SummarySectionRangeRegex").Match(lines[next]);
                if (!match.Success) break;
                Add(match, "summary-pay-range", 1, "SummarySectionRangeRegex", lines[next], next, true);
                index = next;
            }
        }
        }
        catch (Exception ex) when (ex is RegexMatchTimeoutException or OverflowException)
        { AddError(ex, 1, "SummaryPayRangeRegex"); }
        return new(html, lines, result.ToArray(), string.IsNullOrWhiteSpace(html) ? "description-unavailable" :
            rules.Rules.UnparseablePhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)) ? "unparseable" : "not-found");

        void AddError(Exception error, int priority, string ruleId)
        {
            var observation = FactObservations.Create("compensation", "legacy-extraction-error",
                new FactValue("unresolved", text), "unknown", error.GetType().Name,
                new FactScope(null, null, null, null, null, text), new FactEvidence(text, "salary-flat-text-v1", -1, 0, text.Length),
                "salary-v1", rules.Fingerprint, ruleId);
            result.Add(new(observation, new SalaryAnalysis(null, null, "unknown", "unparseable"), priority, error));
        }

        void Add(Match match, string status, int priority, string ruleId, string evidence, int line, bool summary)
        {
            if (!match.Success) return;
            var legacy = CreateSalaryAnalysis(match, status, rules);
            if (summary && !(match.Groups["minimumDollar"].Success || match.Groups["maximumDollar"].Success ||
                legacy.Period != "unknown" || legacy.Maximum >= 10_000m)) return;
            var observation = FactObservations.Create("compensation", "legacy-range-candidate",
                new FactValue("range", match.Value, Lower: legacy.Minimum, Upper: legacy.Maximum, Unit: legacy.Period),
                "unknown", legacy.ParseStatus, new FactScope(null, null, null, null, null, evidence),
                new FactEvidence(evidence, line < 0 ? "salary-flat-text-v1" : "salary-line-v1", line, match.Index, match.Length),
                "salary-v1", rules.Fingerprint, ruleId);
            result.Add(new(observation, legacy, priority));
        }
    }

    internal static SalaryAnalysis SummarizeSalary(SalaryObservationSet observations)
    {
        if (observations.Observations.Length == 0) return new(null, null, "unknown", observations.EmptyStatus);
        var priority = observations.Observations.Min(o => o.LegacyPriority);
        var selected = observations.Observations.Where(o => o.LegacyPriority == priority).ToArray();
        foreach (var candidate in priority == 1 ? selected : selected.Take(1))
            if (candidate.Error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(candidate.Error).Throw();
        return priority == 1 ? AggregateSummaryPayRanges(selected.Select(o => o.LegacyValue).ToArray()) : selected[0].LegacyValue;
    }

    private static SalaryAnalysis AggregateSummaryPayRanges(
        IReadOnlyList<SalaryAnalysis> ranges)
    {
        if (ranges.Any(range => range.Minimum is null || range.Maximum is null))
        {
            return new SalaryAnalysis(null, null, "unknown", "unparseable");
        }

        var periods = ranges
            .Select(range => range.Period)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (periods.Length != 1)
        {
            // Combining hourly and annual figures would produce a misleading card.
            return new SalaryAnalysis(null, null, "unknown", "ambiguous-mixed-periods");
        }

        var minimum = ranges.Min(range => range.Minimum!.Value);
        var maximum = ranges.Max(range => range.Maximum!.Value);
        var parseStatus = ranges.Count == 1
            ? ranges[0].ParseStatus
            : periods[0] == "hourly"
                ? "hourly-unconverted-summary-aggregate"
                : periods[0] == "unknown"
                    ? "ambiguous-period-summary-aggregate"
                    : "summary-pay-range-aggregate";
        return new SalaryAnalysis(minimum, maximum, periods[0], parseStatus);
    }

    public static RemoteLocationAnalysis AnalyzeRemoteLocation(
        string descriptionHtml,
        string primaryLocation,
        IReadOnlyList<string> additionalLocations) =>
        AnalyzeRemoteLocation(descriptionHtml, primaryLocation, additionalLocations, GeographicRestrictionRules.Default);

    internal static RemoteLocationAnalysis AnalyzeRemoteLocation(string descriptionHtml, string primaryLocation,
        IReadOnlyList<string> additionalLocations, GeographicRestrictionRules rules)
    {
        try { return ExecuteRemoteLocation(descriptionHtml, primaryLocation, additionalLocations, rules); }
        catch (RegexMatchTimeoutException ex)
        { throw new InvalidOperationException($"Geographic-restriction rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex); }
    }

    private static RemoteLocationAnalysis ExecuteRemoteLocation(string descriptionHtml, string primaryLocation,
        IReadOnlyList<string> additionalLocations, GeographicRestrictionRules rules)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return new RemoteLocationAnalysis(false, null, null);
        }

        var text = HtmlToPlainText(descriptionHtml);
        var isRemoteListing = primaryLocation.Contains(rules.Rules.RemoteDesignationCues.PrimaryLocation, StringComparison.OrdinalIgnoreCase) ||
            additionalLocations.Any(location =>
                location.Contains(rules.Rules.RemoteDesignationCues.AdditionalLocation, StringComparison.OrdinalIgnoreCase)) ||
            text.Contains(rules.Rules.RemoteDesignationCues.Description, StringComparison.OrdinalIgnoreCase);

        if (!isRemoteListing)
        {
            return new RemoteLocationAnalysis(false, null, null);
        }

        var first = ExtractGeographicCandidates(text, rules).FirstOrDefault(candidate => candidate.Accepted);
        return first is null ? new RemoteLocationAnalysis(false, null, null) : new RemoteLocationAnalysis(true, first.Category, first.Snippet);
    }

    internal sealed record GeographicCandidate(string RuleId, string Category, string Sentence, int SentenceIndex,
        int Start, int Length, bool Accepted, string Snippet);
    internal static IEnumerable<GeographicCandidate> ExtractGeographicCandidates(string text, GeographicRestrictionRules rules)
    {
        var sentences = SentenceSplitRegex().Split(text);
        foreach (var rule in rules.OrderedRules)
        for (var index = 0; index < sentences.Length; index++)
        {
            var sentence = sentences[index];
            var match = rules.Pattern(rule.PatternId).Match(sentence);
            if (!match.Success) continue;
            yield return new(rule.Id, rule.Category, sentence, index, match.Index, match.Length,
                !rule.UnlessPatternIds.Any(id => rules.Pattern(id).IsMatch(sentence)), CreateSnippet(sentence, match.Index));
        }
    }

    internal static string HtmlToPlainText(string html)
    {
        var withSeparators = BlockTagRegex().Replace(html, " ");
        var withoutTags = AnyTagRegex().Replace(withSeparators, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static string[] HtmlToTextLines(string html)
    {
        var withSeparators = BlockTagRegex().Replace(html, "\n");
        var withoutTags = AnyTagRegex().Replace(withSeparators, " ");
        return WebUtility.HtmlDecode(withoutTags)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }

    private static SalaryAnalysis CreateSalaryAnalysis(Match match, string parsedStatus, SalaryRules rules)
    {
        if (!decimal.TryParse(
                RemoveWhitespace(match.Groups["minimum"].Value),
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var minimum) ||
            !decimal.TryParse(
                RemoveWhitespace(match.Groups["maximum"].Value),
                NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var maximum))
        {
            return new SalaryAnalysis(null, null, "unknown", "unparseable");
        }
        if (match.Groups["minimumScale"].Success)
        {
            minimum *= 1_000m;
        }
        if (match.Groups["maximumScale"].Success)
        {
            maximum *= 1_000m;
        }

        var nearbyText = match.Value + " " + match.Groups["context"].Value;
        if (rules.Pattern("HourlyCueRegex").IsMatch(nearbyText))
        {
            // Do not compare hourly dollars directly with an annual threshold.
            return new SalaryAnalysis(minimum, maximum, "hourly", "hourly-unconverted");
        }

        if (rules.Pattern("AnnualCueRegex").IsMatch(nearbyText) || maximum >= 10_000m)
        {
            return new SalaryAnalysis(minimum, maximum, "annual", parsedStatus);
        }

        // A small range without an explicit period is deliberately not assumed annual.
        return new SalaryAnalysis(minimum, maximum, "unknown", "ambiguous-period");
    }

    private static string RemoveWhitespace(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static string CreateSnippet(string sentence, int matchIndex)
    {
        var normalized = WhitespaceRegex().Replace(sentence, " ").Trim();
        const int maximumLength = 340;
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var start = Math.Max(0, matchIndex - 80);
        if (start > 0)
        {
            var nextSpace = normalized.IndexOf(' ', start);
            start = nextSpace >= 0 ? nextSpace + 1 : start;
        }

        var length = Math.Min(maximumLength, normalized.Length - start);
        var snippet = normalized.Substring(start, length);
        if (start + length < normalized.Length)
        {
            var lastSpace = snippet.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                snippet = snippet[..lastSpace];
            }
        }

        return (start > 0 ? "…" : "") + snippet +
            (start + length < normalized.Length ? "…" : "");
    }

    [GeneratedRegex(@"(?is)<br\s*/?>|</?(?:p|div|h[1-6]|li|ul|ol)[^>]*>")]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"(?is)<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z#*])")]
    private static partial Regex SentenceSplitRegex();

}
