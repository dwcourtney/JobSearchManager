using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobSearchManager;

public sealed record RuleMaintenancePrompt(string TemplateVersion, string RulesetVersion,
    string RulesetFingerprint, string RulesetPath, string ExecutionMode, string Prompt);

/// <summary>Read-only request generation. A future executor consumes this DTO through
/// a separate reviewed interface; this service never launches processes or edits rules.</summary>
public sealed class RuleMaintenance(CheapRejectRules rules, IConfiguration configuration,
    IHostEnvironment environment)
{
    private readonly string rulesetPath = configuration["CheapTriage:RulesetPath"] ?? CheapRejectRules.DefaultPath;
    private readonly string repositoryPath = configuration["CheapTriage:RepositoryPath"] ?? @"D:\Computer Everything\Programming\CS\JobSearchManager";

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
