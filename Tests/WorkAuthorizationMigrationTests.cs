using System.Text.Json;
using JobSearchManager;

internal static class WorkAuthorizationMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, WorkAuthorizationAnalysis? Expected = null);
    private static bool Equal(WorkAuthorizationAnalysis? a, WorkAuthorizationAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        var old = new LegacyWorkAuthorizationBaseline();
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = old.Analyze(f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} work-authorization baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "work-authorization-fixtures.json")), Json)!;
        var old = new LegacyWorkAuthorizationBaseline();
        var current = new WorkAuthorizationDetector();
        foreach (var f in fixtures)
            if (!Equal(old.Analyze(f.Html), f.Expected) || !Equal(current.Analyze(f.Html), f.Expected))
                throw new InvalidOperationException($"Work-authorization parity failed: {f.Id}; old={JsonSerializer.Serialize(old.Analyze(f.Html), Json)}; new={JsonSerializer.Serialize(current.Analyze(f.Html), Json)}");
        Console.WriteLine($"Exact work-authorization fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var old = new LegacyWorkAuthorizationBaseline();
        var current = new WorkAuthorizationDetector();
        var pairs = fixtures.Select(f => new { f.Id, Old = old.Analyze(f.Html), Current = current.Analyze(f.Html) }).ToArray();
        var report = new
        {
            baselineAnalysisVersion = 4,
            rulesetVersion = WorkAuthorizationRules.Default.Version,
            fingerprint = WorkAuthorizationRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedEligibility = pairs.Count(p => p.Old.Eligibility != p.Current.Eligibility),
            changedCountryCode = pairs.Count(p => p.Old.CountryCode != p.Current.CountryCode),
            changedSponsorship = pairs.Count(p => p.Old.Sponsorship != p.Current.Sponsorship),
            changedRequirementStrength = pairs.Count(p => p.Old.Strength != p.Current.Strength || p.Old.SponsorshipStrength != p.Current.SponsorshipStrength),
            changedEvidence = pairs.Count(p => !p.Old.Evidence.SequenceEqual(p.Current.Evidence)),
            changedAmbiguityParseStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            changedAnalysisVersion = pairs.Count(p => p.Old.AnalysisVersion != p.Current.AnalysisVersion),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Work-authorization corpus parity failed.");
    }

    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "work-authorization-v1.json");
        var bytes = File.ReadAllBytes(path);
        var rules = WorkAuthorizationRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Work-authorization rules identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("work-authorization", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid work-authorization rules did not fail clearly.");
        }
        Reject(() => WorkAuthorizationRules.Load(path + ".missing"));
        Reject(() => WorkAuthorizationRules.Parse("{"u8.ToArray()));
        var mutations = new Action<System.Text.Json.Nodes.JsonNode>[]
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
            n => n["patterns"]![0]!["scope"] = "plainText",
            n => n["patterns"]![0]!["type"] = "code",
            n => n["patterns"]![0]!["options"] = new System.Text.Json.Nodes.JsonArray("Singleline"),
            n => n["eligibilityRules"]![0]!["result"] = "unknown",
            n => n["eligibilityRules"]![0]!["countryCode"] = "unknown",
            n => n["eligibilityRules"]![0]!["strength"] = "unknown",
            n => n["eligibilityRules"]![0]!["application"] = "unknown",
            n => n["eligibilityRules"]![0]!["evidenceIndex"] = "unknown",
            n => n["eligibilityRules"]![0]!["priority"] = -1,
            n => n["eligibilityRules"]![0]!["priority"] = 20,
            n => n["eligibilityRules"]![0]!["id"] = "CitizenOrResident",
            n => n["eligibilityRules"]![0]!["anyPatternIds"] = new System.Text.Json.Nodes.JsonArray("missing"),
            n => n["eligibilityRules"]![0]!["unlessPatternIds"] = new System.Text.Json.Nodes.JsonArray("missing"),
            n => n["eligibilityRules"]![0]!["conditionalStrength"] = "customerDependent",
            n => n["eligibilityRules"]![11]!["conditionalPatternId"] = "missing",
            n => n["eligibilityRules"]![11]!["conditionalStrength"] = "strict",
            n => n["sponsorshipRules"]![0]!["result"] = "available",
            n => n["sponsorshipRules"]![0]!["strength"] = "unknown",
            n => n["sponsorshipRules"]![0]!["unlessPatternIds"] = new System.Text.Json.Nodes.JsonArray("missing"),
            n => n["sectionRules"]![0]!["result"] = "unknown",
            n => n["sectionRules"]![0]!["priority"] = 20,
            n => n["normalizations"]![0]!["patternId"] = "UsCitizen",
            n => n["normalizations"]![0]!["replacement"] = new string('x', 257),
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => WorkAuthorizationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(node)));
        }
        var changed = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        changed["patterns"]![0]!["pattern"] = "unique-fixture-authorization";
        var changedRules = WorkAuthorizationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(changed));
        if (new WorkAuthorizationDetector(changedRules).Analyze("unique-fixture-authorization").Eligibility != "usCitizenOrPermanentResident" || changedRules.Fingerprint == rules.Fingerprint)
            throw new InvalidOperationException("Work-authorization vocabulary must be editable without recompilation.");
        var hostile = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        hostile["regexTimeoutMilliseconds"] = 1;
        hostile["patterns"]![0]!["pattern"] = "(a+)+$";
        var timedRules = WorkAuthorizationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(hostile));
        try { new WorkAuthorizationDetector(timedRules).Analyze(new string('a', 10000) + "!"); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains(timedRules.Fingerprint))
        {
            Console.WriteLine($"Work-authorization validation: {mutations.Length + 2} invalid documents/files rejected; bounded timeout, identity and editable vocabulary verified.");
            return Task.CompletedTask;
        }
        throw new InvalidOperationException("Work-authorization matching did not raise a bounded explicit timeout.");
    }
}
