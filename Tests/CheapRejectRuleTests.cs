using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobSearchManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

internal static class CheapRejectRuleTests
{
    public static Task Run()
    {
        var path = Path.Combine(AppContext.BaseDirectory, CheapRejectRules.DefaultPath);
        var bytes = File.ReadAllBytes(path);
        var rules = CheapRejectRules.Load(path);
        Check(rules.Version == "1.0.0" && rules.Fingerprint.Length == 64, "External version/fingerprint");
        var keep = rules.Decide("Software Developer", "Develop C# applications.");
        Check(keep.Decision == "KEEP" && keep.Guards.Count == 1 && keep.RuleIds.Count == 1, "Technical KEEP guard");
        var reject = rules.Decide("Registered Nurse", "Provide nursing and patient care in a hospital.");
        Check(reject.Decision == "REJECT" && reject.RuleIds.Contains("reject-clinical") && reject.Evidence.Any(e => e.Scope == "body"), "Corroborated clinical reject");
        Check(reject.RulesetFingerprint == rules.Fingerprint && reject.PostingFingerprint != keep.PostingFingerprint, "Decision provenance");
        Check(rules.Decide("Registered Nurse", "").Decision == "KEEP", "Missing corroboration fails safe");
        foreach (var title in new[] { "PC Support Technician", "Enterprise Systems Engineer", "Cybersecurity Analyst", "Application Developer", "Electrical Engineer" })
            Check(rules.Decide(title, "Technical infrastructure and integration support.").Decision == "KEEP", title);
        Check(rules.Decide(new string('x', 10001), "").FailOpen, "Oversized input fails open");
        Check(rules.Decide("REGISTERED NURSE", "<p>patient &amp; nursing care</p>").Decision == "REJECT", "HTML/case normalization");
        var reordered = JsonNode.Parse(bytes)!.AsObject();
        var reorderedRules = reordered["rules"]!.AsArray().Reverse().Select(n => n!.DeepClone()).ToArray();
        reordered["rules"] = new JsonArray(reorderedRules);
        var reorderedDecision = CheapRejectRules.Parse(Encode(reordered)).Decide("Registered Nurse", "Provide nursing and patient care in a hospital.");
        Check(reorderedDecision.RuleIds.SequenceEqual(reject.RuleIds) && reorderedDecision.Evidence.SequenceEqual(reject.Evidence), "Explicit order independent of JSON array order");
        Invalid(n => n["schemaVersion"] = 2);
        Invalid(n => n["extra"] = true);
        Invalid(n => n["rulesetVersion"] = "01.0.0");
        Invalid(n => n["status"] = "discard");
        Invalid(n => n["predicates"] = null);
        Invalid(n => n["rules"]![0]!["unknown"] = 1);
        Invalid(n => n["rules"]![1]!["id"] = n["rules"]![0]!["id"]!.GetValue<string>());
        Invalid(n => n["rules"]![1]!["order"] = n["rules"]![0]!["order"]!.GetValue<int>());
        Invalid(n => n["rules"]![0]!["when"] = "absent");
        Invalid(n => n["predicates"]![0]!["all"] = new JsonArray("support"));
        Invalid(n => n["predicates"]![0]!["pattern"] = "[");
        Invalid(n => { n["predicates"]![0]!.AsObject().Remove("pattern"); n["predicates"]![0]!.AsObject().Remove("scope"); n["predicates"]![0]!["all"] = new JsonArray("support"); });
        Throws(() => CheapRejectRules.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"))));
        var temp = Path.Combine(Path.GetTempPath(), "jsm-cheap-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "CheapTriage"));
        try
        {
            var copy = Path.Combine(temp, "rules.json"); File.WriteAllBytes(copy, bytes);
            var snapshot = CheapRejectRules.Load(copy);
            var revised = JsonNode.Parse(bytes)!; revised["rulesetVersion"] = "1.0.1"; File.WriteAllBytes(copy, Encode(revised));
            var reload = CheapRejectRules.Load(copy);
            Check(snapshot.Version == "1.0.0" && reload.Version == "1.0.1" && snapshot.Fingerprint != reload.Fingerprint, "Loaded snapshot immutable; explicit reload changes version");
            foreach (var file in new[] { "maintenance-prompt-v1.txt", "evaluation-context.json" })
                File.Copy(Path.Combine(AppContext.BaseDirectory, "CheapTriage", file), Path.Combine(temp, "CheapTriage", file));
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CheapTriage:RepositoryPath"] = "repo/{{unknown}}" }).Build();
            var env = new TestEnvironment { ContentRootPath = temp };
            var maintenance = new RuleMaintenance(snapshot, config, env);
            var prompt = maintenance.Generate();
            config["CheapTriage:RulesetPath"] = "not-loaded.json";
            Check(maintenance.Generate().RulesetPath == prompt.RulesetPath, "Config reload cannot misidentify the loaded snapshot path");
            Check(!prompt.Prompt.Contains("STALE baseline"), "Packaged metrics match the shipped rules bytes on every platform");
            Check(prompt.RulesetVersion == "1.0.0" && prompt.ExecutionMode == "manual-review-only", "Prompt contract");
            foreach (var text in new[] { "1.0.0", snapshot.Fingerprint, "curiosity-codex", "repo/{{unknown}}", "KEEP recall", "blinded", "commit", "supervised_v2" })
                Check(prompt.Prompt.Contains(text, StringComparison.OrdinalIgnoreCase), "Prompt regression: " + text);
            Check(new RuleMaintenance(reload, config, env).Generate().Prompt.Contains("STALE baseline"), "Stale metrics disclosed");
            File.AppendAllText(Path.Combine(temp, "CheapTriage", "maintenance-prompt-v1.txt"), "{{invalid}}");
            Throws(() => new RuleMaintenance(snapshot, config, env).Generate());
        }
        finally { Directory.Delete(temp, true); }
        return Task.CompletedTask;

        void Invalid(Action<JsonNode> mutate) { var node = JsonNode.Parse(bytes)!; mutate(node); Throws(() => CheapRejectRules.Parse(Encode(node))); }
    }
    private static byte[] Encode(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { return; }
        throw new Exception("Invalid rules/template accepted");
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
