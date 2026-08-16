using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace JobSearchManager;

public enum WorkspaceDataFile
{
    Settings,
    JobsCache,
    JobHistory
}

public interface IWorkspaceDataStore
{
    string Description { get; }
    WorkspaceStorageDiagnostics Diagnostics { get; }
    string Describe(WorkspaceDataFile file);
    string DescribeCompanyCache(string companyId, string queryFingerprint);
    string DescribeSourceStatus(string companyId, string queryFingerprint);
    Task<bool> ExistsAsync(WorkspaceDataFile file, CancellationToken cancellationToken = default);
    Task<bool> CompanyCacheExistsAsync(string companyId, string queryFingerprint, CancellationToken cancellationToken = default);
    Task<T?> ReadJsonAsync<T>(WorkspaceDataFile file, CancellationToken cancellationToken = default);
    Task<T?> ReadCompanyCacheJsonAsync<T>(string companyId, string queryFingerprint, CancellationToken cancellationToken = default);
    Task<T?> ReadSourceStatusJsonAsync<T>(string companyId, string queryFingerprint, CancellationToken cancellationToken = default);
    Task WriteJsonAsync<T>(WorkspaceDataFile file, T value, CancellationToken cancellationToken = default);
    Task WriteCompanyCacheJsonAsync<T>(string companyId, string queryFingerprint, T value, CancellationToken cancellationToken = default);
    Task WriteSourceStatusJsonAsync<T>(string companyId, string queryFingerprint, T value, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(WorkspaceDataFile file, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
}

public interface IWorkspaceDataStoreFactory
{
    IWorkspaceDataStore Create(string workspaceId);
    Task ValidateAsync(CancellationToken cancellationToken = default);
}

public class WorkspaceStorageException : Exception
{
    public WorkspaceStorageException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WorkspaceConcurrencyException : WorkspaceStorageException
{
    public WorkspaceConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class WorkspaceBusyException : Exception
{
    public WorkspaceBusyException(string message) : base(message) { }
}

public static class WorkspaceDataFiles
{
    private static readonly Regex CompanyIdPattern = new(
        "^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FingerprintPattern = new(
        "^[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string FileName(WorkspaceDataFile file) => file switch
    {
        WorkspaceDataFile.Settings => "settings.json",
        WorkspaceDataFile.JobsCache => "jobs-cache.json",
        WorkspaceDataFile.JobHistory => "job-history.json",
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    public static string CompanyCacheRelativePath(string companyId, string queryFingerprint) =>
        ScopedRelativePath("job-caches", companyId, queryFingerprint);

    public static string SourceStatusRelativePath(string companyId, string queryFingerprint) =>
        ScopedRelativePath("source-status", companyId, queryFingerprint);

    private static string ScopedRelativePath(string root, string companyId, string queryFingerprint)
    {
        companyId = companyId?.Trim().ToLowerInvariant() ?? "";
        queryFingerprint = queryFingerprint?.Trim().ToLowerInvariant() ?? "";
        if (!CompanyIdPattern.IsMatch(companyId))
        {
            throw new ArgumentException("Invalid company cache identifier.", nameof(companyId));
        }
        if (!FingerprintPattern.IsMatch(queryFingerprint))
        {
            throw new ArgumentException("Invalid source-query fingerprint.", nameof(queryFingerprint));
        }
        return $"{root}/{companyId}/{queryFingerprint}.json";
    }
}

public sealed record WorkspaceStorageDiagnosticsSnapshot(
    int Reads,
    int Writes,
    int Deletes,
    long BytesWritten,
    IReadOnlyDictionary<string, int> WritesByDocument);

public sealed class WorkspaceStorageDiagnostics
{
    private int _reads;
    private int _writes;
    private int _deletes;
    private long _bytesWritten;
    private readonly ConcurrentDictionary<string, int> _writesByDocument =
        new(StringComparer.Ordinal);

    public WorkspaceStorageDiagnosticsSnapshot Snapshot() => new(
        Volatile.Read(ref _reads),
        Volatile.Read(ref _writes),
        Volatile.Read(ref _deletes),
        Interlocked.Read(ref _bytesWritten),
        new Dictionary<string, int>(_writesByDocument, StringComparer.Ordinal));

    public void Reset()
    {
        Interlocked.Exchange(ref _reads, 0);
        Interlocked.Exchange(ref _writes, 0);
        Interlocked.Exchange(ref _deletes, 0);
        Interlocked.Exchange(ref _bytesWritten, 0);
        _writesByDocument.Clear();
    }

    internal void RecordRead() => Interlocked.Increment(ref _reads);

    internal void RecordWrite(string document, long bytes)
    {
        Interlocked.Increment(ref _writes);
        Interlocked.Add(ref _bytesWritten, bytes);
        _writesByDocument.AddOrUpdate(document, 1, (_, count) => count + 1);
    }

    internal void RecordDelete() => Interlocked.Increment(ref _deletes);
}

public sealed class FileWorkspaceDataStoreFactory : IWorkspaceDataStoreFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

    public FileWorkspaceDataStoreFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IWorkspaceDataStore Create(string workspaceId)
    {
        if (!string.Equals(workspaceId, WorkspaceContext.LocalWorkspaceId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local file storage supports only the local workspace.");
        }

        return new FileWorkspaceDataStore(
            _dataDirectory,
            _loggerFactory.CreateLogger<FileWorkspaceDataStore>());
    }

    public Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        _ = Create(WorkspaceContext.LocalWorkspaceId);
        return Task.CompletedTask;
    }
}

public sealed class FileWorkspaceDataStore : IWorkspaceDataStore
{
    private readonly ILogger<FileWorkspaceDataStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly JsonSerializerOptions _compactJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileGates = new(StringComparer.Ordinal);

    public FileWorkspaceDataStore(string dataDirectory, ILogger<FileWorkspaceDataStore> logger)
    {
        Description = Path.GetFullPath(dataDirectory);
        _logger = logger;
        EnsureDataDirectoryIsWritable();
    }

    public string Description { get; }
    public WorkspaceStorageDiagnostics Diagnostics { get; } = new();

    public string Describe(WorkspaceDataFile file) =>
        Path.Combine(Description, WorkspaceDataFiles.FileName(file));

    public string DescribeCompanyCache(string companyId, string queryFingerprint) =>
        DescribeRelative(WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint));

    public string DescribeSourceStatus(string companyId, string queryFingerprint) =>
        DescribeRelative(WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint));

    public Task<bool> ExistsAsync(WorkspaceDataFile file, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(Describe(file)));

    public Task<bool> CompanyCacheExistsAsync(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(DescribeCompanyCache(companyId, queryFingerprint)));

    public async Task<T?> ReadJsonAsync<T>(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(WorkspaceDataFiles.FileName(file), cancellationToken);

    public async Task<T?> ReadCompanyCacheJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(
            WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint), cancellationToken);

    public async Task<T?> ReadSourceStatusJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(
            WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint), cancellationToken);

    private async Task<T?> ReadRelativeJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var path = DescribeRelative(relativePath);
        if (!File.Exists(path))
        {
            return default;
        }

        var gate = Gate(relativePath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
            Diagnostics.RecordRead();
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not read persistent state from {StatePath}. The file was left untouched and defaults will be used.",
                path);
            return default;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task WriteJsonAsync<T>(
        WorkspaceDataFile file,
        T value,
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(WorkspaceDataFiles.FileName(file), value, cancellationToken);

    public async Task WriteCompanyCacheJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        T value,
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(
            WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint), value, cancellationToken);

    public async Task WriteSourceStatusJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        T value,
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(
            WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint), value, cancellationToken);

    private async Task WriteRelativeJsonAsync<T>(
        string relativePath,
        T value,
        CancellationToken cancellationToken)
    {
        var path = DescribeRelative(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = Gate(relativePath);
        await gate.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    IsCompactDocument(relativePath) ? _compactJsonOptions : _jsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
                catch (IOException)
                {
                    File.Move(temporaryPath, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, path);
            }
            var bytes = new FileInfo(path).Length;
            Diagnostics.RecordWrite(relativePath, bytes);
            _logger.LogInformation(
                "Wrote {StorageBytes} bytes to workspace document {DocumentName}.",
                bytes,
                relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceStorageException(
                $"Persistent state could not be saved in the application-local data directory '{Description}'. " +
                "Move the application to a writable directory or grant write access. No AppData fallback is used.",
                ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            gate.Release();
        }
    }

    private static bool IsCompactDocument(string relativePath) =>
        relativePath.StartsWith("job-caches/", StringComparison.Ordinal) ||
        relativePath.StartsWith("source-status/", StringComparison.Ordinal);

    public async Task<bool> DeleteAsync(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default)
    {
        var relativePath = WorkspaceDataFiles.FileName(file);
        var gate = Gate(relativePath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = DescribeRelative(relativePath);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            Diagnostics.RecordDelete();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        var relativePaths = Enum.GetValues<WorkspaceDataFile>()
            .Select(WorkspaceDataFiles.FileName)
            .ToList();
        foreach (var directoryName in new[] { "job-caches", "source-status" })
        {
            var directory = DescribeRelative(directoryName);
            if (!Directory.Exists(directory)) continue;
            relativePaths.AddRange(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Description, path)
                    .Replace(Path.DirectorySeparatorChar, '/')));
        }
        relativePaths = relativePaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var acquired = new List<SemaphoreSlim>(relativePaths.Count);
        try
        {
            foreach (var relativePath in relativePaths)
            {
                var gate = Gate(relativePath);
                await gate.WaitAsync(cancellationToken);
                acquired.Add(gate);
            }

            var deleted = 0;
            foreach (var relativePath in relativePaths)
            {
                var path = DescribeRelative(relativePath);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                Diagnostics.RecordDelete();
                deleted++;
            }
            foreach (var directoryName in new[] { "job-caches", "source-status" })
            {
                var directory = DescribeRelative(directoryName);
                if (!Directory.Exists(directory)) continue;
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
                else
                {
                    foreach (var child in Directory.GetDirectories(directory))
                    {
                        if (!Directory.EnumerateFileSystemEntries(child).Any()) Directory.Delete(child);
                    }
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
            }
            return deleted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceStorageException(
                $"Persistent state could not be reset in the application-local data directory '{Description}'. " +
                "The existing workspace identity and any documents not yet deleted were preserved.",
                ex);
        }
        finally
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
            {
                acquired[index].Release();
            }
        }
    }

    private string DescribeRelative(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(Description, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Description.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Workspace document path escaped its data directory.", nameof(relativePath));
        }
        return fullPath;
    }

    private SemaphoreSlim Gate(string relativePath) =>
        _fileGates.GetOrAdd(relativePath, _ => new SemaphoreSlim(1, 1));

    private void EnsureDataDirectoryIsWritable()
    {
        try
        {
            Directory.CreateDirectory(Description);
            var probePath = Path.Combine(Description, $".write-test-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.WriteThrough))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceStorageException(
                $"The application-local data directory '{Description}' is not writable. " +
                "Persistent state must be stored beside the application; no AppData fallback will be used.",
                ex);
        }
    }
}
