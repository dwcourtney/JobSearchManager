using System.Collections.Concurrent;

namespace WorkdayJobManager;

public sealed record WorkspaceRuntime(
    AppStateStore StateStore,
    JobCatalog Catalog,
    AutomaticJobCheckService AutomaticChecks);

public sealed class WorkspaceRuntimeManager
{
    private const int MaximumResidentWorkspaces = 100;
    private static readonly TimeSpan IdleWorkspaceLifetime = TimeSpan.FromMinutes(30);

    private sealed class RuntimeEntry
    {
        private long _lastAccessUtcTicks;

        public RuntimeEntry(Lazy<Task<WorkspaceRuntime>> runtime)
        {
            Runtime = runtime;
            Touch();
        }

        public Lazy<Task<WorkspaceRuntime>> Runtime { get; }
        public long LastAccessUtcTicks => Volatile.Read(ref _lastAccessUtcTicks);
        public void Touch() => Interlocked.Exchange(ref _lastAccessUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    private readonly IServiceProvider _services;
    private readonly IWorkspaceDataStoreFactory _dataStores;
    private readonly ILogger<WorkspaceRuntimeManager> _logger;
    private readonly ConcurrentDictionary<string, RuntimeEntry> _runtimes =
        new(StringComparer.Ordinal);

    public WorkspaceRuntimeManager(
        IServiceProvider services,
        IWorkspaceDataStoreFactory dataStores,
        ILogger<WorkspaceRuntimeManager> logger)
    {
        _services = services;
        _dataStores = dataStores;
        _logger = logger;
    }

    public async Task<WorkspaceRuntime> GetAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        ValidateWorkspaceId(workspaceId);
        PruneIdleEntries();
        if (_runtimes.Count >= MaximumResidentWorkspaces && !_runtimes.ContainsKey(workspaceId))
        {
            throw new InvalidOperationException(
                "The server is temporarily at its active-workspace capacity. Try again shortly.");
        }

        var entry = _runtimes.GetOrAdd(
            workspaceId,
            id => new RuntimeEntry(new Lazy<Task<WorkspaceRuntime>>(
                () => CreateAsync(id),
                LazyThreadSafetyMode.ExecutionAndPublication)));
        entry.Touch();
        try
        {
            return await entry.Runtime.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _runtimes.TryRemove(new KeyValuePair<string, RuntimeEntry>(workspaceId, entry));
            throw;
        }
    }

    private async Task<WorkspaceRuntime> CreateAsync(string workspaceId)
    {
        var dataStore = _dataStores.Create(workspaceId);
        var stateStore = ActivatorUtilities.CreateInstance<AppStateStore>(_services, dataStore);
        await stateStore.EnsureSettingsFileAsync();
        var settings = await stateStore.LoadSettingsAsync();
        await stateStore.SaveSettingsAsync(settings);

        var catalog = ActivatorUtilities.CreateInstance<JobCatalog>(_services, stateStore);
        await catalog.InitializeAsync(WorkdayQuery.FromSettings(
            settings,
            _services.GetRequiredService<CompanyCatalog>()));
        var automaticChecks = ActivatorUtilities.CreateInstance<AutomaticJobCheckService>(
            _services,
            catalog);
        automaticChecks.ApplySettings(settings);

        _logger.LogInformation(
            "Initialized workspace {WorkspaceReference} using {StorageDescription}.",
            workspaceId == WorkspaceContext.LocalWorkspaceId
                ? WorkspaceContext.LocalWorkspaceId
                : WorkspaceIdentity.Redact(workspaceId),
            dataStore.Description);
        return new WorkspaceRuntime(stateStore, catalog, automaticChecks);
    }

    private void PruneIdleEntries()
    {
        if (_runtimes.Count < MaximumResidentWorkspaces)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.Subtract(IdleWorkspaceLifetime).UtcTicks;
        foreach (var pair in _runtimes)
        {
            if (pair.Value.LastAccessUtcTicks >= cutoff ||
                !pair.Value.Runtime.IsValueCreated ||
                !pair.Value.Runtime.Value.IsCompletedSuccessfully)
            {
                continue;
            }

            var runtime = pair.Value.Runtime.Value.Result;
            if (runtime.Catalog.Snapshot.IsRefreshing || runtime.AutomaticChecks.Status.IsChecking)
            {
                continue;
            }

            _runtimes.TryRemove(pair);
        }
    }

    private static void ValidateWorkspaceId(string workspaceId)
    {
        if (!string.Equals(workspaceId, WorkspaceContext.LocalWorkspaceId, StringComparison.Ordinal) &&
            !WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new InvalidOperationException("The workspace identifier is invalid.");
        }
    }
}

public sealed class WorkspaceRuntimeProvider
{
    private readonly WorkspaceRuntimeManager _runtimes;
    private readonly WorkspaceContext _workspace;

    public WorkspaceRuntimeProvider(
        WorkspaceRuntimeManager runtimes,
        WorkspaceContext workspace)
    {
        _runtimes = runtimes;
        _workspace = workspace;
    }

    public Task<WorkspaceRuntime> GetAsync(CancellationToken cancellationToken = default) =>
        _runtimes.GetAsync(_workspace.WorkspaceId, cancellationToken);
}
