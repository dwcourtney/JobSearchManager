using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JobSearchManager;

public sealed record HumanReviewQueue(string QueueVersion, string SourceManifestHash, JsonElement[] Cases);
public sealed record HumanAdjudication(long Revision, string StableJobId, string Decision, string Note,
    string Reviewer, string ReviewedAtUtc);
public sealed record HumanReviewSave(string QueueFingerprint, string StableJobId, string Decision,
    string? Note, long ExpectedRevision);
public sealed record HumanReviewReport(string QueueVersion, string SourceManifestHash, string QueueFingerprint,
    JsonElement[] Cases, IReadOnlyDictionary<string, HumanAdjudication> Reviews,
    IReadOnlyList<HumanAdjudication> Revisions, int Reviewed, int Remaining,
    IReadOnlyDictionary<string, int> Counts, bool Complete, bool RulesChanged = false)
{
    public string CategoryName { get; init; } = "Technology";
}

/// <summary>Global administrator adjudications, separate from every production classifier/cache.</summary>
public sealed class CheapTriageHumanReview
{
    public static void Backup(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(sourcePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open();
        // Never overwrite a previous backup or initialize/migrate the source database.
        using (File.Open(destinationPath, FileMode.CreateNew, FileAccess.Write)) { }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(destinationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(destinationPath), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
        using var check = destination.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Human review backup failed integrity verification.");
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string categoryName = "Technology";
    private readonly HumanReviewQueue queue;
    private readonly object queueGate = new();
    private readonly string originalQueuePath;
    private readonly string databaseFile;
    private string? queueDirectory;
    private CheapTriageHumanReview? active;
    internal string? ActiveQueueDirectory => queueDirectory;
    private readonly HashSet<string> ids;
    private readonly string connectionString;
    public string QueueFingerprint { get; }

    public CheapTriageHumanReview(IConfiguration configuration, IHostEnvironment environment, HostingConfiguration hosting)
        : this(Path.Combine(environment.ContentRootPath, "CheapTriage", "review-queues", "human-review-v1.json"),
            configuration["HumanReview:DatabasePath"] ?? (hosting.IsContainer ? "/app/data/cheap-triage-human-review.db" :
                Path.Combine(environment.ContentRootPath, "data", "cheap-triage-human-review.db")))
    {
        queueDirectory = configuration["HumanReview:QueueDirectory"] ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databaseFile))!, "cheap-triage-review-queues");
        categoryName = configuration["HumanReview:CategoryName"]?.Trim() is { Length: > 0 } name ? name : "Technology";
    }

    internal CheapTriageHumanReview(string queuePath, string databasePath, string? dynamicQueueDirectory = null)
    {
        originalQueuePath = queuePath; databaseFile = databasePath; queueDirectory = dynamicQueueDirectory;
        var bytes = File.ReadAllBytes(queuePath);
        QueueFingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        queue = JsonSerializer.Deserialize<HumanReviewQueue>(bytes, Json) ?? throw new InvalidDataException("Missing review queue.");
        if (string.IsNullOrWhiteSpace(queue.QueueVersion) || queue.SourceManifestHash?.Length != 64 || queue.Cases is not { Length: > 0 })
            throw new InvalidDataException("Invalid review queue provenance.");
        ids = new(StringComparer.Ordinal);
        foreach (var item in queue.Cases)
        {
            var id = item.GetProperty("stableJobId").GetString();
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || item.GetProperty("currentDecision").GetString() is not ("REJECT" or "UNDETERMINED"))
                throw new InvalidDataException("Invalid or duplicate review job.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS HumanReviewRevisions (
                Revision INTEGER PRIMARY KEY AUTOINCREMENT,
                QueueFingerprint TEXT NOT NULL, QueueVersion TEXT NOT NULL, SourceManifestHash TEXT NOT NULL,
                StableJobId TEXT NOT NULL, Decision TEXT NOT NULL CHECK(Decision IN ('KEEP','REJECT','AMBIGUOUS')),
                Note TEXT NOT NULL, Reviewer TEXT NOT NULL, ReviewedAtUtc TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS HumanReviewQueueIndex ON HumanReviewRevisions(QueueFingerprint,StableJobId,Revision);
            """;
        command.ExecuteNonQuery();
    }

    // Queue publication is atomic. Existing queue bytes and review revisions are never rewritten.
    private void RefreshActive()
    {
        if (queueDirectory is null || !File.Exists(Path.Combine(queueDirectory, "active.json"))) return;
        var hash = JsonSerializer.Deserialize<string>(File.ReadAllBytes(Path.Combine(queueDirectory, "active.json")), Json)!;
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid active queue identity.");
        if (active?.QueueFingerprint == hash) return;
        var next = new CheapTriageHumanReview(Path.Combine(queueDirectory, hash + ".json"), databaseFile);
        if (next.QueueFingerprint != hash) throw new InvalidDataException("Active queue hash mismatch.");
        active = next;
    }
    public HumanReviewReport Read()
    {
        lock (queueGate) { RefreshActive(); return (active?.ReadCurrent() ?? ReadCurrent()) with { CategoryName = categoryName }; }
    }
    public void Save(HumanReviewSave request, string reviewer)
    {
        lock (queueGate) { RefreshActive(); if (active is null) SaveCurrent(request, reviewer); else active.SaveCurrent(request, reviewer); }
    }
    internal HumanReviewReport[] History()
    {
        lock (queueGate)
        {
            var reports = new List<HumanReviewReport> { ReadCurrent() };
            if (queueDirectory is not null && Directory.Exists(queueDirectory))
                foreach (var path in Directory.EnumerateFiles(queueDirectory, "*.json").Where(p => Path.GetFileNameWithoutExtension(p).Length == 64).Order(StringComparer.Ordinal))
                    reports.Add(new CheapTriageHumanReview(path, databaseFile).ReadCurrent());
            return reports.ToArray();
        }
    }
    internal string Publish(HumanReviewQueue next, Func<HumanReviewReport, bool> stillEligible)
    {
        lock (queueGate)
        {
            RefreshActive();
            if (queueDirectory is null || !stillEligible(active?.ReadCurrent() ?? ReadCurrent())) throw new InvalidOperationException("Maintenance cycle changed during detection.");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json); var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Directory.CreateDirectory(queueDirectory); var path = Path.Combine(queueDirectory, hash + ".json");
            CheapTriageMaintenanceDetector.WriteImmutable(path, bytes);
            var temporary = Path.Combine(queueDirectory, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(hash, Json));
                File.Move(temporary, Path.Combine(queueDirectory, "active.json"), true); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
            RefreshActive(); return hash;
        }
    }

    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }

    private HumanReviewReport ReadCurrent()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Revision,StableJobId,Decision,Note,Reviewer,ReviewedAtUtc FROM HumanReviewRevisions WHERE QueueFingerprint=$hash ORDER BY Revision";
        command.Parameters.AddWithValue("$hash", QueueFingerprint);
        using var reader = command.ExecuteReader();
        var revisions = new List<HumanAdjudication>();
        while (reader.Read())
            if (ids.Contains(reader.GetString(1))) revisions.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        var latest = revisions.GroupBy(x => x.StableJobId).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var counts = new[] { "KEEP", "REJECT", "AMBIGUOUS" }.ToDictionary(x => x, x => latest.Values.Count(r => r.Decision == x));
        return new(queue.QueueVersion, queue.SourceManifestHash, QueueFingerprint, queue.Cases, latest, revisions,
            latest.Count, ids.Count - latest.Count, counts, latest.Count == ids.Count) { CategoryName = categoryName };
    }

    private void SaveCurrent(HumanReviewSave request, string reviewer)
    {
        if (request.QueueFingerprint != QueueFingerprint) throw new InvalidOperationException("Queue changed. Reload before reviewing.");
        if (!ids.Contains(request.StableJobId ?? "")) throw new ArgumentException("Unknown review job.");
        if (request.Decision is not ("KEEP" or "REJECT" or "AMBIGUOUS") || (request.Note?.Length ?? 0) > 2000 ||
            string.IsNullOrWhiteSpace(reviewer) || request.ExpectedRevision < 0)
            throw new ArgumentException("Choose KEEP, REJECT or AMBIGUOUS and a note of at most 2000 characters.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand(); check.Transaction = transaction;
        check.CommandText = "SELECT COALESCE(MAX(Revision),0) FROM HumanReviewRevisions WHERE QueueFingerprint=$hash AND StableJobId=$id";
        check.Parameters.AddWithValue("$hash", QueueFingerprint); check.Parameters.AddWithValue("$id", request.StableJobId);
        if (Convert.ToInt64(check.ExecuteScalar()) != request.ExpectedRevision)
            throw new InvalidOperationException("This review was changed in another session. Reload before saving.");
        using var write = connection.CreateCommand(); write.Transaction = transaction;
        write.CommandText = """
            INSERT INTO HumanReviewRevisions (QueueFingerprint,QueueVersion,SourceManifestHash,StableJobId,Decision,Note,Reviewer,ReviewedAtUtc)
            VALUES ($hash,$version,$manifest,$id,$decision,$note,$reviewer,$utc)
            """;
        write.Parameters.AddWithValue("$hash", QueueFingerprint); write.Parameters.AddWithValue("$version", queue.QueueVersion);
        write.Parameters.AddWithValue("$manifest", queue.SourceManifestHash); write.Parameters.AddWithValue("$id", request.StableJobId);
        write.Parameters.AddWithValue("$decision", request.Decision); write.Parameters.AddWithValue("$note", request.Note ?? "");
        write.Parameters.AddWithValue("$reviewer", reviewer); write.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        write.ExecuteNonQuery(); transaction.Commit();
    }
}
