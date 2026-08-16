namespace JobSearchManager;

public sealed class JobCatalog
{
    private readonly JobSourceClient _jobSourceClient;
    private readonly AppStateStore _stateStore;
    private readonly ILogger<JobCatalog> _logger;
    private readonly CredentialDetector _credentialDetector;
    private readonly AcademicQualificationDetector _academicQualificationDetector;
    private readonly WorkAuthorizationDetector _workAuthorizationDetector;
    private readonly RemoteWorkDetector _remoteWorkDetector;
    private readonly CompanyCatalog _companyCatalog;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly SemaphoreSlim _sourceOperationGate = new(1, 1);
    private JobsSnapshot _snapshot = JobsSnapshot.Empty;
    private JobHistoryDocument _history = JobHistoryDocument.Empty with
    {
        Jobs = new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal)
    };
    private Task<JobsSnapshot>? _activeRefresh;
    private JobSourceQuery _currentQuery = JobsSnapshot.Empty.Query;
    private IReadOnlyList<JobRecord> _cachedJobs = [];

    public JobCatalog(
        JobSourceClient jobSourceClient,
        AppStateStore stateStore,
        ILogger<JobCatalog> logger,
        CredentialDetector credentialDetector,
        AcademicQualificationDetector academicQualificationDetector,
        WorkAuthorizationDetector workAuthorizationDetector,
        RemoteWorkDetector remoteWorkDetector,
        CompanyCatalog companyCatalog)
    {
        _jobSourceClient = jobSourceClient;
        _stateStore = stateStore;
        _logger = logger;
        _credentialDetector = credentialDetector;
        _academicQualificationDetector = academicQualificationDetector;
        _workAuthorizationDetector = workAuthorizationDetector;
        _remoteWorkDetector = remoteWorkDetector;
        _companyCatalog = companyCatalog;
    }

    public JobsSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public async Task InitializeAsync(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        var history = await _stateStore.LoadJobHistoryAsync();
        var cache = await _stateStore.LoadJobsCacheAsync(company.Id);
        _history = history;
        _currentQuery = query;

        if (cache?.Jobs is not { Count: > 0 })
        {
            lock (_gate)
            {
                _snapshot = JobsSnapshot.Empty with { Query = query };
            }
            return;
        }

        var cachedQuery = cache.Query is not null &&
            _companyCatalog.TryGet(cache.Query.CompanyId, out var cachedCompany)
                ? cache.Query.Normalize(cachedCompany)
                : null;
        if (cachedQuery is null || !cachedQuery.IsEquivalentTo(query, _companyCatalog) ||
            cache.Jobs.Any(job => !string.Equals(
                job.CompanyId, query.CompanyId, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation(
                "Ignoring {CachePath} because its job-source query does not match the selected country/location.",
                _stateStore.JobsCachePath);
            lock (_gate)
            {
                _snapshot = JobsSnapshot.Empty with { Query = query };
            }
            return;
        }

        var cacheNeedsAnalysisUpgrade = cache.Jobs.Any(job =>
            !string.IsNullOrWhiteSpace(job.DescriptionHtml) && !_jobSourceClient.IsAnalysisCurrent(job));
        var cacheNeedsQueryUpgrade = cache.Query is not null &&
            (!string.IsNullOrWhiteSpace(cache.Query.LocationLabel) || cache.Query.SourceModelVersion < 2);
        var cachedJobs = cache.Jobs.Select(job =>
            !string.IsNullOrWhiteSpace(job.DescriptionHtml) && !_jobSourceClient.IsAnalysisCurrent(job)
                ? _jobSourceClient.Reclassify(job)
                : job).ToArray();

        if (cacheNeedsAnalysisUpgrade || cacheNeedsQueryUpgrade)
        {
            await _stateStore.SaveJobsCacheAsync(
                cachedJobs,
                cache.LastRefreshedUtc ?? cache.SavedAtUtc,
                cache.DetailFailureCount,
                query);
            _logger.LogInformation(
                "Updated cached query/derived analysis (credential catalog {CredentialCatalogVersion}, academic analysis {AcademicAnalysisVersion}, work-authorization analysis {WorkAuthorizationAnalysisVersion}, remote-work analysis {RemoteWorkAnalysisVersion}).",
                _credentialDetector.CatalogVersion,
                _academicQualificationDetector.AnalysisVersion,
                WorkAuthorizationDetector.CurrentAnalysisVersion,
                RemoteWorkDetector.CurrentAnalysisVersion);
        }

        var historyChanged = ReconcileHistory(
            cachedJobs,
            cache.LastRefreshedUtc ?? cache.SavedAtUtc,
            updateKnownLastSeen: false);
        if (historyChanged)
        {
            await _stateStore.SaveJobHistoryAsync(CloneHistory());
        }

        lock (_gate)
        {
            _cachedJobs = cachedJobs;
            var availableJobs = cachedJobs.Where(job => job.IsSourceAvailable).ToArray();
            _snapshot = new JobsSnapshot(
                availableJobs,
                availableJobs.Length,
                cache.LastRefreshedUtc,
                false,
                null,
                cache.DetailFailureCount,
                true,
                GetNewJobIds(availableJobs),
                query,
                GetJobStates(availableJobs),
                null);
        }

        _logger.LogInformation(
            "Loaded {JobCount} jobs from {CachePath}.",
            cachedJobs.Length,
            _stateStore.JobsCachePath);
    }

    public Task<JobsSnapshot> RefreshAsync()
        => RefreshAsync(_currentQuery, automaticCheck: false, CancellationToken.None);

    public Task<JobsSnapshot> RefreshAsync(CancellationToken cancellationToken)
        => RefreshAsync(_currentQuery, automaticCheck: false, cancellationToken);

    public Task<JobsSnapshot> RefreshAsync(JobSourceQuery query)
        => RefreshAsync(query, automaticCheck: false, CancellationToken.None);

    public Task<JobsSnapshot> RefreshAsync(
        JobSourceQuery query,
        CancellationToken cancellationToken)
        => RefreshAsync(query, automaticCheck: false, cancellationToken);

    private Task<JobsSnapshot> RefreshAsync(
        JobSourceQuery query,
        bool automaticCheck,
        CancellationToken cancellationToken)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        lock (_gate)
        {
            if (_activeRefresh is { IsCompleted: false })
            {
                if (_currentQuery.IsEquivalentTo(query, _companyCatalog))
                {
                    return _activeRefresh;
                }

                throw new InvalidOperationException(
                    "A job-source refresh is already running. Wait for it to finish before applying another location.");
            }

            var previousQuery = _currentQuery;
            var previousSnapshot = _snapshot;
            var queryChanged = !previousQuery.IsEquivalentTo(query, _companyCatalog);
            _currentQuery = query;
            _snapshot = queryChanged
                ? JobsSnapshot.Empty with
                {
                    IsRefreshing = true,
                    Query = query,
                    RefreshProgress = new RefreshProgress("listings", 0, null)
                }
                : _snapshot with
                {
                    IsRefreshing = true,
                    Error = null,
                    Query = query,
                    RefreshProgress = new RefreshProgress("listings", 0, null)
                };
            _activeRefresh = RefreshCoreAsync(
                query, previousQuery, previousSnapshot, automaticCheck, cancellationToken);
            return _activeRefresh;
        }
    }

    public async Task<AutomaticCheckResult> CheckForUnknownJobsAsync(
        CancellationToken cancellationToken = default)
    {
        HashSet<string> knownIds;
        JobSourceQuery query;
        lock (_gate)
        {
            if (_snapshot.IsRefreshing)
            {
                return new AutomaticCheckResult(false, true, 0, [], false);
            }
            query = _currentQuery;
            knownIds = _history.Jobs.Keys.ToHashSet(StringComparer.Ordinal);
        }

        Task<JobsSnapshot> refresh;
        try
        {
            refresh = RefreshAsync(query, automaticCheck: true, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new AutomaticCheckResult(false, true, 0, [], false);
        }

        var refreshed = await refresh.WaitAsync(cancellationToken);
        var unknownStableIds = refreshed.Jobs
            .Select(job => job.StableId)
            .Where(id => !knownIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new AutomaticCheckResult(
            true,
            false,
            refreshed.Metrics?.ListingsFetched ?? refreshed.TotalJobs,
            unknownStableIds,
            refreshed.Error is null && unknownStableIds.Length > 0);
    }

    public async Task<JobRecord?> GetJobDetailAsync(
        string stableId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stableId)) return null;

        await _sourceOperationGate.WaitAsync(cancellationToken);
        try
        {
            JobRecord? job;
            JobSourceQuery query;
            lock (_gate)
            {
                job = _cachedJobs.FirstOrDefault(item =>
                    string.Equals(item.StableId, stableId, StringComparison.Ordinal));
                query = _currentQuery;
            }
            if (job is null) return null;

            var updated = job;
            if (!string.IsNullOrWhiteSpace(job.DescriptionHtml))
            {
                if (!_jobSourceClient.IsAnalysisCurrent(job))
                {
                    updated = _jobSourceClient.Reclassify(job);
                }
            }
            else
            {
                updated = await _jobSourceClient.FetchJobDetailAsync(
                    _companyCatalog.Get(job.CompanyId), job, cancellationToken);
            }

            IReadOnlyList<JobRecord> cache;
            lock (_gate)
            {
                _cachedJobs = _cachedJobs
                    .Select(item => string.Equals(item.StableId, stableId, StringComparison.Ordinal)
                        ? updated
                        : item)
                    .ToArray();
                var available = _cachedJobs.Where(item => item.IsSourceAvailable).ToArray();
                _snapshot = _snapshot with
                {
                    Jobs = available,
                    TotalJobs = available.Length,
                    JobStates = GetJobStates(available)
                };
                cache = _cachedJobs;
            }
            await _stateStore.SaveJobsCacheAsync(
                cache,
                _snapshot.LastRefreshedUtc ?? DateTimeOffset.UtcNow,
                _snapshot.DetailFailureCount,
                query);
            return updated;
        }
        finally
        {
            _sourceOperationGate.Release();
        }
    }

    public IReadOnlyDictionary<string, bool> GetDescriptionMatches(DescriptionMatchRequest request)
    {
        static string[] Terms(IReadOnlyList<string>? terms) => (terms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var inclusions = Terms(request.IncludeKeywords);
        var exclusions = Terms(request.ExcludeKeywords);
        JobRecord[] jobs;
        lock (_gate)
        {
            jobs = _snapshot.Jobs.ToArray();
        }

        return jobs.ToDictionary(job => job.StableId, job =>
        {
            var metadata = string.Join('\n',
                job.Title,
                job.RequisitionId,
                job.PrimaryLocation,
                string.Join('\n', job.AdditionalLocations)).ToLowerInvariant();
            var hasDetail = !string.IsNullOrWhiteSpace(job.DescriptionHtml);
            var description = hasDetail
                ? JobAnalysis.HtmlToPlainText(job.DescriptionHtml).ToLowerInvariant()
                : "";
            var inclusionMatch = inclusions.Length == 0 ||
                inclusions.Any(term => metadata.Contains(term, StringComparison.Ordinal) ||
                    description.Contains(term, StringComparison.Ordinal));
            if (!inclusionMatch && !hasDetail)
            {
                inclusionMatch = true; // Unknown remains visible until it is hydrated.
            }
            var exclusionMatch = exclusions.Any(term =>
                metadata.Contains(term, StringComparison.Ordinal) ||
                description.Contains(term, StringComparison.Ordinal));
            return inclusionMatch && !exclusionMatch;
        }, StringComparer.Ordinal);
    }

    public async Task<bool> MarkViewedAsync(string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        await _historyGate.WaitAsync();
        try
        {
            JobRecord? currentJob;
            lock (_gate)
            {
                currentJob = _snapshot.Jobs.FirstOrDefault(
                    job => string.Equals(job.StableId, stableId, StringComparison.Ordinal));
            }

            if (currentJob is null)
            {
                return false;
            }

            if (!_history.Jobs.TryGetValue(stableId, out var entry))
            {
                var now = DateTimeOffset.UtcNow;
                entry = new JobHistoryEntry(
                    currentJob.RequisitionId,
                    currentJob.ExternalPath,
                    now,
                    now,
                    false,
                    CompanyId: currentJob.CompanyId);
            }

            if (entry.HasBeenViewed)
            {
                return true;
            }

            _history.Jobs[stableId] = entry with { HasBeenViewed = true };
            await _stateStore.SaveJobHistoryAsync(CloneHistory());

            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    NewJobIds = _snapshot.NewJobIds
                        .Where(id => !string.Equals(id, stableId, StringComparison.Ordinal))
                        .ToArray()
                };
            }

            return true;
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task<bool> SetWorkflowStateAsync(string stableId, string workflowState)
    {
        if (string.IsNullOrWhiteSpace(stableId) || !JobWorkflowStates.IsValid(workflowState))
        {
            return false;
        }

        await _historyGate.WaitAsync();
        try
        {
            JobRecord? currentJob;
            lock (_gate)
            {
                currentJob = _snapshot.Jobs.FirstOrDefault(
                    job => string.Equals(job.StableId, stableId, StringComparison.Ordinal));
            }

            if (currentJob is null)
            {
                return false;
            }

            if (!_history.Jobs.TryGetValue(stableId, out var entry))
            {
                var now = DateTimeOffset.UtcNow;
                entry = new JobHistoryEntry(
                    currentJob.RequisitionId,
                    currentJob.ExternalPath,
                    now,
                    now,
                    false,
                    CompanyId: currentJob.CompanyId);
            }

            if (!JobWorkflowStates.CanTransition(entry.WorkflowState, workflowState))
            {
                return false;
            }

            if (string.Equals(entry.WorkflowState, workflowState, StringComparison.Ordinal))
            {
                return true;
            }

            _history.Jobs[stableId] = entry with
            {
                WorkflowState = workflowState,
                WorkflowStateChangedAt = DateTimeOffset.UtcNow,
                Dismissed = false,
                DismissedAt = null,
                Saved = false,
                SavedAt = null,
                Applied = false,
                AppliedAt = null
            };
            await _stateStore.SaveJobHistoryAsync(CloneHistory());

            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    JobStates = GetJobStates(_snapshot.Jobs)
                };
            }

            return true;
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task InitializeWithoutSourceAsync()
    {
        _history = await _stateStore.LoadJobHistoryAsync();
        lock (_gate)
        {
            _snapshot = JobsSnapshot.Empty;
        }
    }

    public async Task ReloadHistoryAsync()
    {
        await _historyGate.WaitAsync();
        try
        {
            _history = await _stateStore.LoadJobHistoryAsync();
            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    NewJobIds = GetNewJobIds(_snapshot.Jobs),
                    JobStates = GetJobStates(_snapshot.Jobs)
                };
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task<JobsSnapshot> RefreshCoreAsync(
        JobSourceQuery query,
        JobSourceQuery previousQuery,
        JobsSnapshot previousSnapshot,
        bool automaticCheck,
        CancellationToken cancellationToken)
    {
        await _sourceOperationGate.WaitAsync(cancellationToken);
        try
        {
            var company = _companyCatalog.Get(query.CompanyId);
            _logger.LogInformation(
                "Refreshing the {Company} job snapshot for country {Country} and locations {Locations}.",
                company.DisplayName,
                query.CountryLabel,
                DescribeLocations(query));
            var filterSettings = await _stateStore.LoadSettingsAsync();
            var result = await _jobSourceClient.FetchAllJobsAsync(
                company,
                query,
                progress => ReportRefreshProgress(query, progress),
                cancellationToken,
                cachedJobs: await CachedJobsForQueryAsync(query),
                automaticCheck: automaticCheck,
                filterSettings: filterSettings);
            var refreshedAt = DateTimeOffset.UtcNow;

            ReportRefreshProgress(query, new RefreshProgress("saving", 0, null));

            await _historyGate.WaitAsync();
            try
            {
                ReconcileHistory(
                    result.Jobs.Where(job => job.IsSourceAvailable).ToArray(),
                    refreshedAt,
                    updateKnownLastSeen: true);
                await _stateStore.SaveJobHistoryAsync(CloneHistory());
            }
            finally
            {
                _historyGate.Release();
            }

            await _stateStore.SaveJobsCacheAsync(
                result.Jobs,
                refreshedAt,
                result.DetailFailureCount,
                query);

            var availableJobs = result.Jobs.Where(job => job.IsSourceAvailable).ToArray();
            var refreshed = new JobsSnapshot(
                availableJobs,
                availableJobs.Length,
                refreshedAt,
                false,
                null,
                result.DetailFailureCount,
                false,
                GetNewJobIds(availableJobs),
                query,
                GetJobStates(availableJobs),
                null,
                result.Metrics);

            lock (_gate)
            {
                _cachedJobs = result.Jobs;
                _snapshot = refreshed;
            }

            _logger.LogInformation(
                "Refresh completed with {JobCount} jobs and {DetailFailureCount} detail failures.",
                result.Jobs.Count,
                result.DetailFailureCount);
            return refreshed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                _currentQuery = previousQuery;
                _snapshot = previousSnapshot with
                {
                    IsRefreshing = false,
                    RefreshProgress = null
                };
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not refresh the {Company} job snapshot.",
                _companyCatalog.Get(query.CompanyId).DisplayName);
            lock (_gate)
            {
                _currentQuery = previousQuery;
                _snapshot = previousSnapshot with
                {
                    IsRefreshing = false,
                    Error = ex.Message,
                    RefreshProgress = null
                };
                return _snapshot;
            }
        }
        finally
        {
            _sourceOperationGate.Release();
        }
    }

    private async Task<IReadOnlyList<JobRecord>> CachedJobsForQueryAsync(JobSourceQuery query)
    {
        lock (_gate)
        {
            if (_snapshot.Query.IsEquivalentTo(query, _companyCatalog) && _cachedJobs.Count > 0)
            {
                return _cachedJobs;
            }
        }

        var cache = await _stateStore.LoadJobsCacheAsync(query.CompanyId);
        return cache?.Query?.IsEquivalentTo(query, _companyCatalog) == true
            ? cache.Jobs
            : [];
    }

    private void ReportRefreshProgress(JobSourceQuery query, RefreshProgress progress)
    {
        lock (_gate)
        {
            if (!_snapshot.IsRefreshing ||
                !_snapshot.Query.IsEquivalentTo(query, _companyCatalog))
            {
                return;
            }

            var current = _snapshot.RefreshProgress;
            if (current is not null &&
                string.Equals(current.Phase, progress.Phase, StringComparison.Ordinal) &&
                progress.Completed < current.Completed)
            {
                return;
            }

            _snapshot = _snapshot with { RefreshProgress = progress };
        }
    }

    private string DescribeLocations(JobSourceQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        if (query.IncludeAllLocations)
        {
            return FacetDefaults.AllLocationsLabel;
        }

        var locations = (query.PhysicalLocations ?? [])
            .Select(location => location.Label)
            .ToList();
        if (query.IncludeRemote)
        {
            locations.Insert(0, "Remote/Teleworker");
        }
        return locations.Count == 0 ? "none" : string.Join(", ", locations);
    }

    private bool ReconcileHistory(
        IReadOnlyList<JobRecord> jobs,
        DateTimeOffset seenAt,
        bool updateKnownLastSeen)
    {
        var changed = false;
        foreach (var job in jobs)
        {
            var stableId = job.StableId;
            if (!_history.Jobs.ContainsKey(stableId) &&
                !string.IsNullOrWhiteSpace(job.ExternalPath))
            {
                var fallback = _history.Jobs.FirstOrDefault(pair =>
                    string.Equals(
                        pair.Value.ExternalPath,
                        job.ExternalPath,
                        StringComparison.Ordinal) &&
                    string.Equals(pair.Value.CompanyId, job.CompanyId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(fallback.Key))
                {
                    _history.Jobs.Remove(fallback.Key);
                    _history.Jobs[stableId] = fallback.Value with
                    {
                        JobReqId = job.RequisitionId,
                        ExternalPath = job.ExternalPath,
                        CompanyId = job.CompanyId
                    };
                    changed = true;
                }
            }

            if (!_history.Jobs.TryGetValue(stableId, out var existing))
            {
                _history.Jobs[stableId] = new JobHistoryEntry(
                    job.RequisitionId,
                    job.ExternalPath,
                    seenAt,
                    seenAt,
                    false,
                    CompanyId: job.CompanyId);
                changed = true;
            }
            else if (updateKnownLastSeen)
            {
                _history.Jobs[stableId] = existing with
                {
                    JobReqId = string.IsNullOrWhiteSpace(existing.JobReqId)
                        ? job.RequisitionId
                        : existing.JobReqId,
                    ExternalPath = string.IsNullOrWhiteSpace(existing.ExternalPath)
                        ? job.ExternalPath
                        : existing.ExternalPath,
                    LastSeenAt = seenAt
                };
                changed = true;
            }
        }

        return changed;
    }

    private string[] GetNewJobIds(IReadOnlyList<JobRecord> jobs) => jobs
        .Select(job => job.StableId)
        .Where(id => _history.Jobs.TryGetValue(id, out var entry) && !entry.HasBeenViewed)
        .ToArray();

    private Dictionary<string, string> GetJobStates(IReadOnlyList<JobRecord> jobs) => jobs
        .ToDictionary(
            job => job.StableId,
            job => _history.Jobs.TryGetValue(job.StableId, out var entry)
                ? JobWorkflowStates.Normalize(entry.WorkflowState)
                : JobWorkflowStates.Normal,
            StringComparer.Ordinal);

    private JobHistoryDocument CloneHistory() => _history with
    {
        Jobs = _history.Jobs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal)
    };
}
