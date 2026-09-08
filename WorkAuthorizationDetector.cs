using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

/// <summary>
/// Executes the packaged work-authorization rules without embedded domain vocabulary.
/// </summary>
public sealed class WorkAuthorizationDetector
{
    public const int CurrentAnalysisVersion = 4;
    private readonly WorkAuthorizationRules rules;
    public WorkAuthorizationDetector() : this(WorkAuthorizationRules.Default) { }
    internal WorkAuthorizationDetector(WorkAuthorizationRules rules) => this.rules = rules;

    public WorkAuthorizationAnalysis Analyze(string descriptionHtml)
    {
        try { return Execute(descriptionHtml); }
        catch (RegexMatchTimeoutException ex)
        {
            throw new InvalidOperationException($"Work-authorization rules {rules.Version} ({rules.Fingerprint}) exceeded the {rules.Rules.RegexTimeoutMilliseconds}ms regex timeout.", ex);
        }
    }

    private WorkAuthorizationAnalysis Execute(string descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml)) return NoneSpecified();
        var evidence = new List<string>();
        var eligibility = "noneSpecified";
        var sponsorship = "noneSpecified";
        var strength = "none";
        var sponsorshipStrength = "none";
        string? countryCode = null;
        var sectionContext = "mentioned";

        foreach (var segment in Segments(descriptionHtml))
        {
            var section = rules.Rules.SectionRules.FirstOrDefault(rule => rules.Pattern(rule.PatternId).IsMatch(segment));
            if (section is not null)
            {
                sectionContext = section.Result;
                continue;
            }
            foreach (var rule in rules.Rules.SponsorshipRules)
            {
                var match = rules.Pattern(rule.PatternId).Match(segment);
                if (!match.Success || rule.UnlessPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) continue;
                sponsorship = rule.Result;
                sponsorshipStrength = rule.Strength;
                evidence.Add(Evidence(segment, match.Index));
            }
            foreach (var rule in rules.Rules.EligibilityRules)
            {
                var match = rules.Pattern(rule.PatternId).Match(segment);
                if (!match.Success || (rule.OnlyWhenUnset && eligibility != "noneSpecified") ||
                    (rule.AnyPatternIds.Length > 0 && !rule.AnyPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) ||
                    rule.UnlessPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) continue;

                // A recognized branch consumes this segment even if its first-specific guard declines it.
                if (rule.Application == "firstSpecific" && eligibility is not ("noneSpecified" or "exportControlled")) break;
                if (rule.Application != "fillUnset" || eligibility == "noneSpecified") eligibility = rule.Result;
                if (rule.CountryCode is not null) countryCode = rule.CountryCode;
                strength = rule.Strength == "section" ? sectionContext == "preferred" ? "preferred" : "strict" : rule.Strength;
                if (rule.ConditionalPatternId is not null && rules.Pattern(rule.ConditionalPatternId).IsMatch(segment))
                    strength = rule.ConditionalStrength!;
                evidence.Add(Evidence(segment, rule.EvidenceIndex == "segmentStart" ? 0 : match.Index));
                break;
            }
        }
        if (eligibility == "noneSpecified" && sponsorship == "noneSpecified") return NoneSpecified();
        return new WorkAuthorizationAnalysis(eligibility, sponsorship, strength, sponsorshipStrength, countryCode,
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
            strength is "strict" or "preferred" || sponsorshipStrength == "strict" ? "parsed" : "review",
            CurrentAnalysisVersion);
    }

    public JobRecord AnalyzeJob(JobRecord job) => job with { WorkAuthorization = Analyze(job.DescriptionHtml) };

    private IEnumerable<string> Segments(string html)
    {
        var timeout = TimeSpan.FromMilliseconds(rules.Rules.RegexTimeoutMilliseconds);
        var blockSeparated = Regex.Replace(html,
            @"</?(?:p|div|li|ul|ol|h[1-6]|br|section|article)[^>]*>", "\n", RegexOptions.IgnoreCase, timeout);
        var plain = WebUtility.HtmlDecode(Regex.Replace(blockSeparated, "<[^>]+>", " ", RegexOptions.Singleline, timeout));
        foreach (var normalization in rules.Rules.Normalizations)
            plain = rules.Pattern(normalization.PatternId).Replace(plain, normalization.Replacement);
        return Regex.Split(plain, @"(?:\r?\n)+|(?<=[.!?;])\s+(?=[A-Z])", RegexOptions.None, timeout)
            .Select(value => Regex.Replace(value, @"\s+", " ", RegexOptions.None, timeout).Trim())
            .Where(value => value.Length > 0);
    }

    private static string Evidence(string segment, int index)
    {
        const int maximum = 320;
        if (segment.Length <= maximum) return segment;
        var start = Math.Clamp(index - 80, 0, segment.Length - maximum);
        return $"{(start > 0 ? "…" : "")}{segment.Substring(start, maximum)}…";
    }

    private static WorkAuthorizationAnalysis NoneSpecified() => new(
        "noneSpecified", "noneSpecified", "none", "none", null, [], "not-mentioned", CurrentAnalysisVersion);
}
