using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using JobSearchManager;

internal static class ClearanceMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, ClearanceAnalysis? Expected = null);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = LegacyClearanceBaseline.AnalyzeClearance(f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} legacy clearance results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "clearance-fixtures.json")), Json)!;
        foreach (var fixture in fixtures)
        {
            var old = LegacyClearanceBaseline.AnalyzeClearance(fixture.Html);
            var current = JobAnalysis.AnalyzeClearance(fixture.Html);
            if (old != fixture.Expected || current != old)
                throw new InvalidOperationException($"Clearance parity failed for {fixture.Id}: expected {fixture.Expected}; old {old}; current {current}");
        }
        Console.WriteLine($"Exact clearance fixture parity: {fixtures.Length}/{fixtures.Length} (all result/evidence/status fields).");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var pairs = fixtures.Select(f => new { f.Id, Old = LegacyClearanceBaseline.AnalyzeClearance(f.Html), Current = JobAnalysis.AnalyzeClearance(f.Html) }).ToArray();
        var report = new
        {
            baselineCommit = "41eda623187db07bfd5550823325e1ecf72aa3fd",
            rulesetVersion = ClearanceRules.Default.Version,
            fingerprint = ClearanceRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => p.Old == p.Current),
            changedResults = pairs.Count(p => p.Old.Level != p.Current.Level || p.Old.Requirement != p.Current.Requirement),
            changedEvidence = pairs.Count(p => p.Old.Evidence != p.Current.Evidence),
            changedAmbiguityStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            changedPolygraphStatus = pairs.Count(p => p.Old.PolygraphRequired != p.Current.PolygraphRequired),
            differences = pairs.Where(p => p.Old != p.Current).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Clearance corpus parity failed.");
    }
    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "clearance-v1.json");
        var bytes = File.ReadAllBytes(path);
        var rules = ClearanceRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(SHA256.HashData(bytes)))
            throw new InvalidOperationException("Rules identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("clearance", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid clearance rules were not rejected clearly.");
        }
        Reject(() => ClearanceRules.Load(path + ".missing"));
        Reject(() => ClearanceRules.Parse("{"u8.ToArray()));
        var mutations = new Action<JsonNode>[]
        {
            n => n["schemaVersion"] = 2,
            n => n["rulesetVersion"] = "invalid",
            n => n["unexpected"] = true,
            n => n.AsObject().Remove("patterns"),
            n => n["patterns"] = null,
            n => n["patterns"]![0] = null,
            n => n["patterns"]![0]!["pattern"] = "[",
            n => n["patterns"]![0]!["pattern"] = "",
            n => n["patterns"]![0]!["pattern"] = new string('a', 8193),
            n => n["patterns"]![1]!["id"] = n["patterns"]![0]!["id"]!.GetValue<string>(),
            n => n["patterns"]![0]!["scope"] = "clearanceContext",
            n => n["patterns"]![0]!["type"] = "code",
            n => n["levelRules"]![0]!["result"] = "unknown",
            n => n["levelRules"]![0]!["priority"] = 20,
            n => n["levelRules"]![0]!["id"] = "TopSecretSci",
            n => n["requirementRules"]![0]!["result"] = "unknown",
            n => n["requirementRules"]![0]!["level"] = "unknown",
            n => n["requirementRules"]![0]!["priority"] = 20,
            n => n["requirementRules"]![0]!["unlessPatternIds"] = new JsonArray("missing"),
            n => n["levelOverrides"]![0]!["result"] = "unknown",
            n => n["sections"]!["preferredMarkerIds"] = new JsonArray("TopSecretSci"),
            n => n["sections"]!["lookbehindCharacters"] = 0,
            n => n["negationPatternId"] = "missing",
            n => n["regexOptions"] = new JsonArray("IgnoreCase"),
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001
        };
        foreach (var mutate in mutations)
        {
            var node = JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => ClearanceRules.Parse(JsonSerializer.SerializeToUtf8Bytes(node)));
        }
        var changed = JsonNode.Parse(bytes)!;
        changed["patterns"]![0]!["pattern"] = "unique-fixture-level";
        var changedRules = ClearanceRules.Parse(JsonSerializer.SerializeToUtf8Bytes(changed));
        if (JobAnalysis.AnalyzeClearance("unique-fixture-level", changedRules).Level != "topSecretSCI" || changedRules.Fingerprint == rules.Fingerprint)
            throw new InvalidOperationException("Rules must change without recompilation and expose changed content identity.");
        var hostile = JsonNode.Parse(bytes)!;
        hostile["regexTimeoutMilliseconds"] = 1;
        hostile["patterns"]![5]!["pattern"] = "(a+)+$";
        var timedRules = ClearanceRules.Parse(JsonSerializer.SerializeToUtf8Bytes(hostile));
        try { JobAnalysis.AnalyzeClearance(new string('a', 10000) + "!", timedRules); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains(timedRules.Fingerprint))
        {
            Console.WriteLine($"Clearance rule validation: {mutations.Length + 2} invalid documents/files rejected; bounded timeout and editable rules verified.");
            return Task.CompletedTask;
        }
        throw new InvalidOperationException("Pathological regex did not fail with a bounded, explicit timeout.");
    }
}
