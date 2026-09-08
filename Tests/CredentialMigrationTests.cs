using System.Text.Json;
using JobSearchManager;

internal static class CredentialMigrationTests
{
    private static readonly LegacyCredentialBaseline Old = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<LegacyCredentialBaseline>.Instance);
    private static readonly CredentialDetector Current = new(Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialDetector>.Instance);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, CredentialAnalysis? Expected = null);
    private static bool Equal(CredentialAnalysis? a, CredentialAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = Old.Analyze(f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} credential baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "credential-fixtures.json")), Json)!;
        foreach (var f in fixtures)
            if (!Equal(Old.Analyze(f.Html), f.Expected) || !Equal(Current.Analyze(f.Html), f.Expected))
                throw new InvalidOperationException($"Credential parity failed: {f.Id}; old={JsonSerializer.Serialize(Old.Analyze(f.Html), Json)}; new={JsonSerializer.Serialize(Current.Analyze(f.Html), Json)}");
        Console.WriteLine($"Exact credential fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var pairs = fixtures.Select(f => new { f.Id, Old = Old.Analyze(f.Html), Current = Current.Analyze(f.Html) }).ToArray();
        File.WriteAllText(output + ".outputs.json", JsonSerializer.Serialize(pairs, Json));
        var report = new
        {
            rulesetVersion = CredentialRules.Default.Version,
            fingerprint = CredentialRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedCredentials = pairs.Count(p => JsonSerializer.Serialize(p.Old.Credentials, Json) != JsonSerializer.Serialize(p.Current.Credentials, Json)),
            changedUnrecognized = pairs.Count(p => JsonSerializer.Serialize(p.Old.UnrecognizedMentions, Json) != JsonSerializer.Serialize(p.Current.UnrecognizedMentions, Json)),
            changedUnknownRequirements = pairs.Count(p => JsonSerializer.Serialize(p.Old.UnknownRequirements, Json) != JsonSerializer.Serialize(p.Current.UnknownRequirements, Json)),
            changedCatalogVersion = pairs.Count(p => p.Old.CatalogVersion != p.Current.CatalogVersion),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Credential corpus parity failed.");
    }


    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "credential-v1.json");
        var bytes = File.ReadAllBytes(path); var rules = CredentialRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Credential rules identity mismatch.");
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CredentialCatalog.json")).Replace("\r\n", "\n", StringComparison.Ordinal)))) != "61b13e1e0043c8d73073ee7b9d6d16e5f141d44f70f7542d7e1fca522d1e9a65")
            throw new InvalidOperationException("Credential migration changed catalog bytes.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("credential", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid credential rules were accepted.");
        }
        Reject(() => CredentialRules.Load(path + ".missing")); Reject(() => CredentialRules.Parse("{"u8.ToArray()));
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
            n => n["regexTimeoutMilliseconds"] = 251
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => CredentialRules.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        foreach (var p in rules.Rules.Patterns)
        {
            var original = (System.Text.RegularExpressions.Regex)typeof(LegacyCredentialBaseline).GetField(p.Id, flags)!.GetValue(null)!;
            var current = rules.Pattern(p.Id);
            if (original.ToString() != p.Pattern || original.Options != current.Options || original.MatchTimeout != current.MatchTimeout)
                throw new InvalidOperationException("Credential expression/options/timeout changed: " + p.Id);
        }
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CredentialDetector>.Instance;
        var editable = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        editable["patterns"]![0]!["pattern"] = @"\bZXQ\b";
        var edited = new CredentialDetector(logger, CredentialRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString())));
        if (Current.Analyze("ZXQ").UnrecognizedMentions.Count != 0 || edited.Analyze("ZXQ").UnrecognizedMentions.Count != 1)
            throw new InvalidOperationException("Credential declarative recognition vocabulary not applied.");
        editable["patterns"]![1]!["pattern"] = @"\bZXQ\b";
        edited = new CredentialDetector(logger, CredentialRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString())));
        if (edited.Analyze("ZXQ").UnrecognizedMentions.Count != 0)
            throw new InvalidOperationException("Credential declarative exclusion not applied.");
        editable["patterns"]![0]!["pattern"] = "(a+)+$";
        editable["regexTimeoutMilliseconds"] = 1;
        // Test the changed matcher directly so unrelated catalog patterns do not consume this timeout fixture first.
        try { CredentialRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString())).Pattern("GeneralCredentialLanguageRegex").IsMatch(new string('a', 50000) + "!"); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        { Console.WriteLine($"Credential validation: {mutations.Length + 2} invalid configurations, 2 exact expressions/options/timeouts, unchanged catalog, editable vocabulary/exclusion and finite timeout passed."); return Task.CompletedTask; }
        throw new InvalidOperationException("Credential timeout not enforced.");
    }
}
