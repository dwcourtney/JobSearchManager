using System.Text.Json;
using JobSearchManager;

internal static class GeographicRestrictionMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, string PrimaryLocation = "", IReadOnlyList<string>? AdditionalLocations = null, RemoteLocationAnalysis? Expected = null);
    private static bool Equal(RemoteLocationAnalysis? a, RemoteLocationAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = LegacyGeographicRestrictionBaseline.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} geographic-restriction baseline results.");
    }
    internal static Task RunAsync()
    {
        if (JobSourceClient.CurrentAnalysisVersion != 7) throw new InvalidOperationException("Provider analysis version changed.");
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "geographic-restriction-fixtures.json")), Json)!;
        foreach (var f in fixtures)
            if (!Equal(LegacyGeographicRestrictionBaseline.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []), f.Expected) || !Equal(JobAnalysis.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []), f.Expected))
                throw new InvalidOperationException($"Geographic-restriction parity failed: {f.Id}; old={JsonSerializer.Serialize(LegacyGeographicRestrictionBaseline.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []), Json)}; new={JsonSerializer.Serialize(JobAnalysis.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []), Json)}");
        Console.WriteLine($"Exact geographic-restriction fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var pairs = fixtures.Select(f => new { f.Id, Old = LegacyGeographicRestrictionBaseline.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []), Current = JobAnalysis.AnalyzeRemoteLocation(f.Html, f.PrimaryLocation, f.AdditionalLocations ?? []) }).ToArray();
        File.WriteAllText(output + ".outputs.json", JsonSerializer.Serialize(pairs, Json));
        var report = new
        {
            rulesetVersion = GeographicRestrictionRules.Default.Version,
            fingerprint = GeographicRestrictionRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedRestrictionFlag = pairs.Count(p => p.Old.IsRestricted != p.Current.IsRestricted),
            changedCategory = pairs.Count(p => p.Old.Category != p.Current.Category),
            changedSnippet = pairs.Count(p => p.Old.Snippet != p.Current.Snippet),
            changedPrecedenceOutcome = pairs.Count(p => p.Old.Category != p.Current.Category || p.Old.Snippet != p.Current.Snippet),
            baselineAnalysisVersion = 7,
            currentAnalysisVersion = JobSourceClient.CurrentAnalysisVersion,
            changedAnalysisVersion = JobSourceClient.CurrentAnalysisVersion == 7 ? 0 : pairs.Length,
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings || report.changedAnalysisVersion != 0) throw new InvalidOperationException("Geographic-restriction corpus parity failed.");
    }


    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "geographic-restriction-v1.json");
        var bytes = File.ReadAllBytes(path); var rules = GeographicRestrictionRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))) throw new InvalidOperationException("Geographic identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("geographic-restriction", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid geographic rules accepted.");
        }
        Reject(() => GeographicRestrictionRules.Load(path + ".missing")); Reject(() => GeographicRestrictionRules.Parse("{"u8.ToArray()));
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
            n => n["patterns"]![0]!["id"] = "unknown",
            n => n["patterns"]![0]!["pattern"] = null,
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001,
            n => n["remoteDesignationCues"] = null,
            n => n["remoteDesignationCues"]!["description"] = "",
            n => n["remoteDesignationCues"]!["primaryLocation"] = null,
            n => n["rules"] = null,
            n => n["rules"]![0] = null,
            n => n["rules"]![1]!["id"] = n["rules"]![0]!["id"]!.GetValue<string>(),
            n => n["rules"]![1]!["priority"] = 0,
            n => n["rules"]![0]!["priority"] = -1,
            n => n["rules"]![0]!["category"] = "unknown",
            n => n["rules"]![0]!["patternId"] = "unknown",
            n => n["rules"]![0]!["unlessPatternIds"] = null,
            n => n["rules"]![1]!["unlessPatternIds"]![0] = "unknown"
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => GeographicRestrictionRules.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        foreach (var p in rules.Rules.Patterns)
        {
            var old = (System.Text.RegularExpressions.Regex)typeof(LegacyGeographicRestrictionBaseline).GetMethod(p.Id, flags)!.Invoke(null, null)!;
            if (old.ToString() != p.Pattern || old.Options != rules.Pattern(p.Id).Options || old.MatchTimeout != System.Text.RegularExpressions.Regex.InfiniteMatchTimeout || rules.Pattern(p.Id).MatchTimeout != TimeSpan.FromSeconds(1)) throw new InvalidOperationException("Geographic expression/options or timeout mismatch: " + p.Id);
        }
        if (rules.Rules.RemoteDesignationCues != new GeographicRestrictionRules.RemoteCues("Remote", "Remote", "remote")) throw new InvalidOperationException("Literal cue spellings changed.");
        var expectedOrder = new[] { "distance-radius", "commuting-distance", "hybrid-local", "required-region", "regional-preference" };
        if (!rules.OrderedRules.Select(r => r.Category).SequenceEqual(expectedOrder)) throw new InvalidOperationException("Original precedence changed.");
        var node2 = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        RemoteLocationAnalysis Evaluate(string html, string location = "Remote") => JobAnalysis.AnalyzeRemoteLocation(html, location, [], GeographicRestrictionRules.Parse(System.Text.Encoding.UTF8.GetBytes(node2.ToJsonString())));
        var conflict = "Must live within 50 miles of Orlando. Pacific time zone is preferred.";
        node2["rules"]![4]!["priority"] = 0; node2["rules"]![0]!["priority"] = 4;
        if (Evaluate(conflict).Category != "regional-preference") throw new InvalidOperationException("Declarative priority not applied.");
        node2 = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        var array = node2["rules"]!.AsArray(); var reversed = array.Select(n => n!.DeepClone()).Reverse().ToArray(); array.Clear(); foreach (var rule in reversed) array.Add(rule);
        if (Evaluate(conflict).Category != "distance-radius") throw new InvalidOperationException("Array position overrode explicit priority.");
        node2 = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        node2["remoteDesignationCues"]!["primaryLocation"] = "ZXQ";
        if (!Evaluate("Must live in Florida.", "ZXQ").IsRestricted || Evaluate("Must live in Florida.", "Remote").IsRestricted) throw new InvalidOperationException("Declarative designation cue not applied.");
        node2 = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        node2["rules"]![1]!["unlessPatternIds"]!.AsArray().Clear();
        if (!Evaluate("Commuting distance is a plus.").IsRestricted) throw new InvalidOperationException("Declarative exclusion not applied.");
        node2["patterns"]![0]!["pattern"] = "(a+)+$"; node2["regexTimeoutMilliseconds"] = 1;
        try { Evaluate(new string('a', 50000) + "!"); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains("Geographic-restriction rules", StringComparison.Ordinal))
        { Console.WriteLine($"Geographic validation: {mutations.Length + 2} invalid configurations, six exact expressions/options, original cues/order, editable priority/cue/exclusion, bounded pathological regex passed."); return Task.CompletedTask; }
        throw new InvalidOperationException("Geographic regex timeout not enforced.");
    }
}
