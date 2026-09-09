using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace JobSearchManager;

// Read-only packaged reports. Labeling, sampling and scoring never run in the web process.
internal sealed class ConceptEvaluationRuns
{
    private readonly ImmutableArray<JsonElement> _runs;
    private readonly ImmutableArray<string> _errors;
    private readonly int _freshnessDays = 45;
    private readonly string _scoreHash = "";
    private readonly string _metricHash = "";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal ConceptEvaluationRuns(string directory)
    {
        var runs = ImmutableArray.CreateBuilder<JsonElement>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var indexPath = Path.Combine(directory, "index-v2.json");
        if (File.Exists(indexPath))
        {
            try
            {
                using var index = JsonDocument.Parse(File.ReadAllBytes(indexPath));
                var root = index.RootElement;
                if (root.GetProperty("schemaVersion").GetInt32() != 2) throw new InvalidDataException();
                _freshnessDays = root.GetProperty("freshnessDays").GetInt32();
                if (_freshnessDays is < 1 or > 365) throw new InvalidDataException();
                _scoreHash = Hash(File.ReadAllBytes(Path.Combine(directory, "score-policy-v1.json")));
                _metricHash = Hash(File.ReadAllBytes(Path.Combine(directory, "metric-policy-v2.json")));
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in root.GetProperty("runs").EnumerateArray())
                {
                    try
                    {
                        var id = entry.GetProperty("runId").GetString()!;
                        if (!ids.Add(id)) throw new InvalidDataException();
                        var file = entry.GetProperty("file").GetString()!;
                        if (file != Path.GetFileName(file) || file.Contains('/') || file.Contains('\\')) throw new InvalidDataException();
                        var bytes = File.ReadAllBytes(Path.Combine(directory, file));
                        if (Hash(bytes) != entry.GetProperty("sha256").GetString()) throw new InvalidDataException();
                        using var report = JsonDocument.Parse(bytes);
                        Validate(report.RootElement, id);
                        runs.Add(report.RootElement.Clone());
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
                    {
                        errors.Add("A packaged evaluation run failed integrity or format validation and was excluded.");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                runs.Clear();
                errors.Add("The evaluation index could not be validated. Historical references remain available.");
            }
        }
        _runs = runs.OrderByDescending(r => r.GetProperty("completedUtc").GetDateTimeOffset())
            .Select((r, index) => index == 0 ? r : HistoricalSummary(r)).ToImmutableArray();
        _errors = errors.ToImmutable();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // Monthly history must not repeatedly send every old posting excerpt to the browser.
    // Full immutable files remain on disk; the history UI uses summaries and raw micro PR points.
    private static JsonElement HistoricalSummary(JsonElement report) => JsonSerializer.SerializeToElement(new
    {
        schemaVersion = 2, runId = report.GetProperty("runId"), completedUtc = report.GetProperty("completedUtc"),
        authority = report.GetProperty("authority"), summary = report.GetProperty("summary"), microCurve = report.GetProperty("microCurve"),
        technical = new
        {
            referenceHash = report.GetProperty("technical").GetProperty("referenceHash"),
            scoringPolicyHash = report.GetProperty("technical").GetProperty("scoringPolicyHash"),
            metricPolicyHash = report.GetProperty("technical").GetProperty("metricPolicyHash")
        }
    }, Json);

    internal static void Validate(JsonElement report, string id)
    {
        if (report.GetProperty("schemaVersion").GetInt32() != 2 || report.GetProperty("runId").GetString() != id)
            throw new InvalidDataException();
        if (report.GetProperty("completedUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Future evaluation date.");
        var s = report.GetProperty("summary");
        var count = s.GetProperty("postings").GetInt32();
        var concepts = report.GetProperty("perConcept").EnumerateArray().ToArray();
        if (count < 1 || concepts.Length < 1 || concepts.Select(c => c.GetProperty("id").GetString()).Distinct().Count() != concepts.Length ||
            s.GetProperty("totalPossible").GetInt32() != count * concepts.Length ||
            s.GetProperty("resolved").GetInt32() < 0 || s.GetProperty("unresolved").GetInt32() < 0 ||
            s.GetProperty("resolved").GetInt32() + s.GetProperty("unresolved").GetInt32() != count * concepts.Length)
            throw new InvalidDataException("Incomplete evaluation matrix.");
        foreach (var metric in concepts.Append(s.GetProperty("micro")))
        {
            foreach (var key in new[] { "tp", "fp", "fn", "tn", "support", "negativeSupport" })
                if (metric.GetProperty(key).GetInt32() < 0) throw new InvalidDataException();
            foreach (var key in new[] { "precision", "recall", "f1" })
                if (metric.GetProperty(key).GetDouble() is not (>= 0 and <= 1)) throw new InvalidDataException();
            var tp = metric.GetProperty("tp").GetInt32(); var fp = metric.GetProperty("fp").GetInt32();
            var fn = metric.GetProperty("fn").GetInt32(); var tn = metric.GetProperty("tn").GetInt32();
            if (metric.GetProperty("support").GetInt32() != tp + fn || metric.GetProperty("negativeSupport").GetInt32() != tn + fp ||
                Math.Abs(metric.GetProperty("precision").GetDouble() - (tp + fp == 0 ? 0 : (double)tp / (tp + fp))) > 1e-12 ||
                Math.Abs(metric.GetProperty("recall").GetDouble() - (tp + fn == 0 ? 0 : (double)tp / (tp + fn))) > 1e-12 ||
                Math.Abs(metric.GetProperty("f1").GetDouble() - (2 * tp + fp + fn == 0 ? 0 : (double)(2 * tp) / (2 * tp + fp + fn))) > 1e-12)
                throw new InvalidDataException("Confusion counts and metrics disagree.");
        }
        var micro = s.GetProperty("micro");
        foreach (var key in new[] { "tp", "fp", "fn", "tn" })
            if (concepts.Sum(c => c.GetProperty(key).GetInt32()) != micro.GetProperty(key).GetInt32()) throw new InvalidDataException();
        if (new[] { "tp", "fp", "fn", "tn" }.Sum(k => micro.GetProperty(k).GetInt32()) != s.GetProperty("resolved").GetInt32()) throw new InvalidDataException();
        foreach (var key in new[] { "precision", "recall", "f1" })
            if (Math.Abs(s.GetProperty("macro").GetProperty(key).GetDouble() - concepts.Average(c => c.GetProperty(key).GetDouble())) > 1e-12) throw new InvalidDataException();
        ValidateCurve(report.GetProperty("microCurve"), micro.GetProperty("support").GetInt32(), micro.GetProperty("negativeSupport").GetInt32());
        foreach (var concept in concepts)
            if (concept.TryGetProperty("curve", out var curve) && curve.ValueKind != JsonValueKind.Null)
                ValidateCurve(curve, concept.GetProperty("support").GetInt32(), concept.GetProperty("negativeSupport").GetInt32());
        _ = report.GetProperty("authority").GetProperty("pipelineFingerprint").GetString();
        _ = report.GetProperty("authority").GetProperty("byteHash").GetString();
        _ = report.GetProperty("authority").GetProperty("taxonomyIdentity").GetProperty("sha256").GetString();
        _ = report.GetProperty("technical").GetProperty("referenceHash").GetString();
        _ = report.GetProperty("technical").GetProperty("scoringPolicyHash").GetString();
        _ = report.GetProperty("technical").GetProperty("metricPolicyHash").GetString();
    }

    private static void ValidateCurve(JsonElement curve, int positives, int negatives)
    {
        var points = curve.GetProperty("points").EnumerateArray().ToArray();
        if (points.Length == 0) throw new InvalidDataException("Missing PR thresholds.");
        var priorThreshold = double.PositiveInfinity;
        var priorTp = 0; var priorFp = 0; var priorRecall = 0.0; var ap = 0.0;
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var tp = point.GetProperty("tp").GetInt32(); var fp = point.GetProperty("fp").GetInt32();
            var fn = point.GetProperty("fn").GetInt32(); var tn = point.GetProperty("tn").GetInt32();
            if (tp < priorTp || fp < priorFp || fn < 0 || tn < 0 || tp + fn != positives || fp + tn != negatives) throw new InvalidDataException();
            if (i == 0)
            {
                if (point.GetProperty("threshold").ValueKind != JsonValueKind.Null || tp != 0 || fp != 0) throw new InvalidDataException();
            }
            else
            {
                var threshold = point.GetProperty("threshold").GetDouble();
                if (threshold is not (>= 0 and <= 1) || threshold >= priorThreshold || tp + fp <= priorTp + priorFp) throw new InvalidDataException();
                priorThreshold = threshold;
            }
            var precision = i == 0 ? 1 : (double)tp / (tp + fp);
            var recall = positives == 0 ? 0 : (double)tp / positives;
            if (Math.Abs(point.GetProperty("precision").GetDouble() - precision) > 1e-12 || Math.Abs(point.GetProperty("recall").GetDouble() - recall) > 1e-12) throw new InvalidDataException();
            ap += (recall - priorRecall) * precision;
            priorRecall = recall; priorTp = tp; priorFp = fp;
        }
        if (priorTp != positives || priorFp != negatives) throw new InvalidDataException("PR sweep is incomplete.");
        var averagePrecision = curve.GetProperty("averagePrecision");
        if (positives == 0 ? averagePrecision.ValueKind != JsonValueKind.Null : Math.Abs(averagePrecision.GetDouble() - ap) > 1e-12)
            throw new InvalidDataException("Average precision disagrees with the raw PR points.");
    }

    internal static string Status(JsonElement report, JsonElement authority, DateTimeOffset now, int freshnessDays, string scoreHash, string metricHash)
    {
        var age = now - report.GetProperty("completedUtc").GetDateTimeOffset();
        var identity = report.GetProperty("authority");
        var technical = report.GetProperty("technical");
        return age >= TimeSpan.Zero && age <= TimeSpan.FromDays(freshnessDays) &&
            identity.GetProperty("pipelineFingerprint").GetString() == authority.GetProperty("pipelineFingerprint").GetString() &&
            identity.GetProperty("byteHash").GetString() == authority.GetProperty("byteHash").GetString() &&
            identity.GetProperty("taxonomyIdentity").GetProperty("sha256").GetString() == authority.GetProperty("taxonomyIdentity").GetProperty("sha256").GetString() &&
            technical.GetProperty("scoringPolicyHash").GetString() == scoreHash && technical.GetProperty("metricPolicyHash").GetString() == metricHash
            ? "CURRENT" : "STALE";
    }

    internal object View(ConceptRuleSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        var authority = JsonSerializer.SerializeToElement(snapshot.Identity, Json);
        var runs = _runs.Select(r => new
        {
            status = Status(r, authority, now, _freshnessDays, _scoreHash, _metricHash),
            ageDays = Math.Max(0, (int)(now - r.GetProperty("completedUtc").GetDateTimeOffset()).TotalDays),
            artifact = r
        }).ToArray();
        return new { latest = runs.FirstOrDefault(), previous = runs.Skip(1).FirstOrDefault(), history = runs.Skip(1).ToArray(), freshnessDays = _freshnessDays, errors = _errors };
    }
}
