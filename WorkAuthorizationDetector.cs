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

    internal sealed record Candidate(FactualObservation Observation, string LegacyEvidence,
        WorkAuthorizationRules.SponsorshipRule? Sponsorship,
        WorkAuthorizationRules.EligibilityRule? Eligibility, bool Conditional);
    internal sealed record SegmentObservations(string Text, string? SectionChange, Candidate[] Candidates);
    internal sealed record ObservationSet(string OriginalHtml, SegmentObservations[] Segments);

    private WorkAuthorizationAnalysis Execute(string descriptionHtml) => Summarize(Extract(descriptionHtml));

    internal ObservationSet Extract(string html, string? provider = null)
    {
        var result = new List<SegmentObservations>();
        var sectionContext = "mentioned";
        foreach (var segment in Segments(html))
        {
            var section = rules.Rules.SectionRules.FirstOrDefault(rule => rules.Pattern(rule.PatternId).IsMatch(segment));
            if (section is not null)
            {
                sectionContext = section.Result;
                result.Add(new(segment, section.Result, []));
                continue;
            }
            var candidates = new List<Candidate>();
            foreach (var rule in rules.Rules.SponsorshipRules)
            {
                var match = rules.Pattern(rule.PatternId).Match(segment);
                if (!match.Success || rule.UnlessPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) continue;
                candidates.Add(new(Create("sponsorship", rule.Result, rule.Id, match, [], "strict"),
                    Evidence(segment, match.Index), rule, null, false));
            }
            foreach (var rule in rules.Rules.EligibilityRules)
            {
                var match = rules.Pattern(rule.PatternId).Match(segment);
                if (!match.Success ||
                    (rule.AnyPatternIds.Length > 0 && !rule.AnyPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) ||
                    rule.UnlessPatternIds.Any(id => rules.Pattern(id).IsMatch(segment))) continue;
                var conditional = rule.ConditionalPatternId is not null && rules.Pattern(rule.ConditionalPatternId).IsMatch(segment);
                var strength = rule.Strength == "section" ? sectionContext == "preferred" ? "preferred" : "strict" : rule.Strength;
                if (conditional) strength = rule.ConditionalStrength!;
                candidates.Add(new(Create("eligibility", rule.Result, rule.Id, match,
                    rule.Result == "usCitizenOrPermanentResident" ? ["usCitizen", "permanentResident"] : [], strength),
                    Evidence(segment, rule.EvidenceIndex == "segmentStart" ? 0 : match.Index), null, rule, conditional));
            }
            result.Add(new(segment, null, candidates.ToArray()));

            FactualObservation Create(string type, string value, string ruleId, Match match, string[] alternatives, string strength) =>
                FactObservations.Create("work-authorization", type, new FactValue("categorical", match.Value, value, Alternatives: alternatives),
                    strength == "strict" ? "required" : strength == "preferred" ? "preferred" : "unknown",
                    strength, new FactScope(sectionContext, null, null, null, null, segment),
                    new FactEvidence(segment, "authorization-normalized-segment-v1", result.Count, match.Index, match.Length),
                    "work-authorization-v1/analysis-4", rules.Fingerprint, ruleId,
                    provider: provider, connective: alternatives.Length > 0 ? "or" : "unspecified");
        }
        return new(html, result.ToArray());
    }

    internal static WorkAuthorizationAnalysis Summarize(ObservationSet observations)
    {
        var evidence = new List<string>();
        var eligibility = "noneSpecified";
        var sponsorship = "noneSpecified";
        var strength = "none";
        var sponsorshipStrength = "none";
        string? countryCode = null;
        var sectionContext = "mentioned";
        foreach (var segment in observations.Segments)
        {
            if (segment.SectionChange is not null) { sectionContext = segment.SectionChange; continue; }
            foreach (var candidate in segment.Candidates.Where(c => c.Sponsorship is not null))
            {
                var rule = candidate.Sponsorship!;
                sponsorship = rule.Result;
                sponsorshipStrength = rule.Strength;
                evidence.Add(candidate.LegacyEvidence);
            }
            foreach (var candidate in segment.Candidates.Where(c => c.Eligibility is not null))
            {
                var rule = candidate.Eligibility!;
                if (rule.OnlyWhenUnset && eligibility != "noneSpecified") continue;
                // Legacy first-specific selection remains here, after observation retention.
                if (rule.Application == "firstSpecific" && eligibility is not ("noneSpecified" or "exportControlled")) break;
                if (rule.Application != "fillUnset" || eligibility == "noneSpecified") eligibility = rule.Result;
                if (rule.CountryCode is not null) countryCode = rule.CountryCode;
                strength = rule.Strength == "section" ? sectionContext == "preferred" ? "preferred" : "strict" : rule.Strength;
                if (candidate.Conditional) strength = rule.ConditionalStrength!;
                evidence.Add(candidate.LegacyEvidence);
                break;
            }
        }
        if (eligibility == "noneSpecified" && sponsorship == "noneSpecified") return NoneSpecified();
        return new WorkAuthorizationAnalysis(eligibility, sponsorship, strength, sponsorshipStrength, countryCode,
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
            strength is "strict" or "preferred" || sponsorshipStrength == "strict" ? "parsed" : "review", CurrentAnalysisVersion);
    }

    internal FactObservationDocument Observe(string html, string? provider = null)
    {
        var extracted = Extract(html, provider);
        var supplemental = FactualObservationRules.Default;
        var all = extracted.Segments.SelectMany(s => s.Candidates).SelectMany(c => {
            var o = c.Observation;
            var patternId = c.Sponsorship?.PatternId ?? c.Eligibility!.PatternId;
            return rules.Pattern(patternId).Matches(o.Evidence.Text).Cast<Match>().Select(match => {
                var start = o.Evidence.Text.LastIndexOf(';', Math.Max(0, match.Index - 1)) + 1;
                var end = o.Evidence.Text.IndexOf(';', match.Index + match.Length);
                if (end < 0) end = o.Evidence.Text.Length;
                var clause = o.Evidence.Text[start..end];
                return FactObservations.Create(o.Domain, o.Type, o.Value with { Raw = match.Value },
                    supplemental.Obligation(clause, o.Scope.Section),
                    supplemental.Pattern("conditional").IsMatch(clause) ? "conditional" : o.Qualifier,
                    o.Scope with { RawApplicability = clause },
                    o.Evidence with { Start = match.Index, Length = match.Length }, o.ParserId, o.RulesHash,
                    o.RuleId, o.Source, provider,
                    o.Value.Alternatives?.Count > 0 ? "or" : supplemental.Connective(clause), o.Exclusions,
                    supplemental.Pattern("not-required").IsMatch(clause) ? "negated-requirement" : "affirmed");
            });
        }).ToList();
        for (var i = 0; i < extracted.Segments.Length; i++)
        {
            var segment = extracted.Segments[i];
            foreach (var (patternId, type, code) in new[] {
                ("sponsorship-available", "sponsorship", "available"),
                ("sponsorship-unavailable", "sponsorship", "notAvailable"),
                ("permanent-residency", "eligibility", "permanentResident") })
            foreach (Match match in supplemental.Pattern(patternId).Matches(segment.Text))
            {
                // A supplemental positive is retained even alongside a negative. It cannot enter v9's projection.
                all.Add(FactObservations.Create("work-authorization", type,
                    new FactValue("categorical", match.Value, code), supplemental.Obligation(segment.Text),
                    supplemental.Pattern("conditional").IsMatch(segment.Text) ? "conditional" : "explicit",
                    supplemental.Scope(segment.Text), new FactEvidence(segment.Text, "authorization-normalized-segment-v1", i, match.Index, match.Length),
                    "factual-observations-v1", supplemental.Fingerprint, patternId, provider: provider,
                    connective: supplemental.Connective(segment.Text)));
            }
        }
        return new(1, FactObservations.Hash(html), html, extracted.Segments.Select(s => s.Text).ToArray(), FactObservations.LinkSponsorship(all));
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
