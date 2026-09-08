using System.Text.Json;
using JobSearchManager;

internal static class RemoteWorkMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, RemoteWorkAnalysis? Expected = null, string Title = "Engineer", string PrimaryLocation = "Remote", string[]? AdditionalLocations = null);
    private static bool Equal(RemoteWorkAnalysis? a, RemoteWorkAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        var old = new LegacyRemoteWorkBaseline();
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} remote-work baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "remote-work-fixtures.json")), Json)!;
        var old = new LegacyRemoteWorkBaseline();
        var current = new RemoteWorkDetector();
        foreach (var f in fixtures)
            if (!Equal(old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), f.Expected) || !Equal(current.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), f.Expected))
                throw new InvalidOperationException($"Remote-work parity failed: {f.Id}; old={JsonSerializer.Serialize(old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), Json)}; new={JsonSerializer.Serialize(current.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), Json)}");
        Console.WriteLine($"Exact remote-work fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var old = new LegacyRemoteWorkBaseline();
        var current = new RemoteWorkDetector();
        var pairs = fixtures.Select(f => new { f.Id, Old = old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), Current = current.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html) }).ToArray();
        var directory = Path.Combine(Path.GetTempPath(), "jsm-remote-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = JobConceptCatalog.LoadDefault();
            using var store = new SqliteSemanticRuleStore(Path.Combine(directory, "rules.db"), catalog);
            store.Initialize(Path.Combine(AppContext.BaseDirectory, "LegacyJobConceptRules.json"));
            var classifier = new RegexSemanticClassifier(store, catalog);
            classifier.InitializeAsync().GetAwaiter().GetResult();
            var extended = new ExtendedLocationRequirementDetector();
            var downstream = fixtures.Select((f, i) =>
            {
                var location = extended.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html);
                var before = classifier.Classify(f.Title, f.Html, pairs[i].Old, location, false) with { ClassifiedUtc = DateTimeOffset.UnixEpoch };
                var after = classifier.Classify(f.Title, f.Html, pairs[i].Current, location, false) with { ClassifiedUtc = DateTimeOffset.UnixEpoch };
                if (JsonSerializer.Serialize(before, Json) != JsonSerializer.Serialize(after, Json))
                    throw new InvalidOperationException($"Remote-work downstream concept parity failed: {f.Id}");
                return new { f.Id, Old = before.Concepts, Current = after.Concepts };
            }).ToArray();
            File.WriteAllText(output + ".concepts.json", JsonSerializer.Serialize(downstream, Json));
        }
        finally { Directory.Delete(directory, recursive: true); }
        var report = new
        {
            baselineAnalysisVersion = 3,
            rulesetVersion = RemoteWorkRules.Default.Version,
            fingerprint = RemoteWorkRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedDesignation = pairs.Count(p => p.Old.IsRemoteDesignated != p.Current.IsRemoteDesignated),
            changedConcernLevel = pairs.Count(p => p.Old.ConcernLevel != p.Current.ConcernLevel),
            changedSummary = pairs.Count(p => p.Old.Summary != p.Current.Summary),
            changedSignalsEvidenceOrder = pairs.Count(p => JsonSerializer.Serialize(p.Old.Signals, Json) != JsonSerializer.Serialize(p.Current.Signals, Json)),
            changedParseStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            changedAnalysisVersion = pairs.Count(p => p.Old.AnalysisVersion != p.Current.AnalysisVersion),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Remote-work corpus parity failed.");
    }

    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "remote-work-v1.json");
        var bytes = File.ReadAllBytes(path); var rules = RemoteWorkRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Remote-work identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("remote-work", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid remote-work rules were accepted.");
        }
        Reject(() => RemoteWorkRules.Load(path + ".missing"));
        Reject(() => RemoteWorkRules.Parse("{"u8.ToArray()));
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
            n => n["patterns"]![0]!["pattern"] = new string('a',8193),
            n => n["patterns"]![1]!["id"] = n["patterns"]![0]!["id"]!.GetValue<string>(),
            n => n["patterns"]![0]!["scope"] = "sentence",
            n => n["patterns"]![0]!["type"] = "code",
            n => n["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "TravelPercentagePattern")!["pattern"] = "no-capture",
            n => n["signalRules"]![0]!["patternId"] = "missing",
            n => n["signalRules"]![0]!["category"] = "unknown",
            n => n["signalRules"]![0]!["concernLevel"] = "unknown",
            n => n["signalRules"]![0]!["priority"] = -1,
            n => n["signalRules"]![0]!["priority"] = 1,
            n => n["signalRules"]![0]!["reason"] = "",
            n => n["signalRules"]![0]!["unlessContains"] = "",
            n => n["signalRules"]![1]!["overridePatternId"] = "missing",
            n => n["signalRules"]![1]!["overrideConcernLevel"] = "unknown",
            n => n["signalRules"]![1]!["overrideReason"] = null,
            n => n["travelBands"]![0]!["maximumPercent"] = -1,
            n => n["travelBands"]![0]!["maximumPercent"] = 49,
            n => n["travelBands"]![2]!["maximumPercent"] = 99,
            n => n["travelBands"]![0]!["category"] = "unknown",
            n => n["travelReasonTemplate"] = "missing placeholder",
            n => n["frequentTravel"]!["patternId"] = "missing",
            n => n["frequentTravel"]!["concernLevel"] = "unknown",
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001
        };
        foreach (var mutate in mutations)
        {
            var n = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(n);
            Reject(() => RemoteWorkRules.Parse(JsonSerializer.SerializeToUtf8Bytes(n)));
        }
        var changed = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        changed["patterns"]![0]!["pattern"] = "unique-fixture-designation";
        var editable = RemoteWorkRules.Parse(JsonSerializer.SerializeToUtf8Bytes(changed));
        if (!new RemoteWorkDetector(editable).Analyze("unique-fixture-designation", "", [], "").IsRemoteDesignated || editable.Fingerprint == rules.Fingerprint)
            throw new InvalidOperationException("Remote-work vocabulary edit failed.");
        changed["regexTimeoutMilliseconds"] = 1; changed["patterns"]![0]!["pattern"] = "(a+)+$";
        var timed = RemoteWorkRules.Parse(JsonSerializer.SerializeToUtf8Bytes(changed));
        try { new RemoteWorkDetector(timed).Analyze(new string('a',10000) + "!", "", [], ""); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains(timed.Fingerprint))
        { Console.WriteLine($"Remote-work validation: {mutations.Length + 2} invalid configurations, identity, editable vocabulary and bounded timeout passed."); return Task.CompletedTask; }
        throw new InvalidOperationException("Expected bounded remote-work timeout.");
    }
}
