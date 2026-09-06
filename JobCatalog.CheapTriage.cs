namespace JobSearchManager;

public sealed partial class JobCatalog
{
    private Task? _cheapTriageTask;
    private bool _cheapTriageRunning;
    private bool _cheapTriageRequested;
    private DateTimeOffset? _cheapTriageReconciledUtc;
    private string? _cheapTriageError;

    private void ScheduleCheapTriage()
    {
        if (_cheapTriage?.Enabled != true) return;
        lock (_gate)
        {
            if (!_cachedJobs.Any(job => !_cheapTriage.IsCurrent(job))) return;
            _cheapTriageRequested = true;
            if (_cheapTriageRunning) return;
            _cheapTriageRunning = true;
            _cheapTriageTask = Task.Run(RunCheapTriageAsync);
        }
    }

    // Tests and explicit diagnostic reconciliation can wait without adding latency to Jobs reads.
    internal Task WaitForCheapTriageAsync()
    {
        lock (_gate) return _cheapTriageTask ?? Task.CompletedTask;
    }

    private async Task RunCheapTriageAsync()
    {
        try
        {
            while (true)
            {
                lock (_gate) _cheapTriageRequested = false;
                await ReconcileCheapTriageAsync();
                lock (_gate)
                {
                    _cheapTriageError = null;
                    _cheapTriageReconciledUtc = DateTimeOffset.UtcNow;
                    if (_cheapTriageRequested) continue;
                    _cheapTriageRunning = false;
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _cheapTriageError = "Shadow recording failed. Normal Jobs processing is unaffected; reopen the source or refresh observations to retry.";
                _cheapTriageRunning = false;
            }
            _logger.LogError(exception, "Cheap Triage observation failed; no production behavior was gated.");
        }
    }

    private async Task ReconcileCheapTriageAsync()
    {
        JobSourceQuery query;
        JobRecord[] candidates;
        lock (_gate)
        {
            query = _currentQuery;
            candidates = _cachedJobs.Where(job => !_cheapTriage!.IsCurrent(job)).ToArray();
        }
        if (candidates.Length == 0) return;
        // Matching runs outside the catalog/source locks. The commit rechecks current input.
        var observations = candidates.ToDictionary(job => job.StableId,
            job => _cheapTriage!.Observe(job), StringComparer.Ordinal);
        await _sourceOperationGate.WaitAsync();
        try
        {
            var sourceKey = $"{query.CompanyId}:{_stateStore.QueryFingerprint(query)}";
            await using var lease = await _sharedSourceRefreshCoordinator.AcquireAsync(sourceKey);
            var document = await _stateStore.LoadJobsCacheAsync(query);
            if (document?.Query?.IsEquivalentTo(query, _companyCatalog) != true) return;
            var changed = false;
            var jobs = document.Jobs.Select(job =>
            {
                if (_cheapTriage!.IsCurrent(job) || !observations.TryGetValue(job.StableId, out var observation) ||
                    CheapTriageShadow.InputFingerprint(job) != observation.Result.PostingFingerprint) return job;
                changed = true;
                return job with { CheapTriage = observation };
            }).ToArray();
            // Merge only the observation into the latest shared record: never overwrite Job Fit/history.
            if (changed) await _stateStore.SaveJobsCacheAsync(jobs,
                document.LastRefreshedUtc ?? document.SavedAtUtc, document.DetailFailureCount, query);
            lock (_gate)
            {
                if (!_currentQuery.IsEquivalentTo(query, _companyCatalog)) return;
                var byId = jobs.ToDictionary(job => job.StableId, StringComparer.Ordinal);
                _cachedJobs = _cachedJobs.Select(job => byId.TryGetValue(job.StableId, out var stored) &&
                    CheapTriageShadow.InputFingerprint(job) == CheapTriageShadow.InputFingerprint(stored)
                        ? job with { CheapTriage = stored.CheapTriage } : job).ToArray();
                var visible = VisibleJobs(_cachedJobs);
                _snapshot = _snapshot with { Jobs = visible, TotalJobs = visible.Length };
                // Normal refresh/detail scheduling requests another pass when local content changes.
                // Other-workspace content is adopted by normal list hydration, not overwritten here.
            }
        }
        finally { _sourceOperationGate.Release(); }
    }

    public CheapTriageDiagnostic? GetCheapTriageDiagnostic(string stableId)
    {
        ScheduleCheapTriage();
        lock (_gate)
        {
            var job = _cachedJobs.FirstOrDefault(job => job.StableId == stableId);
            return job is null ? null : new(_cheapTriage?.Mode ?? "Off", _cheapTriage?.Rules.Version ?? "",
                _cheapTriage?.Rules.Fingerprint ?? "", _cheapTriage?.IsCurrent(job) == true, job.CheapTriage);
        }
    }

    public CheapTriageLiveReport GetCheapTriageReport()
    {
        ScheduleCheapTriage();
        lock (_gate)
        {
            var current = _cachedJobs.Where(job => _cheapTriage?.IsCurrent(job) == true).ToArray();
            var rejected = _cachedJobs.Where(job => job.CheapTriage?.Decision == "REJECT")
                .OrderBy(job => job.Title, StringComparer.Ordinal).ThenBy(job => job.StableId, StringComparer.Ordinal)
                .Select(job => new CheapTriageReviewJob(_companyCatalog.Get(job.CompanyId).DisplayName,
                    JobListItem.FromJob(job, _semanticClassification?.IsCurrent(job) ?? true),
                    _history.Jobs.TryGetValue(job.StableId, out var entry)
                        ? JobWorkflowStates.Normalize(entry.WorkflowState) : JobWorkflowStates.Normal,
                    job.IsSourceAvailable, _cheapTriage?.IsCurrent(job) == true, job.CheapTriage!)).ToArray();
            var rejectCount = current.Count(job => job.CheapTriage!.Decision == "REJECT");
            return new(_cheapTriage?.Mode ?? "Off", "Current workspace selected-source cache, including retained/hidden jobs; not global traffic",
                _currentQuery.CompanyId, _cheapTriage?.Rules.Version ?? "", _cheapTriage?.Rules.Fingerprint ?? "",
                _cachedJobs.Count, current.Length, current.Count(job => job.CheapTriage!.Decision == "KEEP"), rejectCount,
                current.Count(job => job.CheapTriage!.Decision == "UNDETERMINED"),
                current.Count(job => !job.CheapTriage!.DescriptionAvailable), current.Count(job => job.CheapTriage!.DescriptionAvailable),
                _cachedJobs.Count(job => job.CheapTriage is { } o &&
                    (o.Result.RulesetFingerprint != _cheapTriage?.Rules.Fingerprint || o.Result.RulesetVersion != _cheapTriage?.Rules.Version)),
                _cachedJobs.Count - current.Length, current.Length == 0 ? 0 : 100.0 * rejectCount / current.Length,
                _cheapTriageRunning, _cachedJobs.Select(job => job.CheapTriage?.AnalyzedAtUtc).Max(),
                _cheapTriageReconciledUtc, _cheapTriageError, rejected);
        }
    }

    private static IReadOnlyList<JobRecord> PreserveCheapTriage(IReadOnlyList<JobRecord> previous,
        IReadOnlyList<JobRecord> incoming)
    {
        var observations = previous.ToDictionary(job => job.StableId, job => job.CheapTriage, StringComparer.Ordinal);
        return incoming.Select(job => job with
        {
            CheapTriage = job.CheapTriage ?? observations.GetValueOrDefault(job.StableId)
        }).ToArray();
    }
}
