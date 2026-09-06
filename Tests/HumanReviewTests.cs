using System.Security.Cryptography;
using System.Text.Json;
using JobSearchManager;

internal static class HumanReviewTests
{
    public static Task Run()
    {
        var root = Directory.GetCurrentDirectory();
        var source = Path.Combine(root, "Tests/cheap_reject_evaluation/human_review_v1");
        var queue = Path.Combine(AppContext.BaseDirectory, "CheapTriage/review-queues/human-review-v1.json");
        var rules = Path.Combine(AppContext.BaseDirectory, CheapRejectRules.DefaultPath);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var queueHash = Hash(queue); var rulesHash = Hash(rules); var machineHash = Hash(Path.Combine(source, "queue.csv"));
        var dir = Path.Combine(Path.GetTempPath(), "jsm-human-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = Path.Combine(dir, "reviews.db"); var store = new CheapTriageHumanReview(queue, db);
            var blank = store.Read();
            Check(blank.Cases.Length == 29 && blank.Reviewed == 0 && blank.Remaining == 29 && !blank.Complete, "Initial blank queue");
            using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "excerpt-provenance.json")));
            var expected = provenance.RootElement.EnumerateArray().Select(x => x.GetProperty("stableId").GetString()).ToHashSet();
            Check(expected.SetEquals(blank.Cases.Select(x => x.GetProperty("stableJobId").GetString())), "Exact source 29 IDs");
            Check(blank.SourceManifestHash == Hash(Path.Combine(source, "manifest.json")), "Frozen source provenance");
            var first = blank.Cases[0].GetProperty("stableJobId").GetString()!;
            store.Save(new(store.QueueFingerprint, first, "KEEP", "technical duties", 0), "reviewer-one");
            var reopened = new CheapTriageHumanReview(queue, db); var saved = reopened.Read();
            Check(saved.Reviews[first].Decision == "KEEP" && saved.Reviewed == 1 && saved.Remaining == 28, "Durability/progress");
            Check(saved.Reviews[first].Reviewer == "reviewer-one" && DateTimeOffset.TryParse(saved.Reviews[first].ReviewedAtUtc, out _), "Reviewer/time provenance");
            Throws<InvalidOperationException>(() => reopened.Save(new(store.QueueFingerprint, first, "REJECT", "", 0), "other"));
            store.Save(new(store.QueueFingerprint, first, "AMBIGUOUS", "needs context", saved.Reviews[first].Revision), "reviewer-two");
            Check(store.Read().Reviews[first].Decision == "AMBIGUOUS" && store.Read().Revisions.Count == 2, "Revision history and latest selection");
            foreach (var c in blank.Cases.Skip(1))
                store.Save(new(store.QueueFingerprint, c.GetProperty("stableJobId").GetString()!, "REJECT", "reviewed", 0), "reviewer-one");
            var complete = store.Read();
            var backupPath = Path.Combine(dir, "backup.db");
            var sourceHash = Hash(db);
            CheapTriageHumanReview.Backup(db, backupPath);
            var backup = new CheapTriageHumanReview(queue, backupPath).Read();
            Check(backup.Complete && backup.Revisions.SequenceEqual(complete.Revisions), "Backup restores all human revisions and provenance");
            Check(Hash(db) == sourceHash, "Backup does not mutate source");
            Throws<IOException>(() => CheapTriageHumanReview.Backup(db, backupPath));
            Check(complete.Complete && complete.Remaining == 0 && complete.Counts["AMBIGUOUS"] == 1 && complete.Counts["REJECT"] == 28, "Completion summary");
            var export = JsonSerializer.Serialize(complete, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Check(export.Contains(store.QueueFingerprint) && export.Contains("needs context") && export.Contains("reviewer-two") && !complete.RulesChanged, "Export provenance and decisions");
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, "missing", "KEEP", "", 0), "reviewer"));
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, first, "ACTIVE", "", 0), "reviewer"));
            Throws<ArgumentException>(() => store.Save(new(store.QueueFingerprint, first, "KEEP", new string('x',2001), 0), "reviewer"));
            Throws<InvalidOperationException>(() => store.Save(new("stale", first, "KEEP", "", 0), "reviewer"));
            var changedQueue = Path.Combine(dir,"changed.json"); File.WriteAllText(changedQueue, File.ReadAllText(queue)+"\n");
            Check(new CheapTriageHumanReview(changedQueue, db).Read().Reviewed == 0, "Changed queue cannot inherit old labels");
            Check(Hash(queue)==queueHash && Hash(rules)==rulesHash && Hash(Path.Combine(source,"queue.csv"))==machineHash, "Queue/machine labels/rules immutable");
        }
        finally { Directory.Delete(dir, true); }
        return Task.CompletedTask;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
}
