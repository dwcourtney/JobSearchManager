using System.Collections.Concurrent;

namespace JobSearchManager;

public sealed class SharedSourceRefreshCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceGates =
        new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFingerprint))
        {
            throw new ArgumentException("A source fingerprint is required.", nameof(sourceFingerprint));
        }

        var gate = _sourceGates.GetOrAdd(sourceFingerprint, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
