using System.Collections.Concurrent;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace JobSearchManager;

public sealed class AzureBlobWorkspaceDataStoreFactory : IWorkspaceDataStoreFactory
{
    private readonly BlobContainerClient _container;
    private readonly ILoggerFactory _loggerFactory;

    public AzureBlobWorkspaceDataStoreFactory(
        BlobContainerClient container,
        ILoggerFactory loggerFactory)
    {
        _container = container;
        _loggerFactory = loggerFactory;
    }

    public IWorkspaceDataStore Create(string workspaceId)
    {
        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new InvalidOperationException("The anonymous workspace identifier is invalid.");
        }

        return new AzureBlobWorkspaceDataStore(
            _container,
            workspaceId,
            _loggerFactory.CreateLogger<AzureBlobWorkspaceDataStore>());
    }

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var properties = await _container.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (properties.Value.PublicAccess != PublicAccessType.None)
            {
                throw new InvalidOperationException(
                    "The Azure workspace Blob container must not allow anonymous public access.");
            }
        }
        catch (Exception ex) when (ex is RequestFailedException or InvalidOperationException)
        {
            throw new WorkspaceStorageException(
                "Azure Blob workspace storage could not be accessed. Verify the storage settings, private container, " +
                "managed identity, and Storage Blob Data Contributor assignment.",
                ex);
        }
    }
}

public sealed class AzureBlobWorkspaceDataStore : IWorkspaceDataStore
{
    private sealed record BlobVersion(bool Exists, ETag? ETag);

    private readonly BlobContainerClient _container;
    private readonly string _workspaceId;
    private readonly ILogger<AzureBlobWorkspaceDataStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ConcurrentDictionary<WorkspaceDataFile, SemaphoreSlim> _fileGates = new();
    private readonly ConcurrentDictionary<WorkspaceDataFile, BlobVersion> _versions = new();

    public AzureBlobWorkspaceDataStore(
        BlobContainerClient container,
        string workspaceId,
        ILogger<AzureBlobWorkspaceDataStore> logger)
    {
        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new ArgumentException("Invalid workspace identifier.", nameof(workspaceId));
        }

        _container = container;
        _workspaceId = workspaceId;
        _logger = logger;
        Description = $"Azure Blob workspace {WorkspaceIdentity.Redact(workspaceId)}";
    }

    public string Description { get; }

    public static string BuildBlobName(string workspaceId, WorkspaceDataFile file)
    {
        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new ArgumentException("Invalid workspace identifier.", nameof(workspaceId));
        }
        return $"workspaces/{workspaceId}/{WorkspaceDataFiles.FileName(file)}";
    }

    public string Describe(WorkspaceDataFile file) =>
        $"workspaces/{WorkspaceIdentity.Redact(_workspaceId)}/{WorkspaceDataFiles.FileName(file)}";

    public async Task<bool> ExistsAsync(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default)
    {
        var gate = Gate(file);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await GetCurrentVersionAsync(file, cancellationToken)).Exists;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T?> ReadJsonAsync<T>(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default)
    {
        var gate = Gate(file);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var blob = Blob(file);
            try
            {
                var response = await blob.DownloadContentAsync(cancellationToken);
                _versions[file] = new BlobVersion(true, response.Value.Details.ETag);
                return response.Value.Content.ToObjectFromJson<T>(_jsonOptions);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _versions[file] = new BlobVersion(false, null);
                return default;
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Workspace state document {DocumentName} is not valid JSON for workspace {WorkspaceReference}.",
                    WorkspaceDataFiles.FileName(file),
                    WorkspaceIdentity.Redact(_workspaceId));
                throw new WorkspaceStorageException(
                    "Workspace state is unreadable. The stored document was left untouched.",
                    ex);
            }
            catch (RequestFailedException ex)
            {
                throw StorageFailure("read", file, ex);
            }
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
        var gate = Gate(file);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var version = _versions.TryGetValue(file, out var known)
                ? known
                : await GetCurrentVersionAsync(file, cancellationToken);
            var conditions = version.Exists
                ? new BlobRequestConditions { IfMatch = version.ETag }
                : new BlobRequestConditions { IfNoneMatch = ETag.All };
            var content = BinaryData.FromObjectAsJson(value, _jsonOptions);
            try
            {
                var response = await Blob(file).UploadAsync(
                    content,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        Conditions = conditions
                    },
                    cancellationToken);
                _versions[file] = new BlobVersion(true, response.Value.ETag);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                _versions.TryRemove(file, out _);
                throw new WorkspaceConcurrencyException(
                    "Workspace state changed in another request. Reload the application before saving again.",
                    ex);
            }
            catch (RequestFailedException ex)
            {
                throw StorageFailure("write", file, ex);
            }
        }
        finally
        {
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
                try
                {
                    var response = await Blob(file).DeleteIfExistsAsync(
                        DeleteSnapshotsOption.IncludeSnapshots,
                        cancellationToken: cancellationToken);
                    if (response.Value) deleted++;
                    _versions.TryRemove(file, out _);
                }
                catch (RequestFailedException ex)
                {
                    throw StorageFailure("delete", file, ex);
                }
            }

            return deleted;
        }
        finally
        {
            for (var index = acquired.Count - 1; index >= 0; index--)
            {
                acquired[index].Release();
            }
        }
    }

    private async Task<BlobVersion> GetCurrentVersionAsync(
        WorkspaceDataFile file,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Blob(file).GetPropertiesAsync(cancellationToken: cancellationToken);
            var version = new BlobVersion(true, response.Value.ETag);
            _versions[file] = version;
            return version;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var version = new BlobVersion(false, null);
            _versions[file] = version;
            return version;
        }
        catch (RequestFailedException ex)
        {
            throw StorageFailure("inspect", file, ex);
        }
    }

    private string BlobName(WorkspaceDataFile file) => BuildBlobName(_workspaceId, file);

    private BlobClient Blob(WorkspaceDataFile file) => _container.GetBlobClient(BlobName(file));

    private SemaphoreSlim Gate(WorkspaceDataFile file) =>
        _fileGates.GetOrAdd(file, _ => new SemaphoreSlim(1, 1));

    private WorkspaceStorageException StorageFailure(
        string operation,
        WorkspaceDataFile file,
        RequestFailedException exception)
    {
        _logger.LogError(
            exception,
            "Could not {StorageOperation} {DocumentName} for workspace {WorkspaceReference}.",
            operation,
            WorkspaceDataFiles.FileName(file),
            WorkspaceIdentity.Redact(_workspaceId));
        return new WorkspaceStorageException(
            "Azure workspace storage is temporarily unavailable. No local fallback was used.",
            exception);
    }
}
