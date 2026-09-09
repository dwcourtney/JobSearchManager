using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobSearchManager;

// Packaged evidence only. No runtime evaluator, database, model, or mutable ledger.
internal sealed class ConceptEvaluationReports
{
    internal const string MetricImplementationHash = "ff6f7dcb82a9958d23dd2e47d0a14f95d5907ec2e3c60cc2eb2a19d038051810";
    private readonly ConceptEvaluationRuns _runs;
    private readonly ImmutableArray<(string Role, string Dataset, string Reference, JsonElement Report)> _reports;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal ConceptEvaluationReports(string directory)
    {
        _runs = new ConceptEvaluationRuns(directory);
        using var index = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "index-v1.json")));
        if (index.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported concept evaluation index.");
        var reports = ImmutableArray.CreateBuilder<(string, string, string, JsonElement)>();
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in index.RootElement.GetProperty("reports").EnumerateArray())
        {
            var role = entry.GetProperty("role").GetString()!;
            if (!roles.Add(role) || role is not ("curated" or "holdout" or "historical"))
                throw new InvalidDataException("Invalid/duplicate evaluation role.");
            var file = entry.GetProperty("file").GetString()!;
            if (file != Path.GetFileName(file) || file.Contains('/') || file.Contains('\\'))
                throw new InvalidDataException("Invalid evaluation artifact path.");
            var bytes = File.ReadAllBytes(Path.Combine(directory, file));
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != entry.GetProperty("sha256").GetString())
                throw new InvalidDataException("Concept evaluation artifact checksum mismatch.");
            using var document = JsonDocument.Parse(bytes);
            var report = document.RootElement;
            if (report.GetProperty("schemaVersion").GetInt32() != 1 || report.GetProperty("role").GetString() != role)
                throw new InvalidDataException("Invalid concept evaluation report.");
            var dataset = role == "curated" ? Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RegexValidationCorpus.json")).Replace("\r\n", "\n", StringComparison.Ordinal)))) : entry.GetProperty("datasetFingerprint").GetString()!;
            reports.Add((role, dataset,
                entry.GetProperty("referenceFingerprint").GetString()!, report.Clone()));
        }
        if (!roles.Contains("curated") || !roles.Contains("holdout"))
            throw new InvalidDataException("Missing curated/holdout evaluation evidence.");
        _reports = reports.ToImmutable();
    }

    internal object View(ConceptRuleSnapshot snapshot) => new
    {
        evaluation = _runs.View(snapshot),
        reports = _reports.Select(item => new
        {
            role = item.Role,
            status = Status(item.Report, item.Role, item.Dataset, item.Reference, snapshot),
            artifact = item.Report
        }).ToArray()
    };

    internal static string Status(JsonElement report, string role, string dataset, string reference,
        ConceptRuleSnapshot snapshot)
    {
        if (role == "historical") return "HISTORICAL";
        var identity = JsonSerializer.SerializeToNode(snapshot.Identity, Json);
        return JsonNode.DeepEquals(JsonNode.Parse(report.GetProperty("authority").GetRawText()), identity) &&
            report.GetProperty("datasetFingerprint").GetString() == dataset &&
            report.GetProperty("referenceFingerprint").GetString() == reference &&
            report.GetProperty("metricImplementationHash").GetString() == MetricImplementationHash
                ? "CURRENT" : "STALE";
    }
}
