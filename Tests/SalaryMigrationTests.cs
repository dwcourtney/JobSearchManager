using System.Text.Json;
using JobSearchManager;

internal static class SalaryMigrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal sealed record Fixture(string Id, string Html, SalaryAnalysis? Expected = null);
    private static bool Equal(SalaryAnalysis? a, SalaryAnalysis? b) =>
        JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json);
    internal static void Freeze(string path)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(path), Json)!;
        File.WriteAllText(path, JsonSerializer.Serialize(fixtures.Select(f => f with { Expected = LegacySalaryBaseline.AnalyzeSalary(f.Html) }), Json) + "\n");
        Console.WriteLine($"Frozen {fixtures.Length} salary baseline results.");
    }
    internal static Task RunAsync()
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "salary-fixtures.json")), Json)!;
        foreach (var f in fixtures)
            if (!Equal(LegacySalaryBaseline.AnalyzeSalary(f.Html), f.Expected) || !Equal(JobAnalysis.AnalyzeSalary(f.Html), f.Expected))
                throw new InvalidOperationException($"Salary parity failed: {f.Id}; old={JsonSerializer.Serialize(LegacySalaryBaseline.AnalyzeSalary(f.Html), Json)}; new={JsonSerializer.Serialize(JobAnalysis.AnalyzeSalary(f.Html), Json)}");
        Console.WriteLine($"Exact salary fixture parity: {fixtures.Length}/{fixtures.Length}.");
        return Task.CompletedTask;
    }
    internal static void Compare(string input, string output)
    {
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(input), Json)!;
        var pairs = fixtures.Select(f => new { f.Id, Old = LegacySalaryBaseline.AnalyzeSalary(f.Html), Current = JobAnalysis.AnalyzeSalary(f.Html) }).ToArray();
        File.WriteAllText(output + ".outputs.json", JsonSerializer.Serialize(pairs, Json));
        var report = new
        {
            rulesetVersion = SalaryRules.Default.Version,
            fingerprint = SalaryRules.Default.Fingerprint,
            inputSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))),
            totalPostings = pairs.Length,
            exactMatches = pairs.Count(p => Equal(p.Old, p.Current)),
            changedMinimum = pairs.Count(p => p.Old.Minimum != p.Current.Minimum),
            changedMaximum = pairs.Count(p => p.Old.Maximum != p.Current.Maximum),
            changedPeriod = pairs.Count(p => p.Old.Period != p.Current.Period),
            changedParseStatus = pairs.Count(p => p.Old.ParseStatus != p.Current.ParseStatus),
            differences = pairs.Where(p => !Equal(p.Old, p.Current)).ToArray()
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, Json) + "\n");
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        if (report.exactMatches != report.totalPostings) throw new InvalidOperationException("Salary corpus parity failed.");
    }


    internal static Task ValidationAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rules", "salary-v1.json");
        var bytes = File.ReadAllBytes(path); var rules = SalaryRules.Load(path);
        if (rules.Version != "1.0.0" || rules.Fingerprint != Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)))
            throw new InvalidOperationException("Salary identity mismatch.");
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("salary", StringComparison.OrdinalIgnoreCase)) { return; }
            throw new InvalidOperationException("Invalid salary rules were accepted.");
        }
        Reject(() => SalaryRules.Load(path + ".missing")); Reject(() => SalaryRules.Parse("{"u8.ToArray()));
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
            n => n["patterns"]![0]!["pattern"] = "x",
            n => n["patterns"]![0]!["ignoreCase"] = "yes",
            n => n["regexTimeoutMilliseconds"] = 0,
            n => n["regexTimeoutMilliseconds"] = 1001,
            n => n["unparseablePhrases"] = null,
            n => n["unparseablePhrases"]![0] = null,
            n => n["unparseablePhrases"]![0] = ""
        };
        foreach (var mutate in mutations)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!; mutate(node);
            Reject(() => SalaryRules.Parse(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        foreach (var p in rules.Rules.Patterns)
        {
            var original = (System.Text.RegularExpressions.Regex)typeof(LegacySalaryBaseline).GetMethod(p.Id, flags)!.Invoke(null, null)!;
            var current = rules.Pattern(p.Id);
            if (original.ToString() != p.Pattern || (original.Options & ~System.Text.RegularExpressions.RegexOptions.Compiled) != (current.Options & ~System.Text.RegularExpressions.RegexOptions.Compiled))
                throw new InvalidOperationException("Salary expression/options changed: " + p.Id);
        }
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "salary-fixtures.json")), Json)!;
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR", "de-DE" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                var localized = SalaryRules.Parse(bytes);
                foreach (var f in fixtures)
                    if (!Equal(LegacySalaryBaseline.AnalyzeSalary(f.Html), JobAnalysis.AnalyzeSalary(f.Html, localized)))
                        throw new InvalidOperationException("Salary culture parity failed: " + culture + "/" + f.Id);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        var editable = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        editable["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "HourlyCueRegex")!["pattern"] = @"\bZXQ\b";
        if (JobAnalysis.AnalyzeSalary("Pay Range $20 - $30 ZXQ", SalaryRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString()))).Period != "hourly")
            throw new InvalidOperationException("Salary declarative vocabulary not applied.");
        editable["regexTimeoutMilliseconds"] = 1;
        editable["patterns"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == "SummaryPayHeadingRegex")!["pattern"] = "(a+)+$";
        try { JobAnalysis.AnalyzeSalary(new string('a', 50000) + "!", SalaryRules.Parse(System.Text.Encoding.UTF8.GetBytes(editable.ToJsonString()))); }
        catch (InvalidOperationException ex) when (ex.InnerException is System.Text.RegularExpressions.RegexMatchTimeoutException && ex.Message.Contains("Salary rules", StringComparison.Ordinal))
        { Console.WriteLine($"Salary validation: {mutations.Length + 2} invalid configurations, 10 exact expressions/options, 234 culture comparisons, editable vocabulary and finite timeout passed."); return Task.CompletedTask; }
        throw new InvalidOperationException("Salary timeout not enforced.");
    }
}
