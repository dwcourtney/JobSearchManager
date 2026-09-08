using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed class RegexSemanticClassifier : IConceptMatcher
{
    private const RegexOptions BaseOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex WhitespacePattern = new(@"\s+", BaseOptions,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex LocalNegationPattern = new(
        @"(?:\b(?:do|does|did|is|are|was|were|will|would|should|can|cannot)\s+not|\bnot\s+(?:directly\s+)?(?:responsible\s+for\s+)?|\bno\s+(?:direct\s+)?)$",
        BaseOptions, TimeSpan.FromMilliseconds(100));
    private readonly ConceptRegexPolicy _policy;
    private readonly JobConceptCatalog _catalog;
    private readonly CompiledRuleset _current;

    // One immutable validated snapshot is compiled before the normal host listens.
    internal RegexSemanticClassifier(ConceptMatchSnapshot snapshot, JobConceptCatalog catalog, ConceptRegexPolicy policy)
    {
        _catalog = catalog;
        _policy = policy;
        _current = Compile(snapshot);
    }

    public string RulesetFingerprint => _current?.Fingerprint
        ?? throw new InvalidOperationException("Semantic RegEx rules have not been loaded.");

    public int ActiveRuleCount => _current.Rules.Count;

    public RegexClassification Classify(string title, string descriptionHtml,
        RemoteWorkAnalysis? remoteWork, ExtendedLocationRequirementAnalysis? extendedLocation,
        bool productionUsage)
    {
        if (productionUsage) throw new InvalidOperationException("Immutable concept matching does not record lifecycle telemetry.");
        var current = _current
            ?? throw new InvalidOperationException("Semantic RegEx rules have not been loaded.");
        var description = string.IsNullOrWhiteSpace(descriptionHtml)
            ? "" : JobAnalysis.HtmlToPlainText(descriptionHtml);
        var corpus = string.Join('\n', [title ?? "", description]);
        var results = new Dictionary<string, DetectedJobConcept>(StringComparer.Ordinal);
        var matchedRuleIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var timedOutRuleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var concept in _catalog.Concepts)
        {
            if (!current.ByConcept.TryGetValue(concept.Id, out var rules)) continue;
            var matched = new List<(CompiledRule Rule, string Evidence)>();
            var excluded = rules.Where(item => item.Rule.RuleType == ConceptRuleTypes.Exclusion)
                .Select(item => (item, Match: FirstMatch(item, title ?? "",
                    rejectLocalNegation: false, Timeout)))
                .Where(item => item.Match is not null)
                .ToArray();
            if (excluded.Length > 0)
            {
                continue;
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType is
                         ConceptRuleTypes.TitleEvidence or ConceptRuleTypes.PositiveEvidence))
            {
                var input = rule.Rule.Scope == ConceptRuleScopes.Title ? title ?? "" :
                    rule.Rule.Scope == ConceptRuleScopes.Posting ? description : corpus;
                var match = FirstMatch(rule, input,
                    rejectLocalNegation: rule.Rule.RuleType == ConceptRuleTypes.PositiveEvidence,
                    Timeout);
                if (match is not null) matched.Add((rule, NormalizeEvidence(match.Value)));
            }

            foreach (var group in rules.Where(item => item.Rule.RuleType == ConceptRuleTypes.RequiredContext)
                         .GroupBy(item => item.Rule.ContextGroupId, StringComparer.Ordinal))
            {
                var groupMatches = group.Select(item =>
                {
                    var input = item.Rule.Scope == ConceptRuleScopes.Title ? title ?? "" :
                        item.Rule.Scope == ConceptRuleScopes.Posting ? description : corpus;
                    return (Rule: item, Match: FirstMatch(item, input,
                        rejectLocalNegation: false, Timeout));
                }).ToArray();
                if (groupMatches.All(item => item.Match is not null))
                    matched.AddRange(groupMatches.Select(item => (item.Rule,
                        NormalizeEvidence(item.Match!.Value))));
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType == ConceptRuleTypes.RemoteDesignation))
            {
                if (remoteWork?.IsRemoteDesignated == true)
                    matched.Add((rule, "Remote designation detected in the posting"));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == ConceptRuleTypes.RemoteSignal))
            {
                var signal = remoteWork?.Signals?.FirstOrDefault(item => item.Category == rule.Rule.Pattern);
                if (signal is not null) matched.Add((rule, signal.Evidence));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == ConceptRuleTypes.ExtendedLocationSignal))
            {
                var signal = extendedLocation?.Signals?.FirstOrDefault(item => item.Category == rule.Rule.Pattern);
                if (signal is not null) matched.Add((rule, signal.Evidence));
            }

            if (matched.Count == 0) continue;
            var uniqueRules = matched.Select(item => item.Rule.Rule.RuleId).Distinct(StringComparer.Ordinal).ToArray();
            var evidence = string.Join("; ", matched.Select(item => item.Evidence)
                .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase));
            results[concept.Id] = new(concept.Id, NormalizeEvidence(evidence));
            matchedRuleIds[concept.Id] = uniqueRules;
        }

        return new(ConceptFingerprint.PostingContentHash(title ?? "", description),
            current.Fingerprint, DateTimeOffset.UtcNow,
            results.Values.OrderBy(item => item.ConceptId, StringComparer.Ordinal).ToArray(),
            matchedRuleIds, timedOutRuleIds.OrderBy(item => item, StringComparer.Ordinal).ToArray());

        void Timeout(string id) => timedOutRuleIds.Add(id);
    }

    private CompiledRuleset Compile(ConceptMatchSnapshot snapshot)
    {
        var compiled = snapshot.Rules.Select(rule => new CompiledRule(rule,
            rule.RuleType is ConceptRuleTypes.RemoteDesignation or ConceptRuleTypes.RemoteSignal or
                ConceptRuleTypes.ExtendedLocationSignal ? null : CompilePattern(rule.Pattern))).ToArray();
        return new(snapshot.Fingerprint, compiled,
            compiled.GroupBy(item => item.Rule.ConceptId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CompiledRule>)group.ToArray(),
                    StringComparer.Ordinal));
    }

    private Regex CompilePattern(string pattern)
    {
        var timeout = TimeSpan.FromMilliseconds(_policy.RegexTimeoutMilliseconds);
        try { return new(pattern, BaseOptions | RegexOptions.NonBacktracking, timeout); }
        catch (NotSupportedException) { return new(pattern, BaseOptions, timeout); }
    }

    private static bool IsLocallyNegated(string corpusText, Match match)
    {
        var start = Math.Max(0, match.Index - 60);
        try { return LocalNegationPattern.IsMatch(corpusText[start..match.Index].TrimEnd()); }
        catch (RegexMatchTimeoutException) { return true; }
    }

    private static Match? FirstMatch(CompiledRule rule, string input, bool rejectLocalNegation,
        Action<string> onTimeout)
    {
        try
        {
            foreach (Match match in rule.Pattern!.Matches(input))
                if (!rejectLocalNegation || !IsLocallyNegated(input, match)) return match;
        }
        catch (RegexMatchTimeoutException)
        {
            // A recovered fallback pattern is always bounded. Under transient CPU pressure,
            // isolate its timeout as a non-match instead of discarding every other rule result.
            onTimeout(rule.Rule.RuleId);
        }
        return null;
    }

    private static string NormalizeEvidence(string value)
    {
        var normalized = WhitespacePattern.Replace(value ?? "", " ").Trim(' ', '.', ';', '\u2022');
        return normalized.Length <= 300 ? normalized : normalized[..297] + "...";
    }

    private sealed record CompiledRule(ConceptMatchRule Rule, Regex? Pattern);
    private sealed record CompiledRuleset(string Fingerprint, IReadOnlyList<CompiledRule> Rules,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledRule>> ByConcept);
}
