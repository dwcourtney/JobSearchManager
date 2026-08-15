using System.Threading.Channels;

namespace LeidosJobsViewer;

public sealed class AutomaticJobCheckService : BackgroundService
{
    private readonly JobCatalog _catalog;
    private readonly ILogger<AutomaticJobCheckService> _logger;
    private readonly object _gate = new();
    private readonly Channel<bool> _scheduleChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private bool _configured;
    private bool _enabled = true;
    private int _intervalMinutes = 60;
    private bool _isChecking;
    private DateTimeOffset? _lastCheckedUtc;
    private DateTimeOffset? _nextCheckUtc;
    private DateTimeOffset? _lastAutomaticRefreshUtc;

    public AutomaticJobCheckService(
        JobCatalog catalog,
        ILogger<AutomaticJobCheckService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public AutomaticCheckStatus Status
    {
        get
        {
            lock (_gate)
            {
                return new AutomaticCheckStatus(
                    _enabled,
                    _intervalMinutes,
                    _isChecking,
                    _lastCheckedUtc,
                    _nextCheckUtc,
                    _lastAutomaticRefreshUtc);
            }
        }
    }

    public void ApplySettings(ViewerSettings settings)
    {
        var enabled = settings.AutomaticCheckEnabled ?? true;
        lock (_gate)
        {
            if (_configured &&
                _enabled == enabled &&
                _intervalMinutes == settings.AutomaticCheckIntervalMinutes)
            {
                return;
            }

            _configured = true;
            _enabled = enabled;
            _intervalMinutes = settings.AutomaticCheckIntervalMinutes;
            _nextCheckUtc = _enabled
                ? DateTimeOffset.UtcNow.AddMinutes(_intervalMinutes)
                : null;
        }
        SignalScheduleChanged();
    }

    public void ResetSchedule()
    {
        lock (_gate)
        {
            _nextCheckUtc = _enabled
                ? DateTimeOffset.UtcNow.AddMinutes(_intervalMinutes)
                : null;
        }
        SignalScheduleChanged();
    }

    public async Task<AutomaticCheckResult> CheckNowAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_isChecking)
            {
                return new AutomaticCheckResult(false, true, 0, [], false);
            }
            _isChecking = true;
        }

        try
        {
            var result = await _catalog.CheckForUnknownJobsAsync(cancellationToken);
            if (result.Performed)
            {
                lock (_gate)
                {
                    _lastCheckedUtc = DateTimeOffset.UtcNow;
                    if (result.FullRefreshTriggered)
                    {
                        _lastAutomaticRefreshUtc = _lastCheckedUtc;
                    }
                }

                if (result.FullRefreshTriggered)
                {
                    _logger.LogInformation(
                        "Automatic check found {NewJobCount} unknown job identities and completed a full refresh.",
                        result.UnknownStableIds.Count);
                }
                else if (result.UnknownStableIds.Count == 0)
                {
                    _logger.LogInformation(
                        "Automatic check examined {ListingCount} listing identities; no unknown jobs were found.",
                        result.ListingCount);
                }
                else
                {
                    _logger.LogWarning(
                        "Automatic check found {NewJobCount} unknown job identities, but the full refresh did not complete. The current snapshot remains available.",
                        result.UnknownStableIds.Count);
                }
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automatic job check failed; the current snapshot was left unchanged.");
            return new AutomaticCheckResult(false, false, 0, [], false);
        }
        finally
        {
            lock (_gate)
            {
                _isChecking = false;
                _nextCheckUtc = _enabled
                    ? DateTimeOffset.UtcNow.AddMinutes(_intervalMinutes)
                    : null;
            }
            SignalScheduleChanged();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset? dueAt;
            lock (_gate)
            {
                dueAt = _enabled ? _nextCheckUtc : null;
            }

            if (dueAt is null)
            {
                await WaitForScheduleChangeAsync(stoppingToken);
                continue;
            }

            var delay = dueAt.Value - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                var delayTask = Task.Delay(delay, stoppingToken);
                var changeTask = _scheduleChanges.Reader.WaitToReadAsync(stoppingToken).AsTask();
                if (await Task.WhenAny(delayTask, changeTask) == changeTask)
                {
                    DrainScheduleChanges();
                    continue;
                }
            }

            await CheckNowAsync(stoppingToken);
        }
    }

    private async Task WaitForScheduleChangeAsync(CancellationToken cancellationToken)
    {
        await _scheduleChanges.Reader.ReadAsync(cancellationToken);
        DrainScheduleChanges();
    }

    private void SignalScheduleChanged() => _scheduleChanges.Writer.TryWrite(true);

    private void DrainScheduleChanges()
    {
        while (_scheduleChanges.Reader.TryRead(out _)) { }
    }
}
