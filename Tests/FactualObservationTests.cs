using System.Text.Json;
using JobSearchManager;

internal static class FactualObservationTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, FactObservations.Json);
    internal static Task RunAsync()
    {
        var detector = new WorkAuthorizationDetector();
        var old = new LegacyWorkAuthorizationBaseline();
        string[] authorizationCases = [
            "US citizenship required.", "Must be a US citizen or permanent resident.",
            "Must be a US citizen or green card holder.",
            "<p>US citizenship required.</p><p>Must be a US citizen or permanent resident.</p>",
            "Must be authorized to work in the US; sponsorship not available.",
            "Sponsorship may be available for qualified candidates.",
            "<p>No visa sponsorship.</p><p>Visa sponsorship is available for this position.</p>",
            "Candidates must be a US person due to export controls.",
            "We are an equal opportunity employer without regard to citizenship.",
            "<h3>Preferred qualifications</h3><p>US citizenship is preferred.</p>",
            "<p>US citizenship is required.</p><p>US citizenship is required.</p>",
            "US citizenship and work authorization are required.", ""
        ];
        foreach (var html in authorizationCases)
        {
            var extracted = detector.Extract(html);
            Check(Serialize(old.Analyze(html)) == Serialize(WorkAuthorizationDetector.Summarize(extracted)), "Authorization legacy parity: " + html);
            var observations = detector.Observe(html);
            Check(Serialize(observations) == Serialize(detector.Observe(html)), "Non-deterministic authorization observations.");
            Check(observations.OriginalHtml == html, "Original source lost.");
            foreach (var o in observations.Observations)
                Check(o.Evidence.Start >= 0 && o.Evidence.Start + o.Evidence.Length <= o.Evidence.Text.Length, "Bad evidence coordinate.");
        }
        Check(detector.Observe(authorizationCases[11]).Observations.Any(o => o.LogicalConnective == "and"), "AND conjunction lost.");
        Check(detector.Observe(authorizationCases[9]).Observations.Any(o => o.Obligation == "preferred"), "Preferred requirement lost.");
        var negated = detector.Observe("US citizenship is not required.");
        Check(negated.Observations.All(o => o.Obligation != "required"), "Negation became a requirement.");
        var alternatives = detector.Observe(authorizationCases[3]);
        Check(alternatives.Observations.Any(o => o.Value.Code == "usCitizen") &&
            alternatives.Observations.Any(o => o.Value.Alternatives?.Contains("permanentResident") == true), "Alternative discarded.");
        Check(detector.Observe("Must have a green card.").Observations.Any(o => o.Value.Code == "permanentResident"), "Standalone permanent residency lost.");
        Check(detector.Observe(authorizationCases[4]).Observations.Any(o => o.Value.Code == "notAvailable"), "No-sponsorship wording lost.");
        Check(detector.Observe(authorizationCases[7]).Observations.Any(o => o.Value.Code is "usPerson" or "exportControlled"), "US-person constraint lost.");
        var sponsors = detector.Observe(authorizationCases[6]);
        Check(sponsors.Observations.Any(o => o.Value.Code == "available") && sponsors.Observations.Any(o => o.Value.Code == "notAvailable"), "Sponsorship contradiction lost.");
        Check(sponsors.Observations.Any(o => o.Relations.Count > 0), "Contradiction relationship missing.");
        Check(detector.Observe(authorizationCases[5]).Observations.Any(o => o.Value.Code == "available" && o.Qualifier == "conditional"), "Conditional availability lost.");
        Check(detector.Observe(authorizationCases[8]).Observations.Count == 0, "Employer boilerplate asserted as requirement.");
        var repeated = detector.Observe(authorizationCases[10]).Observations;
        Check(repeated.Count >= 2 && repeated.Select(o => o.Id).Distinct().Count() == repeated.Count, "Distinct evidence locations collapsed.");
        Check(detector.Observe(authorizationCases[0], "provider-a").Observations[0].Id !=
            detector.Observe(authorizationCases[0], "provider-b").Observations[0].Id, "Source identity missing from observation identity.");

        string[] compensationCases = [
            "<p>Colorado:</p><p>$120,000-$145,000</p><p>California:</p><p>$135,000-$160,000</p><p>Elsewhere:</p><p>$110,000-$150,000</p>",
            "<p>Base salary: USD 120000-150000 annually.</p><p>Bonus: USD 10000 annually.</p>",
            "<p>Pay: $30-$40 hourly.</p><p>Annual salary: $60000-$80000.</p>",
            "<p>Level 3:</p><p>Salary USD 100000-120000 annually.</p><p>Level 4:</p><p>Salary USD 140000-160000 annually.</p>",
            "<p>National: Salary $80000-$150000 annually.</p><p>California: Salary $100000-$150000 annually.</p>",
            "Salary: USD 100000 - TBD", "Salary: USD 100000 - CAD 120000 annually.",
            "<p>Summary Pay Range:</p><p>California: $100000-$150000 annually</p><p>Texas: $80000-$110000 annually</p>",
            "<p>Part-time Level 3:</p><p>Salary $30-$40 hourly</p>", "", "Nothing stated.",
            "Anticipated salary range for this role is $80000 - $100000. Pay Range $79228162514264337593543951k - $79228162514264337593543951k."
        ];
        foreach (var html in compensationCases)
        {
            var extracted = JobAnalysis.ExtractSalary(html, SalaryRules.Default);
            Check(Serialize(LegacySalaryBaseline.AnalyzeSalary(html)) == Serialize(JobAnalysis.SummarizeSalary(extracted)), "Salary legacy parity: " + html);
            var observations = CompensationObservations.Observe(extracted);
            Check(Serialize(observations) == Serialize(CompensationObservations.Observe(extracted)), "Non-deterministic compensation observations.");
            foreach (var o in observations.Observations)
                Check(o.Evidence.Start >= 0 && o.Evidence.Start + o.Evidence.Length <= o.Evidence.Text.Length, "Bad compensation evidence coordinate.");
        }
        FactObservationDocument Pay(int index) => CompensationObservations.Observe(JobAnalysis.ExtractSalary(compensationCases[index], SalaryRules.Default));
        var regions = Pay(0).Observations;
        Check(regions.Count == 3 && regions.Select(o => o.Scope.Geography).SequenceEqual(["Colorado", "California", "Elsewhere"]), "Regional ranges collapsed.");
        Check(regions.Select(o => o.Value.Lower).SequenceEqual(new decimal?[] { 120000, 135000, 110000 }), "Regional lower bounds changed.");
        Check(regions.All(o => o.Value.Currency is null && o.Value.Unit is null), "Dollar symbol inferred as USD or annual.");
        Check(Pay(1).Observations.Select(o => o.Value.Basis).SequenceEqual(["base", "bonus"]), "Base and bonus collapsed.");
        Check(Pay(2).Observations.Select(o => o.Value.Unit).SequenceEqual(["hourly", "annual"]), "Hourly/annual semantics lost.");
        Check(Pay(3).Observations.Select(o => o.Scope.JobLevel).SequenceEqual(["Level 3", "Level 4"]), "Job levels lost.");
        Check(Pay(5).Observations.Single().Value.Kind == "partial-range", "Malformed range lost.");
        Check(Pay(6).Observations.Count == 2 && Pay(6).Observations.All(o => o.Qualifier == "currency-mismatch-review"), "Currency mismatch flattened.");
        Check(Pay(8).Observations.Single().Scope.EmploymentType == "Part-time", "Employment applicability lost.");
        var replayHtml = compensationCases[7];
        var trace = JobAnalysis.ExtractSalary(replayHtml, SalaryRules.Default);
        Check(Serialize(JobAnalysis.SummarizeSalary(trace)) == Serialize(JobAnalysis.SummarizeSalary(trace with { OriginalHtml = "not parsed again" })), "Salary projection reparses source.");
        var authorizationTrace = detector.Extract(authorizationCases[3]);
        Check(Serialize(WorkAuthorizationDetector.Summarize(authorizationTrace)) == Serialize(WorkAuthorizationDetector.Summarize(authorizationTrace with { OriginalHtml = "not parsed again" })), "Authorization projection reparses source.");
        var tempInput = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var tempOutput = tempInput + ".out";
        try
        {
            File.WriteAllText(tempInput, Serialize(new[] { new Input("metadata-conflict", "No visa sponsorship.", "example", new() { ["sponsorship"] = "available" }) }));
            Inspect(tempInput, tempOutput);
            using var document = JsonDocument.Parse(File.ReadAllText(tempOutput));
            var record = document.RootElement[0];
            Check(record.GetProperty("metadata")[0].GetProperty("source").GetString() == "provider-metadata", "Provider source lost.");
            Check(record.GetProperty("authorization").GetProperty("observations").EnumerateArray().Any(o => o.GetProperty("value").GetProperty("code").GetString() == "notAvailable"), "Provider metadata overwrote body.");
        }
        finally { File.Delete(tempInput); File.Delete(tempOutput); }
        Console.WriteLine($"Lossless observation adversarial cases: {authorizationCases.Length} authorization + {compensationCases.Length} compensation; legacy parity exact.");
        return Task.CompletedTask;
    }

    internal sealed record Input(string Id, string Html, string? Provider = null, Dictionary<string, string>? Metadata = null, string? Title = null, string? PrimaryLocation = null, string[]? AdditionalLocations = null);
    internal static void Inspect(string input, string output)
    {
        var rows = JsonSerializer.Deserialize<Input[]>(File.ReadAllText(input), FactObservations.Json)!;
        var detector = new WorkAuthorizationDetector();
        var results = rows.Select(row => {
            var auth = detector.Extract(row.Html);
            var pay = JobAnalysis.ExtractSalary(row.Html, SalaryRules.Default);
            var metadata = (row.Metadata ?? []).OrderBy(p => p.Key, StringComparer.Ordinal).Select((pair, index) =>
                FactObservations.Create("provider-metadata", pair.Key, new FactValue("declared", pair.Value, pair.Value),
                    "unknown", "provider-declared-not-reconciled-with-body", new FactScope(null, null, null, null, null, pair.Key),
                    new FactEvidence(pair.Value, "provider-field", index, 0, pair.Value.Length), "input-v1", FactObservations.Hash(Serialize(row.Metadata)),
                    pair.Key, "provider-metadata", row.Provider)).ToArray();
            return new { row.Id, inputHash = FactObservations.Hash(row.Html),
                authorization = detector.Observe(row.Html, row.Provider),
                authorizationSummary = WorkAuthorizationDetector.Summarize(auth),
                compensation = CompensationObservations.Observe(pay, row.Provider),
                compensationLegacyCandidates = pay.Observations,
                compensationSummary = JobAnalysis.SummarizeSalary(pay), metadata, domains = FactualCompletionInspection.Run(row.Html, row.Provider, row.Metadata, row.Title, row.PrimaryLocation, row.AdditionalLocations) };
        }).ToArray();
        File.WriteAllText(output, Serialize(results) + "\n");
        Console.WriteLine($"Wrote observation diagnostics for {results.Length} postings; no cache or provider operations.");
    }
}
