using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearchManager;

internal static class ConceptEvaluationRunTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jsm-evaluation-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var snapshot = ConceptRuleSnapshot.Load(Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json"), JobConceptCatalog.LoadDefault());
            var authority = JsonSerializer.SerializeToElement(snapshot.Identity, Json);
            var metric = new { tp = 1, fp = 0, fn = 0, tn = 1, support = 1, negativeSupport = 1, precision = 1, recall = 1, f1 = 1 };
            var time = DateTimeOffset.UtcNow.AddDays(-1);
            var scoreBytes = "score-policy"u8.ToArray(); var metricBytes = "metric-policy"u8.ToArray();
            var scoreHash = Convert.ToHexStringLower(SHA256.HashData(scoreBytes));
            var metricHash = Convert.ToHexStringLower(SHA256.HashData(metricBytes));
            var report = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 2, runId = "test-current", completedUtc = time, authority,
                summary = new { postings = 2, totalPossible = 2, resolved = 2, unresolved = 0, micro = metric, macro = new { precision = 1, recall = 1, f1 = 1 } },
                perConcept = new[] { new { id = "test", metric.tp, metric.fp, metric.fn, metric.tn, metric.support, metric.negativeSupport, metric.precision, metric.recall, metric.f1 } },
                microCurve = new { averagePrecision = 1, points = new[] {
                    new { threshold = (double?)null, tp = 0, fp = 0, fn = 1, tn = 1, precision = 1.0, recall = 0.0 },
                    new { threshold = (double?).5, tp = 1, fp = 0, fn = 0, tn = 1, precision = 1.0, recall = 1.0 },
                    new { threshold = (double?)0, tp = 1, fp = 1, fn = 0, tn = 0, precision = .5, recall = 1.0 } } },
                technical = new { referenceHash = "frozen", scoringPolicyHash = scoreHash, metricPolicyHash = metricHash }
            }, Json)!;
            var element = JsonSerializer.SerializeToElement(report);
            ConceptEvaluationRuns.Validate(element, "test-current");
            var invalid = report.DeepClone(); invalid["microCurve"]!["averagePrecision"] = .1;
            try { ConceptEvaluationRuns.Validate(JsonSerializer.SerializeToElement(invalid), "test-current"); throw new Exception("Invalid AP accepted"); }
            catch (InvalidDataException) { }
            if (ConceptEvaluationRuns.Status(element, authority, time.AddDays(45), 45, scoreHash, metricHash) != "CURRENT") throw new Exception("Inclusive freshness boundary");
            if (ConceptEvaluationRuns.Status(element, authority, time.AddDays(46), 45, scoreHash, metricHash) != "STALE") throw new Exception("Age not stale");
            if (ConceptEvaluationRuns.Status(element, authority, time.AddDays(1), 45, "different", metricHash) != "STALE") throw new Exception("Policy change not stale");
            var changed = JsonNode.Parse(authority.GetRawText())!; changed["pipelineFingerprint"] = "different";
            if (ConceptEvaluationRuns.Status(element, JsonSerializer.SerializeToElement(changed), time.AddDays(1), 45, scoreHash, metricHash) != "STALE") throw new Exception("Pipeline change not stale");
            File.WriteAllBytes(Path.Combine(directory, "score-policy-v1.json"), scoreBytes);
            File.WriteAllBytes(Path.Combine(directory, "metric-policy-v2.json"), metricBytes);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report);
            File.WriteAllBytes(Path.Combine(directory, "current.json"), bytes);
            var previous = report.DeepClone(); previous["runId"] = "test-previous"; previous["completedUtc"] = time.AddDays(-31);
            var previousBytes = JsonSerializer.SerializeToUtf8Bytes(previous);
            File.WriteAllBytes(Path.Combine(directory, "previous.json"), previousBytes);
            File.WriteAllText(Path.Combine(directory, "index-v2.json"), JsonSerializer.Serialize(new { schemaVersion = 2, freshnessDays = 45, runs = new[] {
                new { runId = "test-previous", file = "previous.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(previousBytes)) },
                new { runId = "test-current", file = "current.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } } }));
            var view = JsonSerializer.SerializeToElement(new ConceptEvaluationRuns(directory).View(snapshot), Json);
            if (view.GetProperty("latest").GetProperty("artifact").GetProperty("runId").GetString() != "test-current" ||
                view.GetProperty("previous").GetProperty("artifact").GetProperty("runId").GetString() != "test-previous") throw new Exception("Latest/previous ordering");
            if (view.GetProperty("previous").GetProperty("artifact").TryGetProperty("perConcept", out _)) throw new Exception("History should use compact summaries, not full old payloads");
            File.AppendAllText(Path.Combine(directory, "current.json"), " ");
            view = JsonSerializer.SerializeToElement(new ConceptEvaluationRuns(directory).View(snapshot), Json);
            if (view.GetProperty("latest").GetProperty("artifact").GetProperty("runId").GetString() != "test-previous" || view.GetProperty("errors").GetArrayLength() != 1) throw new Exception("Tampered report not excluded");
            if (!File.ReadAllBytes(Path.Combine(directory, "previous.json")).SequenceEqual(previousBytes)) throw new Exception("Historical artifact changed");
        }
        finally { Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }
}
