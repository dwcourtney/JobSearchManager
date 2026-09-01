using System.Text.Json;

namespace JobSearchManager;

using Microsoft.Extensions.Options;

public sealed record SemanticClassificationBackfillStatus(
    int Total,
    int Current,
    int Pending,
    int Unavailable,
    bool Running);

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
    private readonly JobSourceOptions _options;
    private readonly SharedSourceRefreshCoordinator _sharedSourceRefreshCoordinator;
    private readonly SemanticClassificationService? _semanticClassification;
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
    private Task? _semanticClassificationTask;

    public JobCatalog(
        JobSourceClient jobSourceClient,
        AppStateStore stateStore,
        ILogger<JobCatalog> logger,
        CredentialDetector credentialDetector,
        AcademicQualificationDetector academicQualificationDetector,
        WorkAuthorizationDetector workAuthorizationDetector,
        RemoteWorkDetector remoteWorkDetector,
        CompanyCatalog companyCatalog,
        IOptions<JobSourceOptions> options,
        SharedSourceRefreshCoordinator? sharedSourceRefreshCoordinator = null,
        SemanticClassificationService? semanticClassification = null)
    {
        _jobSourceClient = jobSourceClient;
        _stateStore = stateStore;
        _logger = logger;
        _credentialDetector = credentialDetector;
        _academicQualificationDetector = academicQualificationDetector;
        _workAuthorizationDetector = workAuthorizationDetector;
        _remoteWorkDetector = remoteWorkDetector;
        _companyCatalog = companyCatalog;
        _options = options.Value;
        _sharedSourceRefreshCoordinator = sharedSourceRefreshCoordinator ?? new();
        _semanticClassification = semanticClassification;
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
        var cache = await _stateStore.LoadJobsCacheAsync(query);
        var sourceStatus = await _stateStore.LoadSourceStatusAsync(query);
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
        var cacheNeedsSemanticStatusUpgrade = _semanticClassification is not null && cache.Jobs.Any(job =>
            !string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
            job.SemanticClassificationStatus == SemanticClassificationStates.Complete &&
            !_semanticClassification.IsCurrent(job));
        var cachedJobs = CanonicalizeStableIdentities(cache.Jobs.Select(job =>
            !string.IsNullOrWhiteSpace(job.DescriptionHtml) && !_jobSourceClient.IsAnalysisCurrent(job)
                ? _jobSourceClient.Reclassify(job)
                : job).Select(job =>
                    _semanticClassification is not null &&
                    !string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
                    !_semanticClassification.IsCurrent(job)
                        ? job with { SemanticClassificationStatus = SemanticClassificationStates.Pending }
                        : job).ToArray(), "cache initialization");
        var cacheNeedsIdentityRepair = cachedJobs.Count != cache.Jobs.Count;

        if (cacheNeedsAnalysisUpgrade || cacheNeedsQueryUpgrade || cacheNeedsIdentityRepair ||
            cacheNeedsSemanticStatusUpgrade)
        {
            await _stateStore.SaveJobsCacheAsync(
                cachedJobs,
                cache.LastRefreshedUtc ?? cache.SavedAtUtc,
                cache.DetailFailureCount,
                query);
            _logger.LogInformation(
                "Updated cached query/derived analysis (credential catalog {CredentialCatalogVersion}, academic analysis {AcademicAnalysisVersion}, work-authorization analysis {WorkAuthorizationAnalysisVersion}, remote-work analysis {RemoteWorkAnalysisVersion}, extended-location analysis {ExtendedLocationAnalysisVersion}).",
                _credentialDetector.CatalogVersion,
                _academicQualificationDetector.AnalysisVersion,
                WorkAuthorizationDetector.CurrentAnalysisVersion,
                RemoteWorkDetector.CurrentAnalysisVersion,
                ExtendedLocationRequirementDetector.CurrentAnalysisVersion);
        }

        var effectiveLastRefreshed = sourceStatus?.LastSuccessfulRefreshUtc ??
            cache.LastRefreshedUtc ?? cache.SavedAtUtc;
        var effectiveDetailFailureCount = sourceStatus?.DetailFailureCount ?? cache.DetailFailureCount;
        var historyChanged = ReconcileHistory(
            cachedJobs,
            effectiveLastRefreshed,
            updateKnownLastSeen: false);
        if (historyChanged)
        {
            await _stateStore.SaveJobHistoryAsync(CloneHistory());
        }

        lock (_gate)
        {
            _cachedJobs = cachedJobs;
            var availableJobs = VisibleJobs(cachedJobs);
            _snapshot = new JobsSnapshot(
                availableJobs,
                availableJobs.Length,
                effectiveLastRefreshed,
                false,
                null,
                effectiveDetailFailureCount,
                true,
                GetNewJobIds(availableJobs),
                query,
                GetJobStates(availableJobs),
                GetJobClosures(availableJobs),
                null);
        }

        _logger.LogInformation(
            "Loaded {JobCount} jobs from {CachePath}.",
            cachedJobs.Count,
            _stateStore.JobsCachePath);
        ScheduleSemanticClassification();
    }

    public Task<JobsSnapshot> RefreshAsync()
        => RefreshAsync(_currentQuery, CancellationToken.None);

    public Task<JobsSnapshot> RefreshAsync(CancellationToken cancellationToken)
        => RefreshAsync(_currentQuery, cancellationToken);

    public Task<JobsSnapshot> RefreshAsync(JobSourceQuery query)
        => RefreshAsync(query, CancellationToken.None);

    public Task<JobsSnapshot> RefreshAsync(
        JobSourceQuery query,
        CancellationToken cancellationToken)
        => RefreshCoreEntryAsync(query, cancellationToken);

    public async Task<JobsSnapshot> SwitchSourceAsync(
        JobSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        var company = _companyCatalog.Get(query.CompanyId);
        query = query.Normalize(company);
        await _sourceOperationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (_activeRefresh is { IsCompleted: false })
                {
                    throw new InvalidOperationException(
                        "A job-source refresh is already running. Wait for it to finish before applying another location.");
                }
            }

            var cache = await _stateStore.LoadJobsCacheAsync(query);
            var status = await _stateStore.LoadSourceStatusAsync(query);
            var cacheMatches = cache?.Query?.IsEquivalentTo(query, _companyCatalog) == true;
            var lastRefreshed = status?.LastSuccessfulRefreshUtc ??
                cache?.LastRefreshedUtc ?? cache?.SavedAtUtc;
            var freshAfter = DateTimeOffset.UtcNow.AddMinutes(
                -_options.SourceSwitchCacheFreshnessMinutes);
            if (cacheMatches && lastRefreshed >= freshAfter)
            {
                var cachedJobs = CanonicalizeStableIdentities(cache!.Jobs, "recent source cache");
                var availableJobs = VisibleJobs(cachedJobs);
                var snapshot = new JobsSnapshot(
                    availableJobs,
                    availableJobs.Length,
                    lastRefreshed,
                    false,
                    null,
                    status?.DetailFailureCount ?? cache.DetailFailureCount,
                    true,
                    GetNewJobIds(availableJobs),
                    query,
                    GetJobStates(availableJobs),
                    GetJobClosures(availableJobs),
                    null,
                    new RefreshMetrics(0, 0, cachedJobs.Count, 0, 0, 0, 0, 0,
                        status?.ListingsTruncated ?? false, 0));
                lock (_gate)
                {
                    _currentQuery = query;
                    _cachedJobs = cachedJobs;
                    _snapshot = snapshot;
                }
                _logger.LogInformation(
                    "Switched to the recent {Company} cache ({JobCount} jobs, refreshed {LastRefreshed}); no provider listing or detail requests were made.",
                    company.DisplayName, availableJobs.Length, lastRefreshed);
                ScheduleSemanticClassification();
                return snapshot;
            }
        }
        finally
        {
            _sourceOperationGate.Release();
        }

        return await RefreshAsync(query, cancellationToken);
    }

    private Task<JobsSnapshot> RefreshCoreEntryAsync(
        JobSourceQuery query,
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
                query, previousQuery, previousSnapshot, cancellationToken);
            return _activeRefresh;
        }
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

            var sourceKey = $"{query.CompanyId}:{_stateStore.QueryFingerprint(query)}";
            await using var sharedSourceLease = await _sharedSourceRefreshCoordinator.AcquireAsync(
                sourceKey, cancellationToken);
            var sharedDocument = await _stateStore.LoadJobsCacheAsync(query);
            var sharedJobs = sharedDocument?.Query?.IsEquivalentTo(query, _companyCatalog) == true
                ? CanonicalizeStableIdentities(sharedDocument.Jobs, "shared detail cache")
                : _cachedJobs;
            job = sharedJobs.FirstOrDefault(item =>
                string.Equals(item.StableId, stableId, StringComparison.Ordinal)) ?? job;

            if (!string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
                _jobSourceClient.IsAnalysisCurrent(job))
            {
                lock (_gate)
                {
                    _cachedJobs = sharedJobs;
                    var available = VisibleJobs(sharedJobs);
                    _snapshot = _snapshot with
                    {
                        Jobs = available,
                        TotalJobs = available.Length,
                        JobStates = GetJobStates(available),
                        JobClosures = GetJobClosures(available)
                    };
                }
                return job;
            }

            JobRecord updated;
            if (!string.IsNullOrWhiteSpace(job.DescriptionHtml))
            {
                updated = _jobSourceClient.Reclassify(job);
            }
            else
            {
                updated = await _jobSourceClient.FetchJobDetailAsync(
                    _companyCatalog.Get(job.CompanyId), job, cancellationToken);
            }

            IReadOnlyList<JobRecord> cache;
            lock (_gate)
            {
                _cachedJobs = sharedJobs
                    .Select(item => string.Equals(item.StableId, stableId, StringComparison.Ordinal)
                        ? updated
                        : item)
                    .ToArray();
                var available = VisibleJobs(_cachedJobs);
                _snapshot = _snapshot with
                {
                    Jobs = available,
                    TotalJobs = available.Length,
                    JobStates = GetJobStates(available),
                    JobClosures = GetJobClosures(available)
                };
                cache = _cachedJobs;
            }
            await _stateStore.SaveJobsCacheAsync(
                cache,
                _snapshot.LastRefreshedUtc ?? DateTimeOffset.UtcNow,
                _snapshot.DetailFailureCount,
                query);
            ScheduleSemanticClassification();
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
                var createdAt = DateTimeOffset.UtcNow;
                entry = new JobHistoryEntry(
                    currentJob.RequisitionId,
                    currentJob.ExternalPath,
                    createdAt,
                    createdAt,
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

    public async Task<bool> SetWorkflowStateAsync(
        string stableId,
        string workflowState,
        string? closeReason = null)
    {
        if (string.IsNullOrWhiteSpace(stableId) || !JobWorkflowStates.IsValid(workflowState))
        {
            return false;
        }
        if (workflowState == JobWorkflowStates.Closed && !JobCloseReasons.IsValid(closeReason))
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
                var createdAt = DateTimeOffset.UtcNow;
                entry = new JobHistoryEntry(
                    currentJob.RequisitionId,
                    currentJob.ExternalPath,
                    createdAt,
                    createdAt,
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

            var now = DateTimeOffset.UtcNow;
            var currentState = JobWorkflowStates.Normalize(entry.WorkflowState);
            var appliedAt = workflowState switch
            {
                JobWorkflowStates.Closed => entry.AppliedAt ?? entry.WorkflowStateChangedAt ?? now,
                JobWorkflowStates.Applied when currentState == JobWorkflowStates.Closed => entry.AppliedAt,
                JobWorkflowStates.Applied => now,
                _ => null
            };
            _history.Jobs[stableId] = entry with
            {
                WorkflowState = workflowState,
                WorkflowStateChangedAt = now,
                Dismissed = false,
                DismissedAt = null,
                Saved = false,
                SavedAt = null,
                Applied = false,
                AppliedAt = appliedAt,
                CloseReason = workflowState == JobWorkflowStates.Closed ? closeReason : null,
                ClosedAt = workflowState == JobWorkflowStates.Closed ? now : null
            };
            await _stateStore.SaveJobHistoryAsync(CloneHistory());

            lock (_gate)
            {
                _snapshot = _snapshot with
                {
                    JobStates = GetJobStates(_snapshot.Jobs),
                    JobClosures = GetJobClosures(_snapshot.Jobs)
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
                    JobStates = GetJobStates(_snapshot.Jobs),
                    JobClosures = GetJobClosures(_snapshot.Jobs)
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
        CancellationToken cancellationToken)
    {
        await _sourceOperationGate.WaitAsync(cancellationToken);
        try
        {
            var company = _companyCatalog.Get(query.CompanyId);
            var observedStatus = await _stateStore.LoadSourceStatusAsync(query);
            var sourceKey = $"{company.Id}:{_stateStore.QueryFingerprint(query)}";
            await using var sharedSourceLease = await _sharedSourceRefreshCoordinator.AcquireAsync(
                sourceKey, cancellationToken);
            var currentStatus = await _stateStore.LoadSourceStatusAsync(query);
            if (currentStatus?.LastSuccessfulRefreshUtc is { } currentRefresh &&
                currentRefresh > (observedStatus?.LastSuccessfulRefreshUtc ?? DateTimeOffset.MinValue))
            {
                var currentCache = await _stateStore.LoadJobsCacheAsync(query);
                if (currentCache?.Query?.IsEquivalentTo(query, _companyCatalog) == true)
                {
                    return await ApplySharedCacheAfterConcurrentRefreshAsync(
                        query, currentCache, currentStatus, cancellationToken);
                }
            }
            _logger.LogInformation(
                "Refreshing the {Company} job snapshot for country {Country} and locations {Locations}.",
                company.DisplayName,
                SanitizeLogValue(query.CountryLabel),
                DescribeLocations(query));
            var cachedDocument = await _stateStore.LoadJobsCacheAsync(query);
            var cacheWasPresent = cachedDocument?.Query?.IsEquivalentTo(query, _companyCatalog) == true;
            var cachedJobs = cacheWasPresent
                ? CanonicalizeStableIdentities(cachedDocument!.Jobs, "source cache")
                : [];
            var fetched = await _jobSourceClient.FetchAllJobsAsync(
                company,
                query,
                progress => ReportRefreshProgress(query, progress),
                cancellationToken,
                cachedJobs: cachedJobs,
                filterSettings: null);
            var result = fetched with
            {
                Jobs = CanonicalizeStableIdentities(fetched.Jobs, "provider refresh")
            };
            var refreshedAt = DateTimeOffset.UtcNow;

            ReportRefreshProgress(query, new RefreshProgress("saving", 0, null));

            var changedJobIds = ChangedJobIds(cachedJobs, result.Jobs);
            var cacheChanged = !cacheWasPresent || changedJobIds.Count > 0;
            var historyChanged = false;
            await _historyGate.WaitAsync();
            try
            {
                historyChanged = ReconcileHistory(
                    result.Jobs.Where(job => job.IsSourceAvailable).ToArray(),
                    refreshedAt,
                    updateKnownLastSeen: true,
                    persistentlyChangedJobIds: changedJobIds);
                if (historyChanged)
                {
                    await _stateStore.SaveJobHistoryAsync(CloneHistory());
                }
            }
            finally
            {
                _historyGate.Release();
            }

            if (cacheChanged)
            {
                await _stateStore.SaveJobsCacheAsync(
                    result.Jobs,
                    refreshedAt,
                    result.DetailFailureCount,
                    query);
            }
            // Source status represents the last successful provider pass, not merely
            // the last time job content changed. This small write also lets an automatic
            // check survive an App Service recycle without making cache/history mutable.
            await _stateStore.SaveSourceStatusAsync(
                query,
                refreshedAt,
                result.DetailFailureCount,
                result.Metrics);

            var availableJobs = VisibleJobs(result.Jobs);
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
                GetJobClosures(availableJobs),
                null,
                result.Metrics);

            lock (_gate)
            {
                _cachedJobs = result.Jobs;
                _snapshot = refreshed;
            }

            _logger.LogInformation(
                "Refresh completed with {JobCount} jobs, {DetailFailureCount} detail failures, cache write {CacheWrite}, history write {HistoryWrite}.",
                result.Jobs.Count,
                result.DetailFailureCount,
                cacheChanged,
                historyChanged);
            ScheduleSemanticClassification();
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

    internal static string SanitizeLogValue(string? value) =>
        (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\u2028', ' ')
            .Replace('\u2029', ' ');

    private async Task<JobsSnapshot> ApplySharedCacheAfterConcurrentRefreshAsync(
        JobSourceQuery query,
        JobsCacheDocument cache,
        SourceStatusDocument status,
        CancellationToken cancellationToken)
    {
        var cachedJobs = CanonicalizeStableIdentities(cache.Jobs, "shared concurrent refresh");
        var historyChanged = false;
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            historyChanged = ReconcileHistory(
                cachedJobs.Where(job => job.IsSourceAvailable).ToArray(),
                status.LastSuccessfulRefreshUtc,
                updateKnownLastSeen: true);
            if (historyChanged)
            {
                await _stateStore.SaveJobHistoryAsync(CloneHistory());
            }
        }
        finally
        {
            _historyGate.Release();
        }

        var availableJobs = VisibleJobs(cachedJobs);
        var snapshot = new JobsSnapshot(
            availableJobs,
            availableJobs.Length,
            status.LastSuccessfulRefreshUtc,
            false,
            null,
            status.DetailFailureCount,
            true,
            GetNewJobIds(availableJobs),
            query,
            GetJobStates(availableJobs),
            GetJobClosures(availableJobs),
            null,
            new RefreshMetrics(0, 0, cachedJobs.Count, 0, 0, 0, 0, 0,
                status.ListingsTruncated, 0));
        lock (_gate)
        {
            _cachedJobs = cachedJobs;
            _snapshot = snapshot;
        }
        _logger.LogInformation(
            "Reused a shared {Company} refresh completed by another workspace; no duplicate provider requests were made.",
            _companyCatalog.Get(query.CompanyId).DisplayName);
        ScheduleSemanticClassification();
        return snapshot;
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

        var cache = await _stateStore.LoadJobsCacheAsync(query);
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
        bool updateKnownLastSeen,
        IReadOnlySet<string>? persistentlyChangedJobIds = null)
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
            else if (updateKnownLastSeen &&
                persistentlyChangedJobIds?.Contains(stableId) == true)
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

    private static HashSet<string> ChangedJobIds(
        IReadOnlyList<JobRecord> before,
        IReadOnlyList<JobRecord> after)
    {
        var beforeById = before.ToDictionary(job => job.StableId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(job => job.StableId, StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in beforeById.Keys.Concat(afterById.Keys))
        {
            if (!beforeById.TryGetValue(id, out var oldJob) ||
                !afterById.TryGetValue(id, out var newJob) ||
                !JsonSerializer.SerializeToUtf8Bytes(oldJob).AsSpan()
                    .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(newJob)))
            {
                changed.Add(id);
            }
        }
        return changed;
    }

    private IReadOnlyList<JobRecord> CanonicalizeStableIdentities(
        IReadOnlyList<JobRecord> jobs,
        string source)
    {
        var canonical = new List<JobRecord>(jobs.Count);
        foreach (var identityGroup in jobs.GroupBy(job => job.StableId, StringComparer.Ordinal))
        {
            if (identityGroup.Count() == 1)
            {
                canonical.Add(identityGroup.First());
                continue;
            }

            var variants = identityGroup
                .GroupBy(job => string.Join('\n',
                    job.Title.Trim().ToUpperInvariant(),
                    job.RequisitionId.Trim().ToUpperInvariant(),
                    job.TimeType.Trim().ToUpperInvariant()), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            _logger.Log(variants.Length == 1 ? LogLevel.Information : LogLevel.Warning,
                "Canonicalizing {DuplicateCount} records with stable identity {StableId} from {Source}; {VariantCount} material variants were found.",
                identityGroup.Count(), identityGroup.Key, source, variants.Length);

            for (var variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                var records = variants[variantIndex]
                    .OrderBy(job => job.ExternalPath, StringComparer.Ordinal)
                    .ToArray();
                var selected = records[0];
                var locations = records
                    .SelectMany(job => new[] { job.PrimaryLocation }.Concat(job.AdditionalLocations))
                    .Where(location => !string.IsNullOrWhiteSpace(location))
                    .Select(location => location.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                canonical.Add(selected with
                {
                    PrimaryLocation = locations.FirstOrDefault() ?? selected.PrimaryLocation,
                    AdditionalLocations = locations.Skip(1).ToArray(),
                    IdentityDiscriminator = variantIndex == 0
                        ? selected.IdentityDiscriminator
                        : $"path:{selected.ExternalPath.Trim('/')}"
                });
            }
        }
        return canonical;
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

    private JobRecord[] VisibleJobs(IReadOnlyList<JobRecord> jobs) => jobs
        .Where(job => job.IsSourceAvailable || IsUserMaintained(job.StableId))
        .ToArray();

    public SemanticClassificationBackfillStatus GetSemanticClassificationStatus()
    {
        if (_semanticClassification is null)
            return new(0, 0, 0, 0, false);
        JobRecord[] jobs;
        bool running;
        lock (_gate)
        {
            jobs = _cachedJobs.Where(job => !string.IsNullOrWhiteSpace(job.DescriptionHtml)).ToArray();
            running = _semanticClassificationTask is { IsCompleted: false };
        }
        var current = jobs.Count(_semanticClassification.IsCurrent);
        var unavailable = jobs.Count(job => !_semanticClassification.IsCurrent(job) &&
            job.SemanticClassificationStatus == SemanticClassificationStates.Unavailable);
        return new(jobs.Length, current, jobs.Length - current - unavailable, unavailable, running);
    }

    public async Task<QwenDeepAnalysis?> DeepAnalyzeWithQwenAsync(
        string stableId,
        CancellationToken cancellationToken = default)
    {
        if (_semanticClassification is null) return null;
        var job = await GetJobDetailAsync(stableId, cancellationToken);
        if (job is null || string.IsNullOrWhiteSpace(job.DescriptionHtml)) return null;
        var analysis = await _semanticClassification.DeepAnalyzeAsync(job, cancellationToken);
        if (analysis is null) return null;

        await _sourceOperationGate.WaitAsync(cancellationToken);
        try
        {
            JobSourceQuery query;
            lock (_gate) { query = _currentQuery; }
            var sourceKey = $"{query.CompanyId}:{_stateStore.QueryFingerprint(query)}";
            await using var sharedSourceLease =
                await _sharedSourceRefreshCoordinator.AcquireAsync(sourceKey, cancellationToken);
            var document = await _stateStore.LoadJobsCacheAsync(query);
            if (document?.Query?.IsEquivalentTo(query, _companyCatalog) != true) return null;
            var current = document.Jobs.FirstOrDefault(item => item.StableId == stableId);
            if (current is null || SemanticClassifierContract.PostingContentHash(
                    current.Title, JobAnalysis.HtmlToPlainText(current.DescriptionHtml)) !=
                analysis.PostingContentHash)
                return null;
            var updated = current with { QwenDeepAnalysis = analysis };
            var jobs = document.Jobs.Select(item => item.StableId == stableId ? updated : item).ToArray();
            await _stateStore.SaveJobsCacheAsync(jobs,
                document.LastRefreshedUtc ?? document.SavedAtUtc,
                document.DetailFailureCount, query);
            lock (_gate)
            {
                _cachedJobs = jobs;
                var visible = VisibleJobs(jobs);
                _snapshot = _snapshot with { Jobs = visible, TotalJobs = visible.Length };
            }
            return analysis;
        }
        finally
        {
            _sourceOperationGate.Release();
        }
    }

    public SemanticClassificationBackfillStatus StartSemanticClassificationBackfill()
    {
        ScheduleSemanticClassification();
        return GetSemanticClassificationStatus();
    }

    private void ScheduleSemanticClassification()
    {
        if (_semanticClassification is null)
            return;
        lock (_gate)
        {
            if (_semanticClassificationTask is { IsCompleted: false })
                return;
            _semanticClassificationTask = Task.Run(RunSemanticClassificationAsync);
        }
    }

    private async Task RunSemanticClassificationAsync()
    {
        try
        {
            while (true)
            {
                JobRecord? candidate;
                lock (_gate)
                {
                    var newIds = _snapshot.NewJobIds.ToHashSet(StringComparer.Ordinal);
                    candidate = _cachedJobs
                        .Where(job => !string.IsNullOrWhiteSpace(job.DescriptionHtml) &&
                            !_semanticClassification!.IsCurrent(job))
                        .OrderBy(job => _history.Jobs.TryGetValue(job.StableId, out var history) &&
                            history.WorkflowState == JobWorkflowStates.Closed ? 1 : 0)
                        .ThenBy(job => job.IsSourceAvailable ? 0 : 1)
                        .ThenBy(job => newIds.Contains(job.StableId) ? 0 : 1)
                        .ThenByDescending(job => job.StartDate ?? DateOnly.MinValue)
                        .ThenByDescending(job => job.DetailCachedAtUtc ?? DateTimeOffset.MinValue)
                        .ThenBy(job => job.StableId, StringComparer.Ordinal)
                        .FirstOrDefault();
                }
                if (candidate is null)
                    return;

                var attempt = await _semanticClassification!.ClassifyAsync(candidate);
                if (!attempt.Available || attempt.Classification is null)
                {
                    await PersistSemanticClassificationAsync(
                        candidate.StableId,
                        null,
                        SemanticClassificationStates.Unavailable,
                        candidate.SemanticClassification);
                    _logger.LogWarning(
                        "Semantic classification paused after the classifier was unavailable; browsing and cached analysis remain available.");
                    return;
                }

                await PersistSemanticClassificationAsync(
                    candidate.StableId,
                    attempt.Classification,
                    SemanticClassificationStates.Complete,
                    candidate.SemanticClassification);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Background semantic classification stopped unexpectedly; ingestion and browsing were not affected.");
        }
    }

    private async Task PersistSemanticClassificationAsync(
        string stableId,
        SemanticJobClassification? classification,
        string status,
        SemanticJobClassification? expectedPrevious)
    {
        await _sourceOperationGate.WaitAsync();
        try
        {
            JobSourceQuery query;
            lock (_gate) { query = _currentQuery; }
            var sourceKey = $"{query.CompanyId}:{_stateStore.QueryFingerprint(query)}";
            await using var sharedSourceLease = await _sharedSourceRefreshCoordinator.AcquireAsync(sourceKey);
            var document = await _stateStore.LoadJobsCacheAsync(query);
            if (document?.Query?.IsEquivalentTo(query, _companyCatalog) != true)
                return;
            var current = document.Jobs.FirstOrDefault(job => job.StableId == stableId);
            if (current is null || string.IsNullOrWhiteSpace(current.DescriptionHtml))
                return;
            if (classification is not null)
            {
                var description = JobAnalysis.HtmlToPlainText(current.DescriptionHtml);
                var contentHash = SemanticClassifierContract.PostingContentHash(current.Title, description);
                if (contentHash != classification.PostingContentHash)
                    return;
            }
            else if (current.SemanticClassification != expectedPrevious &&
                _semanticClassification!.IsCurrent(current))
            {
                return;
            }

            var updated = current with
            {
                SemanticClassification = classification ?? current.SemanticClassification,
                SemanticClassificationStatus = status,
                SemanticClassificationLastAttemptUtc = DateTimeOffset.UtcNow
            };
            var jobs = document.Jobs.Select(job => job.StableId == stableId ? updated : job).ToArray();
            await _stateStore.SaveJobsCacheAsync(
                jobs,
                document.LastRefreshedUtc ?? document.SavedAtUtc,
                document.DetailFailureCount,
                query);
            lock (_gate)
            {
                if (_currentQuery.IsEquivalentTo(query, _companyCatalog))
                {
                    _cachedJobs = jobs;
                    var visible = VisibleJobs(jobs);
                    _snapshot = _snapshot with
                    {
                        Jobs = visible,
                        TotalJobs = visible.Length,
                        JobStates = GetJobStates(visible),
                        JobClosures = GetJobClosures(visible)
                    };
                }
            }
        }
        finally
        {
            _sourceOperationGate.Release();
        }
    }

    private bool IsUserMaintained(string stableId) =>
        _history.Jobs.TryGetValue(stableId, out var entry) &&
        JobWorkflowStates.Normalize(entry.WorkflowState) is
            JobWorkflowStates.Saved or JobWorkflowStates.Applied or JobWorkflowStates.Closed;

    private Dictionary<string, JobClosureInfo> GetJobClosures(IReadOnlyList<JobRecord> jobs) => jobs
        .Where(job => _history.Jobs.TryGetValue(job.StableId, out var entry) &&
            JobWorkflowStates.Normalize(entry.WorkflowState) == JobWorkflowStates.Closed &&
            JobCloseReasons.IsValid(entry.CloseReason) && entry.ClosedAt is not null)
        .ToDictionary(
            job => job.StableId,
            job =>
            {
                var entry = _history.Jobs[job.StableId];
                return new JobClosureInfo(entry.CloseReason!, entry.ClosedAt!.Value, entry.AppliedAt);
            },
            StringComparer.Ordinal);

    private JobHistoryDocument CloneHistory() => _history with
    {
        Jobs = _history.Jobs.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal)
    };
}
