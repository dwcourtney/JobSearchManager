using System.Text.Json;
using JobSearchManager;

internal static class ExtendedLocationMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, ExtendedLocationRequirementAnalysis? Expected = null, string Title = "Engineer", string PrimaryLocation = "Remote", string[]? AdditionalLocations = null);
    private static bool Equal(ExtendedLocationRequirementAnalysis? a, ExtendedLocationRequirementAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        var old = new LegacyExtendedLocationBaseline();
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} extended-location baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "extended-location-fixtures.json")), Json)!;
        var old = new LegacyExtendedLocationBaseline();
        var current = new ExtendedLocationRequirementDetector();
        foreach (var f in fixtures)
            if (!Equal(old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), f.Expected) || !Equal(current.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), f.Expected))
                throw new InvalidOperationException($"Extended-location parity failed: {f.Id}; old={JsonSerializer.Serialize(old.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), Json)}; new={JsonSerializer.Serialize(current.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html), Json)}");
        Console.WriteLine($"Exact extended-location fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var old = new LegacyExtendedLocationBaseline();
        var current = new ExtendedLocationRequirementDetector();
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
            var remote = new RemoteWorkDetector();
            var downstream = fixtures.Select((f, i) =>
            {
                var location = remote.Analyze(f.Title, f.PrimaryLocation, f.AdditionalLocations ?? [], f.Html);
                var before = classifier.Classify(f.Title, f.Html, location, pairs[i].Old, false) with { ClassifiedUtc = DateTimeOffset.UnixEpoch };
                var after = classifier.Classify(f.Title, f.Html, location, pairs[i].Current, false) with { ClassifiedUtc = DateTimeOffset.UnixEpoch };
                if (JsonSerializer.Serialize(before, Json) != JsonSerializer.Serialize(after, Json))
                    throw new InvalidOperationException($"Extended-location downstream concept parity failed: {f.Id}");
                return new { f.Id, Old = before.Concepts, Current = after.Concepts };
            }).ToArray();
            File.WriteAllText(output + ".concepts.json", JsonSerializer.Serialize(downstream, Json));
        }
        finally { Directory.Delete(directory, recursive: true); }
        var report = new
        {
            baselineAnalysisVersion = 4,
            rulesetVersion = ExtendedLocationRules.Default.Version,
            fingerprint = ExtendedLocationRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedDestination = pairs.Count(p => p.Old.Destination != p.Current.Destination),
            changedConfidence = pairs.Count(p => p.Old.Confidence != p.Current.Confidence),
            changedSummary = pairs.Count(p => p.Old.Summary != p.Current.Summary),
            changedSignalsEvidenceOrder = pairs.Count(p => JsonSerializer.Serialize(p.Old.Signals, Json) != JsonSerializer.Serialize(p.Current.Signals, Json)),
            changedParseStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            changedAnalysisVersion = pairs.Count(p => p.Old.AnalysisVersion != p.Current.AnalysisVersion),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Extended-location corpus parity failed.");
    }


    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "extended-location-v1.json");
        var bytes = File.ReadAllBytes(path); var rules = ExtendedLocationRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Extended-location identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("extended-location", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid extended-location rules were accepted.");
        }
        Reject(() => ExtendedLocationRules.Load(path + ".missing"));
        Reject(() => ExtendedLocationRules.Parse("{"u8.ToArray()));
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
            n => n["patterns"]![1]!["id"] = n["patterns"]![0]!["id"]!.GetValue<string>(),
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001,
            n => n["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "DurationPattern")!["pattern"] = "x",
            n => n["signalRules"]![0]!["patternId"] = "missing",
            n => n["signalRules"]![0]!["category"] = "unknown",
            n => n["signalRules"]![0]!["confidence"] = "unknown",
            n => n["signalRules"]![0]!["reason"] = "",
            n => n["signalRules"]![1]!["priority"] = 0,
            n => n["signalRules"] = null,
            n => n["destinations"]![1]!["priority"] = 0,
            n => n["destinations"]![0]!["name"] = "",
            n => n["destinations"]![0]!["patternId"] = "missing",
            n => n["duration"]!["minimumDays"] = 0,
            n => n["duration"]!["numbers"]!["one"] = 0,
            n => n["duration"]!["unitPrefixes"]!["week"] = 0,
            n => n["duration"]!["defaultDaysPerUnit"] = 0,
            n => n["summaries"]!["required-deployment"] = null,
            n => n["summaries"]!.AsObject().Remove("required-deployment"),
            n => n["unspecifiedDestination"] = ""
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => ExtendedLocationRules.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        // The exact old compiled expressions must be preserved in JSON.
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        foreach (var pattern in rules.Rules.Patterns)
        {
            string original;
            if (pattern.Id.StartsWith("signal-", StringComparison.Ordinal))
            {
                var category = pattern.Id[7..];
                var all = new[] { "StrongRules", "QuestionableRules" }.SelectMany(name => ((Array)typeof(LegacyExtendedLocationBaseline).GetField(name, flags)!.GetValue(null)!).Cast<object>());
                var item = all.Single(v => (string)v.GetType().GetProperty("Category")!.GetValue(v)! == category);
                original = ((System.Text.RegularExpressions.Regex)item.GetType().GetProperty("Pattern")!.GetValue(item)!).ToString();
            }
            else if (pattern.Id.StartsWith("destination-", StringComparison.Ordinal))
            {
                var items = (Array)typeof(LegacyExtendedLocationBaseline).GetField("Destinations", flags)!.GetValue(null)!;
                var item = items.GetValue(int.Parse(pattern.Id[12..]))!;
                original = ((System.Text.RegularExpressions.Regex)item.GetType().GetProperty("Pattern")!.GetValue(item)!).ToString();
            }
            else original = ((System.Text.RegularExpressions.Regex)typeof(LegacyExtendedLocationBaseline).GetField(pattern.Id, flags)!.GetValue(null)!).ToString();
            if (original != pattern.Pattern) throw new InvalidOperationException("Changed pattern: " + pattern.Id);
        }
        var editable = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        editable["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "signal-required-deployment")!["pattern"] = @"\bZXQ\b";
        var custom = ExtendedLocationRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString()));
        if (new ExtendedLocationRequirementDetector(custom).Analyze("Engineer", "Remote", [], "ZXQ").Confidence != "strong")
            throw new InvalidOperationException("Declarative vocabulary was not applied.");
        editable["regexTimeoutMilliseconds"] = 1;
        editable["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "HistoricalPattern")!["pattern"] = "(a+)+$";
        try
        {
            new ExtendedLocationRequirementDetector(ExtendedLocationRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString())))
                .Analyze("Engineer", "Remote", [], new string('a', 50000) + "!");
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains("Extended-location rules", StringComparison.Ordinal))
        { Console.WriteLine($"Extended-location validation: {mutations.Length + 2} invalid configurations rejected; 35 exact expressions, editable vocabulary and timeout diagnostics passed."); return Task.CompletedTask; }
        throw new InvalidOperationException("Regex timeout was not enforced.");
    }
}
