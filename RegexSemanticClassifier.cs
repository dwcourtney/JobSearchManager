using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed class RegexSemanticClassifier
{
    private const RegexOptions BaseOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex WhitespacePattern = new(@"\s+", BaseOptions,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex LocalNegationPattern = new(
        @"(?:\b(?:do|does|did|is|are|was|were|will|would|should|can|cannot)\s+not|\bnot\s+(?:directly\s+)?(?:responsible\s+for\s+)?|\bno\s+(?:direct\s+)?)$",
        BaseOptions, TimeSpan.FromMilliseconds(100));
    private readonly SqliteSemanticRuleStore _store;
    private readonly JobConceptCatalog _catalog;
    private readonly ConcurrentDictionary<string, long> _pendingUsage = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private CompiledRuleset? _current;

    public RegexSemanticClassifier(SqliteSemanticRuleStore store, JobConceptCatalog catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    public string RulesetFingerprint => Volatile.Read(ref _current)?.Fingerprint
        ?? throw new InvalidOperationException("Semantic RegEx rules have not been loaded.");

    public int ActiveRuleCount => Volatile.Read(ref _current)?.Rules.Count ?? 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await ReloadAsync(cancellationToken);

    public async Task<string> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _store.LoadRuntimeSnapshotAsync(cancellationToken);
            var compiled = Compile(snapshot);
            Interlocked.Exchange(ref _current, compiled);
            return compiled.Fingerprint;
        }
        finally { _reloadGate.Release(); }
    }

    public RegexClassification Classify(string title, string descriptionHtml,
        RemoteWorkAnalysis? remoteWork, ExtendedLocationRequirementAnalysis? extendedLocation,
        bool productionUsage)
    {
        var current = Volatile.Read(ref _current)
            ?? throw new InvalidOperationException("Semantic RegEx rules have not been loaded.");
        var description = string.IsNullOrWhiteSpace(descriptionHtml)
            ? "" : JobAnalysis.HtmlToPlainText(descriptionHtml);
        var corpus = string.Join('\n', [title ?? "", description]);
        var results = new Dictionary<string, DetectedJobConcept>(StringComparer.Ordinal);
        var matchedRuleIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var concept in _catalog.Concepts)
        {
            if (!current.ByConcept.TryGetValue(concept.Id, out var rules)) continue;
            var matched = new List<(CompiledRule Rule, string Evidence)>();
            var excluded = rules.Where(item => item.Rule.RuleType == SemanticRuleTypes.Exclusion)
                .Select(item => (item, Match: item.Pattern!.Match(title ?? "")))
                .Where(item => item.Match.Success)
                .ToArray();
            if (excluded.Length > 0)
            {
                Count(excluded.Select(item => item.item.Rule.RuleId));
                continue;
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType is
                         SemanticRuleTypes.TitleEvidence or SemanticRuleTypes.PositiveEvidence))
            {
                var input = rule.Rule.Scope == SemanticRuleScopes.Title ? title ?? "" :
                    rule.Rule.Scope == SemanticRuleScopes.Posting ? description : corpus;
                var match = rule.Pattern!.Matches(input).Cast<Match>().FirstOrDefault(candidate =>
                    rule.Rule.RuleType != SemanticRuleTypes.PositiveEvidence ||
                    !IsLocallyNegated(input, candidate));
                if (match is not null) matched.Add((rule, NormalizeEvidence(match.Value)));
            }

            foreach (var group in rules.Where(item => item.Rule.RuleType == SemanticRuleTypes.RequiredContext)
                         .GroupBy(item => item.Rule.ContextGroupId, StringComparer.Ordinal))
            {
                var groupMatches = group.Select(item =>
                {
                    var input = item.Rule.Scope == SemanticRuleScopes.Title ? title ?? "" :
                        item.Rule.Scope == SemanticRuleScopes.Posting ? description : corpus;
                    return (Rule: item, Match: item.Pattern!.Match(input));
                }).ToArray();
                if (groupMatches.All(item => item.Match.Success))
                    matched.AddRange(groupMatches.Select(item => (item.Rule,
                        NormalizeEvidence(item.Match.Value))));
            }

            foreach (var rule in rules.Where(item => item.Rule.RuleType == SemanticRuleTypes.RemoteDesignation))
            {
                if (remoteWork?.IsRemoteDesignated == true)
                    matched.Add((rule, "Remote designation detected in the posting"));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == SemanticRuleTypes.RemoteSignal))
            {
                var signal = remoteWork?.Signals?.FirstOrDefault(item => item.Category == rule.Rule.Pattern);
                if (signal is not null) matched.Add((rule, signal.Evidence));
            }
            foreach (var rule in rules.Where(item => item.Rule.RuleType == SemanticRuleTypes.ExtendedLocationSignal))
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

        return new(SemanticRulesetFingerprint.PostingContentHash(title ?? "", description),
            current.Fingerprint, DateTimeOffset.UtcNow,
            results.Values.OrderBy(item => item.ConceptId, StringComparer.Ordinal).ToArray(),
            matchedRuleIds);

        void Count(IEnumerable<string> ids)
        {
            if (!productionUsage) return;
            foreach (var id in ids.Distinct(StringComparer.Ordinal))
                _pendingUsage.AddOrUpdate(id, 1, (_, currentValue) => currentValue + 1);
        }
    }

    public async Task FlushUsageAsync(CancellationToken cancellationToken = default)
    {
        var batch = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in _pendingUsage)
            if (_pendingUsage.TryRemove(item.Key, out var count)) batch[item.Key] = count;
        if (batch.Count == 0) return;
        try { await _store.ApplyUsageAsync(batch, DateTimeOffset.UtcNow, cancellationToken); }
        catch
        {
            foreach (var item in batch)
                _pendingUsage.AddOrUpdate(item.Key, item.Value, (_, current) => current + item.Value);
            throw;
        }
    }

    private CompiledRuleset Compile(SemanticRulesSnapshot snapshot)
    {
        var compiled = snapshot.Rules.Select(rule => new CompiledRule(rule,
            rule.RuleType is SemanticRuleTypes.RemoteDesignation or SemanticRuleTypes.RemoteSignal or
                SemanticRuleTypes.ExtendedLocationSignal ? null : CompilePattern(rule.Pattern))).ToArray();
        return new(snapshot.Fingerprint, compiled,
            compiled.GroupBy(item => item.Rule.ConceptId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<CompiledRule>)group.ToArray(),
                    StringComparer.Ordinal));
    }

    private Regex CompilePattern(string pattern)
    {
        var timeout = TimeSpan.FromMilliseconds(_store.Policy.RegexTimeoutMilliseconds);
        try { return new(pattern, BaseOptions | RegexOptions.NonBacktracking, timeout); }
        catch (NotSupportedException) { return new(pattern, BaseOptions, timeout); }
    }

    private static bool IsLocallyNegated(string corpusText, Match match)
    {
        var start = Math.Max(0, match.Index - 60);
        return LocalNegationPattern.IsMatch(corpusText[start..match.Index].TrimEnd());
    }

    private static string NormalizeEvidence(string value)
    {
        var normalized = WhitespacePattern.Replace(value ?? "", " ").Trim(' ', '.', ';', '\u2022');
        return normalized.Length <= 300 ? normalized : normalized[..297] + "...";
    }

    private sealed record CompiledRule(SemanticRule Rule, Regex? Pattern);
    private sealed record CompiledRuleset(string Fingerprint, IReadOnlyList<CompiledRule> Rules,
        IReadOnlyDictionary<string, IReadOnlyList<CompiledRule>> ByConcept);
}

public sealed class RegexTelemetryFlushService(
    RegexSemanticClassifier classifier,
    SqliteSemanticRuleStore store,
    ILogger<RegexTelemetryFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(store.Policy.TelemetryFlushSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await classifier.FlushUsageAsync(stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(exception, "Semantic rule usage telemetry flush failed; counters remain buffered."); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await classifier.FlushUsageAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
