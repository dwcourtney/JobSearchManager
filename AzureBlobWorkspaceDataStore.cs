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
    private readonly JsonSerializerOptions _compactJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BlobVersion> _versions = new(StringComparer.Ordinal);

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
    public WorkspaceStorageDiagnostics Diagnostics { get; } = new();

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

    public static string BuildCompanyCacheBlobName(
        string workspaceId,
        string companyId,
        string queryFingerprint)
    {
        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new ArgumentException("Invalid workspace identifier.", nameof(workspaceId));
        }
        return WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint);
    }

    public static string BuildSharedCompanyCacheBlobName(
        string companyId,
        string queryFingerprint) =>
        WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint);

    public string DescribeCompanyCache(string companyId, string queryFingerprint) =>
        WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint);

    public string DescribeSourceStatus(string companyId, string queryFingerprint) =>
        WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint);

    public async Task<bool> ExistsAsync(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default) =>
        await ExistsRelativeAsync(WorkspaceDataFiles.FileName(file), cancellationToken);

    public async Task<bool> CompanyCacheExistsAsync(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        await ExistsRelativeAsync(
            WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint), cancellationToken,
            shared: true);

    private async Task<bool> ExistsRelativeAsync(
        string relativePath,
        CancellationToken cancellationToken,
        bool shared = false)
    {
        var storageKey = StorageKey(relativePath, shared);
        var gate = Gate(storageKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await GetCurrentVersionAsync(relativePath, cancellationToken, shared)).Exists;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<T?> ReadJsonAsync<T>(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(WorkspaceDataFiles.FileName(file), cancellationToken);

    public async Task<T?> ReadCompanyCacheJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(
            WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint), cancellationToken,
            shared: true);

    public async Task<T?> ReadSourceStatusJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        CancellationToken cancellationToken = default) =>
        await ReadRelativeJsonAsync<T>(
            WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint), cancellationToken,
            shared: true);

    private async Task<T?> ReadRelativeJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken,
        bool shared = false)
    {
        var storageKey = StorageKey(relativePath, shared);
        var gate = Gate(storageKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var blob = Blob(relativePath, shared);
            try
            {
                var response = await blob.DownloadContentAsync(cancellationToken);
                _versions[storageKey] = new BlobVersion(true, response.Value.Details.ETag);
                var result = response.Value.Content.ToObjectFromJson<T>(_jsonOptions);
                Diagnostics.RecordRead();
                return result;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                _versions[storageKey] = new BlobVersion(false, null);
                return default;
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Workspace state document {DocumentName} is not valid JSON for workspace {WorkspaceReference}.",
                    relativePath,
                    WorkspaceIdentity.Redact(_workspaceId));
                throw new WorkspaceStorageException(
                    "Workspace state is unreadable. The stored document was left untouched.",
                    ex);
            }
            catch (RequestFailedException ex)
            {
                throw StorageFailure("read", relativePath, ex);
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
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(WorkspaceDataFiles.FileName(file), value, cancellationToken);

    public async Task WriteCompanyCacheJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        T value,
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(
            WorkspaceDataFiles.CompanyCacheRelativePath(companyId, queryFingerprint), value, cancellationToken,
            shared: true);

    public async Task WriteSourceStatusJsonAsync<T>(
        string companyId,
        string queryFingerprint,
        T value,
        CancellationToken cancellationToken = default) =>
        await WriteRelativeJsonAsync(
            WorkspaceDataFiles.SourceStatusRelativePath(companyId, queryFingerprint), value, cancellationToken,
            shared: true);

    private async Task WriteRelativeJsonAsync<T>(
        string relativePath,
        T value,
        CancellationToken cancellationToken,
        bool shared = false)
    {
        var storageKey = StorageKey(relativePath, shared);
        var gate = Gate(storageKey);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var version = _versions.TryGetValue(storageKey, out var known)
                ? known
                : await GetCurrentVersionAsync(relativePath, cancellationToken, shared);
            var conditions = version.Exists
                ? new BlobRequestConditions { IfMatch = version.ETag }
                : new BlobRequestConditions { IfNoneMatch = ETag.All };
            var content = BinaryData.FromObjectAsJson(
                value,
                IsCompactDocument(relativePath) ? _compactJsonOptions : _jsonOptions);
            try
            {
                var response = await Blob(relativePath, shared).UploadAsync(
                    content,
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                        Conditions = conditions
                    },
                    cancellationToken);
                _versions[storageKey] = new BlobVersion(true, response.Value.ETag);
                Diagnostics.RecordWrite(relativePath, content.ToMemory().Length);
                _logger.LogInformation(
                    "Wrote {StorageBytes} bytes to workspace document {DocumentName}.",
                    content.ToMemory().Length,
                    relativePath);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                _versions.TryRemove(storageKey, out _);
                throw new WorkspaceConcurrencyException(
                    "Workspace state changed in another request. Reload the application before saving again.",
                    ex);
            }
            catch (RequestFailedException ex)
            {
                throw StorageFailure("write", relativePath, ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsCompactDocument(string relativePath) =>
        relativePath.StartsWith("shared/job-caches/", StringComparison.Ordinal) ||
        relativePath.StartsWith("shared/source-status/", StringComparison.Ordinal);

    public async Task<bool> DeleteAsync(
        WorkspaceDataFile file,
        CancellationToken cancellationToken = default)
    {
        var relativePath = WorkspaceDataFiles.FileName(file);
        var gate = Gate(relativePath);
        await gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var response = await Blob(relativePath).DeleteIfExistsAsync(
                    DeleteSnapshotsOption.IncludeSnapshots,
                    cancellationToken: cancellationToken);
                _versions.TryRemove(relativePath, out _);
                if (response.Value) Diagnostics.RecordDelete();
                return response.Value;
            }
            catch (RequestFailedException ex)
            {
                throw StorageFailure("delete", relativePath, ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        var relativePaths = new[] { WorkspaceDataFile.Settings, WorkspaceDataFile.JobHistory }
            .Select(WorkspaceDataFiles.FileName)
            .ToList();
        relativePaths.Sort(StringComparer.Ordinal);
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
                try
                {
                    var response = await Blob(relativePath).DeleteIfExistsAsync(
                        DeleteSnapshotsOption.IncludeSnapshots,
                        cancellationToken: cancellationToken);
                    if (response.Value) deleted++;
                    if (response.Value) Diagnostics.RecordDelete();
                    _versions.TryRemove(relativePath, out _);
                }
                catch (RequestFailedException ex)
                {
                    throw StorageFailure("delete", relativePath, ex);
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
        string relativePath,
        CancellationToken cancellationToken,
        bool shared = false)
    {
        var storageKey = StorageKey(relativePath, shared);
        try
        {
            var response = await Blob(relativePath, shared).GetPropertiesAsync(cancellationToken: cancellationToken);
            var version = new BlobVersion(true, response.Value.ETag);
            _versions[storageKey] = version;
            return version;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var version = new BlobVersion(false, null);
            _versions[storageKey] = version;
            return version;
        }
        catch (RequestFailedException ex)
        {
            throw StorageFailure("inspect", relativePath, ex);
        }
    }

    private static string BuildScopedBlobName(string workspaceId, string relativePath)
    {
        if (!WorkspaceIdentity.IsValid(workspaceId))
        {
            throw new ArgumentException("Invalid workspace identifier.", nameof(workspaceId));
        }
        return $"workspaces/{workspaceId}/{relativePath}";
    }

    private BlobClient Blob(string relativePath, bool shared = false) =>
        _container.GetBlobClient(shared
            ? relativePath
            : BuildScopedBlobName(_workspaceId, relativePath));

    private static string StorageKey(string relativePath, bool shared) =>
        shared ? $"shared::{relativePath}" : $"workspace::{relativePath}";

    private SemaphoreSlim Gate(string relativePath) =>
        _fileGates.GetOrAdd(relativePath, _ => new SemaphoreSlim(1, 1));

    private WorkspaceStorageException StorageFailure(
        string operation,
        string relativePath,
        RequestFailedException exception)
    {
        _logger.LogError(
            exception,
            "Could not {StorageOperation} {DocumentName} for workspace {WorkspaceReference}.",
            operation,
            relativePath,
            WorkspaceIdentity.Redact(_workspaceId));
        return new WorkspaceStorageException(
            "Azure workspace storage is temporarily unavailable. No local fallback was used.",
            exception);
    }
}
