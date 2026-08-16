using System.Collections.Concurrent;
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
    string Describe(WorkspaceDataFile file);
    Task<bool> ExistsAsync(WorkspaceDataFile file, CancellationToken cancellationToken = default);
    Task<T?> ReadJsonAsync<T>(WorkspaceDataFile file, CancellationToken cancellationToken = default);
    Task WriteJsonAsync<T>(WorkspaceDataFile file, T value, CancellationToken cancellationToken = default);
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
    public static string FileName(WorkspaceDataFile file) => file switch
    {
        WorkspaceDataFile.Settings => "settings.json",
        WorkspaceDataFile.JobsCache => "jobs-cache.json",
        WorkspaceDataFile.JobHistory => "job-history.json",
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };
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
    private readonly ConcurrentDictionary<WorkspaceDataFile, SemaphoreSlim> _fileGates = new();

    public FileWorkspaceDataStore(string dataDirectory, ILogger<FileWorkspaceDataStore> logger)
    {
        Description = Path.GetFullPath(dataDirectory);
        _logger = logger;
        EnsureDataDirectoryIsWritable();
    }

    public string Description { get; }

    public string Describe(WorkspaceDataFile file) =>
        Path.Combine(Description, WorkspaceDataFiles.FileName(file));

    public Task<bool> ExistsAsync(WorkspaceDataFile file, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(Describe(file)));

    public async Task<T?> ReadJsonAsync<T>(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default)
    {
        var path = Describe(file);
        if (!File.Exists(path))
        {
            return default;
        }

        var gate = Gate(file);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        var path = Describe(file);
        var gate = Gate(file);
        await gate.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(
            Description,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
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

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        var files = Enum.GetValues<WorkspaceDataFile>();
        var acquired = new List<SemaphoreSlim>(files.Length);
        try
        {
            foreach (var file in files)
            {
                var gate = Gate(file);
                await gate.WaitAsync(cancellationToken);
                acquired.Add(gate);
            }

            var deleted = 0;
            foreach (var file in files)
            {
                var path = Describe(file);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                deleted++;
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

    private SemaphoreSlim Gate(WorkspaceDataFile file) =>
        _fileGates.GetOrAdd(file, _ => new SemaphoreSlim(1, 1));

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
