// Frozen from 2c8a2e2735b1b812853ff6eeb0da19d969d82ebb.
// Matching branches unchanged; store/reload/telemetry removed; test-only trace added.
// Original classifier normalized SHA256: 6aa0f195141a27c982c759cfd314b60d25de1e503ffdcd86f480ac2296db419c
using System.Net;
using System.Text.RegularExpressions;

namespace JobSearchManager;

internal sealed partial class FrozenSqliteConceptOracle
{
    private const RegexOptions BaseOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex WhitespacePattern = new(@"\s+", BaseOptions,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex LocalNegationPattern = new(
        @"(?:\b(?:do|does|did|is|are|was|were|will|would|should|can|cannot)\s+not|\bnot\s+(?:directly\s+)?(?:responsible\s+for\s+)?|\bno\s+(?:direct\s+)?)$",
        BaseOptions, TimeSpan.FromMilliseconds(100));
    private readonly JobConceptCatalog _catalog;
    private readonly SemanticRulePolicy _policy;
    private CompiledRuleset? _current;
    internal List<string> Trace { get; } = [];
    internal FrozenSqliteConceptOracle(SemanticRulesSnapshot snapshot, JobConceptCatalog catalog, SemanticRulePolicy policy)
    {
        _catalog = catalog; _policy = policy; _current = Compile(snapshot);
    }

    public RegexClassification Classify(string title, string descriptionHtml,
        RemoteWorkAnalysis? remoteWork, ExtendedLocationRequirementAnalysis? extendedLocation,
        bool productionUsage)
    {
        Trace.Clear();
        var current = Volatile.Read(ref _current)
            ?? throw new InvalidOperationException("Semantic RegEx rules have not been loaded.");
        var description = string.IsNullOrWhiteSpace(descriptionHtml)
            ? "" : HtmlToPlainText(descriptionHtml);
        var corpus = string.Join('\n', [title ?? "", description]);
        var results = new Dictionary<string, DetectedJobConcept>(StringComparer.Ordinal);
        var matchedRuleIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var timedOutRuleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var concept in _catalog.Concepts)
        {
            if (!current.ByConcept.TryGetValue(concept.Id, out var rules)) continue;
            var matched = new List<(CompiledRule Rule, string Evidence)>();
            var excluded = rules.Where(item => item.Rule.RuleType == "exclusion")
                .Select(item => (item, Match: FirstMatch(item, title ?? "",
                    rejectLocalNegation: false, Timeout)))
                .Where(item => item.Match is not null)
                .ToArray();
            Trace.Add("exclusion:" + concept.Id + ":" + string.Join(",", excluded.Select(item => item.item.Rule.RuleId)));
            if (excluded.Length > 0)
            {
                Count(excluded.Select(item => item.item.Rule.RuleId));
                continue;
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType is
                         "title-evidence" or "positive-evidence"))
            {
                var input = rule.Rule.Scope == "title" ? title ?? "" :
                    rule.Rule.Scope == "posting" ? description : corpus;
                var match = FirstMatch(rule, input,
                    rejectLocalNegation: rule.Rule.RuleType == "positive-evidence",
                    Timeout);
                if (match is not null) matched.Add((rule, NormalizeEvidence(match.Value)));
            }

            foreach (var group in rules.Where(item => item.Rule.RuleType == "required-context")
                         .GroupBy(item => item.Rule.ContextGroupId, StringComparer.Ordinal))
            {
                var groupMatches = group.Select(item =>
                {
                    var input = item.Rule.Scope == "title" ? title ?? "" :
                        item.Rule.Scope == "posting" ? description : corpus;
                    return (Rule: item, Match: FirstMatch(item, input,
                        rejectLocalNegation: false, Timeout));
                }).ToArray();
                Trace.Add("context:" + concept.Id + ":" + group.Key + ":" + string.Join(",", groupMatches.Select(item => item.Rule.Rule.RuleId + "=" + (item.Match is not null))));
                if (groupMatches.All(item => item.Match is not null))
                    matched.AddRange(groupMatches.Select(item => (item.Rule,
                        NormalizeEvidence(item.Match!.Value))));
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType == "remote-designation"))
            {
                if (remoteWork?.IsRemoteDesignated == true)
                    matched.Add((rule, "Remote designation detected in the posting"));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == "remote-signal"))
            {
                var signal = remoteWork?.Signals?.FirstOrDefault(item => item.Category == rule.Rule.Pattern);
                if (signal is not null) matched.Add((rule, signal.Evidence));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == "extended-location-signal"))
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
            Count(uniqueRules);
        }

        return new(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((title ?? "") + "\n" + description))),
            current.Fingerprint, DateTimeOffset.UtcNow,
            results.Values.OrderBy(item => item.ConceptId, StringComparer.Ordinal).ToArray(),
            matchedRuleIds, timedOutRuleIds.OrderBy(item => item, StringComparer.Ordinal).ToArray());

        void Count(IEnumerable<string> ids) { } // The oracle has no mutable telemetry/store.
        void Timeout(string id) => timedOutRuleIds.Add(id);
    }

    private CompiledRuleset Compile(SemanticRulesSnapshot snapshot)
    {
        var compiled = snapshot.Rules.Select(rule => new CompiledRule(rule,
            rule.RuleType is "remote-designation" or "remote-signal" or
                "extended-location-signal" ? null : CompilePattern(rule.Pattern))).ToArray();
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

    private sealed record CompiledRule(SemanticRule Rule, Regex? Pattern);
    private sealed record CompiledRuleset(string Fingerprint, IReadOnlyList<CompiledRule> Rules,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledRule>> ByConcept);
    internal static string HtmlToPlainText(string html)
    {
        var withSeparators = BlockTagRegex().Replace(html, " ");
        var withoutTags = AnyTagRegex().Replace(withSeparators, " ");
        return WhitespaceRegex().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    [GeneratedRegex(@"(?is)<br\s*/?>|</?(?:p|div|h[1-6]|li|ul|ol)[^>]*>")]
    private static partial Regex BlockTagRegex();
    [GeneratedRegex(@"(?is)<[^>]+>")]
    private static partial Regex AnyTagRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
