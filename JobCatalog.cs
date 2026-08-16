namespace WorkdayJobManager;

public sealed class JobCatalog
{
    private readonly WorkdayClient _workdayClient;
    private readonly AppStateStore _stateStore;
    private readonly ILogger<JobCatalog> _logger;
    private readonly CredentialDetector _credentialDetector;
    private readonly AcademicQualificationDetector _academicQualificationDetector;
    private readonly WorkAuthorizationDetector _workAuthorizationDetector;
    private readonly RemoteWorkDetector _remoteWorkDetector;
    private readonly CompanyCatalog _companyCatalog;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private readonly SemaphoreSlim _workdayOperationGate = new(1, 1);
    private JobsSnapshot _snapshot = JobsSnapshot.Empty;
    private JobHistoryDocument _history = JobHistoryDocument.Empty with
    {
        Jobs = new Dictionary<string, JobHistoryEntry>(StringComparer.Ordinal)
    };
    private Task<JobsSnapshot>? _activeRefresh;
    private WorkdayQuery _currentQuery = JobsSnapshot.Empty.Query;

    public JobCatalog(
        WorkdayClient workdayClient,
        AppStateStore stateStore,
        ILogger<JobCatalog> logger,
        CredentialDetector credentialDetector,
        AcademicQualificationDetector academicQualificationDetector,
        WorkAuthorizationDetector workAuthorizationDetector,
        RemoteWorkDetector remoteWorkDetector,
        CompanyCatalog companyCatalog)
    {
        _workdayClient = workdayClient;
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

    public async Task InitializeAsync(WorkdayQuery query)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        var history = await _stateStore.LoadJobHistoryAsync();
        var cache = await _stateStore.LoadJobsCacheAsync();
        _history = history;
        _currentQuery = query;

        if (cache?.Jobs is not { Count: > 0 })
        {
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
                "Ignoring {CachePath} because its Workday query does not match the selected country/location.",
                _stateStore.JobsCachePath);
            lock (_gate)
            {
                _snapshot = JobsSnapshot.Empty with { Query = query };
            }
            return;
        }

        var cacheNeedsCredentialUpgrade = cache.Jobs.Any(job =>
            job.CredentialCatalogVersion != _credentialDetector.CatalogVersion ||
            job.Credentials is null ||
            job.UnrecognizedCredentialMentions is null);
        var cacheNeedsAcademicUpgrade = cache.Jobs.Any(job =>
            job.AcademicQualification is null ||
            job.AcademicQualification.AnalysisVersion != _academicQualificationDetector.AnalysisVersion);
        var cacheNeedsWorkAuthorizationUpgrade = cache.Jobs.Any(job =>
            job.WorkAuthorization is null ||
            job.WorkAuthorization.AnalysisVersion != WorkAuthorizationDetector.CurrentAnalysisVersion);
        var cacheNeedsRemoteWorkUpgrade = cache.Jobs.Any(job =>
            job.RemoteWork is null ||
            job.RemoteWork.AnalysisVersion != RemoteWorkDetector.CurrentAnalysisVersion);
        var cacheNeedsQueryUpgrade = cache.Query is not null &&
            (!string.IsNullOrWhiteSpace(cache.Query.LocationLabel) || cache.Query.SourceModelVersion < 2);
        var cachedJobs = cacheNeedsCredentialUpgrade
            ? cache.Jobs.Select(_credentialDetector.AnalyzeJob).ToArray()
            : cache.Jobs;
        cachedJobs = cacheNeedsAcademicUpgrade
            ? cachedJobs.Select(_academicQualificationDetector.AnalyzeJob).ToArray()
            : cachedJobs;
        cachedJobs = cacheNeedsWorkAuthorizationUpgrade
            ? cachedJobs.Select(_workAuthorizationDetector.AnalyzeJob).ToArray()
            : cachedJobs;
        cachedJobs = cacheNeedsRemoteWorkUpgrade
            ? cachedJobs.Select(_remoteWorkDetector.AnalyzeJob).ToArray()
            : cachedJobs;

        if (cacheNeedsCredentialUpgrade || cacheNeedsAcademicUpgrade ||
            cacheNeedsWorkAuthorizationUpgrade || cacheNeedsRemoteWorkUpgrade || cacheNeedsQueryUpgrade)
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
            _snapshot = new JobsSnapshot(
                cachedJobs,
                cachedJobs.Count,
                cache.LastRefreshedUtc,
                false,
                null,
                cache.DetailFailureCount,
                true,
                GetNewJobIds(cachedJobs),
                query,
                GetJobStates(cachedJobs),
                null);
        }

        _logger.LogInformation(
            "Loaded {JobCount} jobs from {CachePath}.",
            cachedJobs.Count,
            _stateStore.JobsCachePath);
    }

    public Task<JobsSnapshot> RefreshAsync()
        => RefreshAsync(_currentQuery);

    public Task<JobsSnapshot> RefreshAsync(WorkdayQuery query)
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
                    "A Workday refresh is already running. Wait for it to finish before applying another location.");
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
            _activeRefresh = RefreshCoreAsync(query, previousQuery, previousSnapshot);
            return _activeRefresh;
        }
    }

    public async Task<AutomaticCheckResult> CheckForUnknownJobsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_snapshot.IsRefreshing)
            {
                return new AutomaticCheckResult(false, true, 0, [], false);
            }
        }

        if (!await _workdayOperationGate.WaitAsync(0, cancellationToken))
        {
            return new AutomaticCheckResult(false, true, 0, [], false);
        }

        WorkdayQuery query;
        IReadOnlyList<ListingIdentity> identities;
        try
        {
            lock (_gate)
            {
                if (_snapshot.IsRefreshing)
                {
                    return new AutomaticCheckResult(false, true, 0, [], false);
                }
                query = _currentQuery;
            }

            identities = await _workdayClient.FetchListingIdentitiesAsync(
                _companyCatalog.Get(query.CompanyId), query, cancellationToken);
        }
        finally
        {
            _workdayOperationGate.Release();
        }

        string[] unknownStableIds;
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            unknownStableIds = identities
                .Where(identity => !_history.Jobs.ContainsKey(identity.StableId) &&
                    !_history.Jobs.Values.Any(entry =>
                        !string.IsNullOrWhiteSpace(identity.ExternalPath) &&
                        string.Equals(
                            entry.CompanyId,
                            query.CompanyId,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.ExternalPath, identity.ExternalPath, StringComparison.Ordinal)))
                .Select(identity => identity.StableId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        finally
        {
            _historyGate.Release();
        }

        if (unknownStableIds.Length == 0)
        {
            return new AutomaticCheckResult(true, false, identities.Count, [], false);
        }

        var refreshed = await RefreshAsync(query);
        return new AutomaticCheckResult(
            true,
            false,
            identities.Count,
            unknownStableIds,
            refreshed.Error is null);
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

    private async Task<JobsSnapshot> RefreshCoreAsync(
        WorkdayQuery query,
        WorkdayQuery previousQuery,
        JobsSnapshot previousSnapshot)
    {
        await _workdayOperationGate.WaitAsync();
        try
        {
            var company = _companyCatalog.Get(query.CompanyId);
            _logger.LogInformation(
                "Refreshing the {Company} Workday job snapshot for country {Country} and locations {Locations}.",
                company.DisplayName,
                query.CountryLabel,
                DescribeLocations(query));
            var result = await _workdayClient.FetchAllJobsAsync(
                company,
                query,
                progress => ReportRefreshProgress(query, progress));
            var refreshedAt = DateTimeOffset.UtcNow;

            ReportRefreshProgress(query, new RefreshProgress("saving", 0, null));

            await _historyGate.WaitAsync();
            try
            {
                ReconcileHistory(result.Jobs, refreshedAt, updateKnownLastSeen: true);
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

            var refreshed = new JobsSnapshot(
                result.Jobs,
                result.Jobs.Count,
                refreshedAt,
                false,
                null,
                result.DetailFailureCount,
                false,
                GetNewJobIds(result.Jobs),
                query,
                GetJobStates(result.Jobs),
                null);

            lock (_gate)
            {
                _snapshot = refreshed;
            }

            _logger.LogInformation(
                "Refresh completed with {JobCount} jobs and {DetailFailureCount} detail failures.",
                result.Jobs.Count,
                result.DetailFailureCount);
            return refreshed;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not refresh the {Company} Workday job snapshot.",
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
            _workdayOperationGate.Release();
        }
    }

    private void ReportRefreshProgress(WorkdayQuery query, RefreshProgress progress)
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

    private string DescribeLocations(WorkdayQuery query)
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
