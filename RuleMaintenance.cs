using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record RuleMaintenancePrompt(string TemplateVersion, string RulesetVersion,
    string RulesetFingerprint, string RulesetPath, string ExecutionMode, string Prompt)
{
    public string? ArtifactBundle { get; init; }
    public IReadOnlyDictionary<string, string>? ArtifactHashes { get; init; }
}

/// <summary>Request generation exports immutable evidence snapshots only. A future executor consumes this DTO through
/// a separate reviewed interface; this service never launches processes or edits rules.</summary>
public sealed class RuleMaintenance(CheapRejectRules rules, IConfiguration configuration,
    IHostEnvironment environment, CheapTriageShadow? shadow = null, HostingConfiguration? hosting = null)
{
    private readonly string rulesetPath = configuration["CheapTriage:RulesetPath"] ?? CheapRejectRules.DefaultPath;
    private readonly string repositoryPath = configuration["CheapTriage:RepositoryPath"] ?? @"D:\Computer Everything\Programming\CS\JobSearchManager";

    public CheapTriageStatus GetStatus()
    {
        using var context = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            environment.ContentRootPath, "CheapTriage", "evaluation-context.json")));
        var current = context.RootElement.GetProperty("rulesetFingerprint").GetString() == rules.Fingerprint;
        return new(shadow?.Mode ?? "Off", false, rules.Version, rules.Fingerprint, current,
            current ? context.RootElement.GetProperty("baseline").Clone() : null);
    }

    public RuleMaintenancePrompt GenerateReviewed(HumanReviewReport review, CheapTriageLiveReport? live)
    {
        if (!review.Complete || review.Remaining != 0 || review.Cases.Any(c =>
            !review.Reviews.ContainsKey(c.GetProperty("stableJobId").GetString()!)))
            throw new InvalidOperationException("Complete Human Review before preparing a rule update.");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var reviewBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, review.CategoryName, review.QueueVersion, review.QueueFingerprint,
            review.SourceManifestHash, review.Reviewed, review.Counts,
            decisions = review.Cases.OrderBy(c => c.GetProperty("stableJobId").GetString(), StringComparer.Ordinal).Select(c => new
            {
                posting = c, human = review.Reviews[c.GetProperty("stableJobId").GetString()!],
                disagreesWithMachine = c.GetProperty("currentDecision").GetString() !=
                    review.Reviews[c.GetProperty("stableJobId").GetString()!].Decision
            })
        }, json);
        var liveBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, rulesetVersion = rules.Version, rulesetFingerprint = rules.Fingerprint,
            scope = "Current workspace cached observations only; null means unavailable. No provider refresh requested. Not a global traffic sample.",
            liveShadow = live
        }, json);
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["human-review.json"] = Hash(reviewBytes), ["live-shadow.json"] = Hash(liveBytes)
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, rulesetPath, rulesetVersion = rules.Version, rulesetFingerprint = rules.Fingerprint,
            review.QueueVersion, review.QueueFingerprint, review.SourceManifestHash, artifacts = hashes
        }, json);
        var bundle = Hash(manifestBytes);
        var directory = Path.Combine(configuration["CheapTriage:ReviewArtifactDirectory"] ??
            Path.Combine(hosting?.IsContainer == true ? "/app/data" : Path.Combine(environment.ContentRootPath, "data"),
                "rule-update-artifacts"), bundle);
        Directory.CreateDirectory(directory);
        WriteImmutable(Path.Combine(directory, "human-review.json"), reviewBytes);
        WriteImmutable(Path.Combine(directory, "live-shadow.json"), liveBytes);
        WriteImmutable(Path.Combine(directory, "manifest.json"), manifestBytes);
        hashes["manifest.json"] = bundle;
        var values = new Dictionary<string, string>
        {
            ["repository"] = repositoryPath, ["rulesetPath"] = rulesetPath,
            ["rulesetVersion"] = rules.Version, ["fingerprint"] = rules.Fingerprint,
            ["bundle"] = bundle, ["reviewHash"] = hashes["human-review.json"], ["liveHash"] = hashes["live-shadow.json"],
            ["queueVersion"] = review.QueueVersion, ["queueHash"] = review.QueueFingerprint,
            ["sourceHash"] = review.SourceManifestHash,
            ["counts"] = $"{review.Reviewed} reviewed; KEEP {review.Counts["KEEP"]}, REJECT {review.Counts["REJECT"]}, AMBIGUOUS {review.Counts["AMBIGUOUS"]}",
            ["runtimeDirectory"] = directory
        };
        var template = File.ReadAllText(Path.Combine(environment.ContentRootPath, "CheapTriage", "human-reviewed-prompt-v2.txt"));
        var prompt = Regex.Replace(template, @"\{\{([A-Za-z]+)\}\}", match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value : throw new InvalidDataException("Unknown human-reviewed prompt token."));
        return new("human-reviewed-2.0.0", rules.Version, rules.Fingerprint, rulesetPath, "manual-review-only", prompt)
        { ArtifactBundle = bundle, ArtifactHashes = hashes };
    }

    private static void WriteImmutable(string path, byte[] bytes)
    {
        // Publish complete bytes atomically. Concurrent requests may only reuse identical content.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            try { File.Move(temporary, path, overwrite: false); }
            catch (IOException) when (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("Existing rule-update artifact failed integrity verification.");
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public RuleMaintenancePrompt Generate()
    {
        var path = rulesetPath;
        var root = Path.Combine(environment.ContentRootPath, "CheapTriage");
        var template = File.ReadAllText(Path.Combine(root, "maintenance-prompt-v1.txt"));
        using var context = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "evaluation-context.json")));
        var matching = context.RootElement.GetProperty("rulesetFingerprint").GetString() == rules.Fingerprint;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["repository"] = repositoryPath,
            ["rulesetPath"] = path, ["rulesetVersion"] = rules.Version,
            ["fingerprint"] = rules.Fingerprint,
            ["metricsStatus"] = matching ? "Matches the loaded ruleset. Provisional machine labels; not human ground truth." :
                "STALE baseline: these metrics do not describe the loaded ruleset. Evaluate it before proposing changes.",
            ["evaluationContext"] = context.RootElement.GetRawText()
        };
        // Single substitution pass: values can never inject another template token.
        var prompt = Regex.Replace(template, @"\{\{([A-Za-z]+)\}\}", match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value :
                throw new InvalidDataException("Unknown maintenance prompt token."));
        return new("1.0.0", rules.Version, rules.Fingerprint, path, "manual-review-only", prompt);
    }
}

public sealed record CheapTriageStatus(string Mode, bool ProductionGateAvailable,
    string RulesetVersion, string RulesetFingerprint, bool MetricsCurrent, JsonElement? FrozenEvaluation, CheapTriageLiveReport? Live = null);
