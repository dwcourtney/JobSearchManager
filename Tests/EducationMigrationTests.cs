using System.Text.Json;
using JobSearchManager;

internal static class EducationMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, AcademicQualificationAnalysis? Expected = null);
    private static bool Equal(AcademicQualificationAnalysis? a, AcademicQualificationAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        var old = new LegacyEducationBaseline();
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = old.Analyze(f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} education baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "education-fixtures.json")), Json)!;
        var old = new LegacyEducationBaseline();
        var current = new AcademicQualificationDetector();
        foreach (var f in fixtures)
            if (!Equal(old.Analyze(f.Html), f.Expected) || !Equal(current.Analyze(f.Html), f.Expected))
                throw new InvalidOperationException($"Education parity failed: {f.Id}; old={JsonSerializer.Serialize(old.Analyze(f.Html), Json)}; new={JsonSerializer.Serialize(current.Analyze(f.Html), Json)}");
        Console.WriteLine($"Exact education fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var old = new LegacyEducationBaseline();
        var current = new AcademicQualificationDetector();
        var pairs = fixtures.Select(f => new { f.Id, Old = old.Analyze(f.Html), Current = current.Analyze(f.Html) }).ToArray();
        var report = new
        {
            baselineAnalysisVersion = 4,
            rulesetVersion = EducationRules.Default.Version,
            fingerprint = EducationRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedMinimumLevel = pairs.Count(p => p.Old.MinimumLevel != p.Current.MinimumLevel),
            changedSpecificDegree = pairs.Count(p => p.Old.SpecificDegree != p.Current.SpecificDegree),
            changedRequirementType = pairs.Count(p => p.Old.RequirementType != p.Current.RequirementType),
            changedExperienceSubstitutionAccepted = pairs.Count(p => p.Old.ExperienceSubstitutionAccepted != p.Current.ExperienceSubstitutionAccepted),
            changedAccreditation = pairs.Count(p => JsonSerializer.Serialize(p.Old.Accreditations, Json) != JsonSerializer.Serialize(p.Current.Accreditations, Json)),
            changedParseStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            changedEvidenceOrder = pairs.Count(p => !p.Old.Evidence.SequenceEqual(p.Current.Evidence)),
            changedPathsFieldsOrOrder = pairs.Count(p => JsonSerializer.Serialize(p.Old.Paths, Json) != JsonSerializer.Serialize(p.Current.Paths, Json) || !p.Old.Fields.SequenceEqual(p.Current.Fields) || !p.Old.PreferredLevels.SequenceEqual(p.Current.PreferredLevels)),
            changedAnalysisVersion = pairs.Count(p => p.Old.AnalysisVersion != p.Current.AnalysisVersion),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Education corpus parity failed.");
    }

    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "education-v1.json");
        var bytes = File.ReadAllBytes(path);
        var rules = EducationRules.Load(path);
        var detector = new AcademicQualificationDetector(rules);
        if (detector.RulesetVersion != "1.0.0" || detector.RulesetFingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Education rules identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("education", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid education rules did not fail clearly.");
        }
        Reject(() => EducationRules.Load(path + ".missing"));
        Reject(() => EducationRules.Parse("{"u8.ToArray()));
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
            n => n["patterns"]![0]!["scope"] = "segment",
            n => n["patterns"]![0]!["type"] = "code",
            n => n["patterns"]![0]!["options"] = new System.Text.Json.Nodes.JsonArray("Singleline"),
            n => n["degreeRules"]![0]!["level"] = "unknown",
            n => n["degreeRules"]![0]!["specificDegree"] = "phD",
            n => n["degreeRules"]![5]!["specificDegree"] = "unknown",
            n => n["degreeRules"]![0]!["priority"] = -1,
            n => n["degreeRules"]![0]!["priority"] = 1,
            n => n["degreeRules"]![0]!["patternId"] = "missing",
            n => n["degreeRules"]![0]!["id"] = "HighSchool",
            n => n["abbreviations"]!["captureGroup"] = "missing",
            n => n["abbreviations"]!["normalizationPatternId"] = "HighSchool",
            n => n["abbreviations"]!["levels"]!["AA"] = "unknown",
            n => n["abbreviations"]!["levels"]!["lowercase"] = "bachelor",
            n => n["abbreviations"]!["levels"]!["AA"] = null,
            n => n["qualifierRules"]![0]!["result"] = "unknown",
            n => n["qualifierRules"]![0]!["priority"] = 1,
            n => n["sectionRules"]![0]!["result"] = "unknown",
            n => n["sectionRules"]![0]!["conditionalResult"] = "unknown",
            n => n["sectionRules"]![0]!["conditionalPatternId"] = "missing",
            n => n["sectionRules"]![0]!["conditionalPatternId"] = null,
            n => n["sectionRules"]![0]!["priority"] = 1,
            n => n["accreditation"]!["patternId"] = "missing",
            n => n["accreditation"]!["name"] = "",
            n => n["accreditation"]!["requirementRules"]![0]!["result"] = "unknown",
            n => n["relatedFieldResult"] = "",
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001,
            n => n["patterns"]!.AsArray().Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "unused", ["type"] = "regex", ["pattern"] = "unused", ["scope"] = "segment", ["options"] = new System.Text.Json.Nodes.JsonArray() }),
            n => n["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "FieldList")!["pattern"] = "missing-capture-group"
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => EducationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(node)));
        }
        var changed = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        changed["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "HighSchool")!["pattern"] = "unique-fixture-education";
        var changedRules = EducationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(changed));
        if (new AcademicQualificationDetector(changedRules).Analyze("unique-fixture-education").MinimumLevel != "highSchool" || changedRules.Fingerprint == rules.Fingerprint)
            throw new InvalidOperationException("Education vocabulary must be editable without recompilation.");
        var hostile = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        hostile["regexTimeoutMilliseconds"] = 1;
        hostile["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "HighSchool")!["pattern"] = "(a+)+$";
        var timedRules = EducationRules.Parse(JsonSerializer.SerializeToUtf8Bytes(hostile));
        try { new AcademicQualificationDetector(timedRules).Analyze(new string('a', 10000) + "!"); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains(timedRules.Fingerprint))
        {
            Console.WriteLine($"Education validation: {mutations.Length + 2} invalid documents/files rejected; bounded timeout, identity and editable vocabulary verified.");
            return Task.CompletedTask;
        }
        throw new InvalidOperationException("Education matching did not raise a bounded explicit timeout.");
    }
}
