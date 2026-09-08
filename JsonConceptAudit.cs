using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace JobSearchManager;

// Deployment-only, read-only validation of the exact production matcher. No ledger or provider calls.
internal static class JsonConceptAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<object> RunAsync(string cacheRoot)
    {
        var catalog = JobConceptCatalog.LoadDefault();
        var snapshot = ConceptRuleSnapshot.Load(Path.Combine(AppContext.BaseDirectory, "rules", "concepts-v1.json"), catalog);
        var reports = new ConceptEvaluationReports(Path.Combine(AppContext.BaseDirectory, "evaluation", "concept-detection"));
        using var view = JsonDocument.Parse(JsonSerializer.Serialize(reports.View(snapshot), Json));
        var curated = view.RootElement.GetProperty("reports").EnumerateArray().Single(r => r.GetProperty("role").GetString() == "curated");
        if (curated.GetProperty("status").GetString() != "CURRENT") throw new InvalidDataException("Curated evidence is stale.");
        var expected = curated.GetProperty("artifact");
        using var corpus = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "RegexValidationCorpus.json")));
        var root = corpus.RootElement;
        var observations = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var postings = 0; var decisions = 0;
        foreach (var fixture in root.GetProperty("fixtures").EnumerateArray())
        {
            var title = fixture.GetProperty("title").GetString()!;
            var html = "<p>" + fixture.GetProperty("excerpt").GetString() + "</p>";
            var result = snapshot.Classify(title, html, new RemoteWorkDetector().Analyze(title, "", [], html), new ExtendedLocationRequirementDetector().Analyze(title, "", [], html));
            if (result.TimedOutRuleIds.Count != 0) throw new InvalidDataException("Curated matching timed out.");
            var present = result.Concepts.Select(c => c.ConceptId).ToHashSet(StringComparer.Ordinal);
            if (fixture.TryGetProperty("conceptId", out var id) && id.ValueKind == JsonValueKind.String)
                Observe(id.GetString()!, fixture.GetProperty("expectedPresent").GetBoolean());
            else
            {
                var positive = fixture.GetProperty("expectedPresentConceptIds").EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
                foreach (var label in root.GetProperty("labelScopes").GetProperty(fixture.GetProperty("labelScope").GetString()!).EnumerateArray())
                    Observe(label.GetString()!, positive.Contains(label.GetString()!));
            }
            postings++;
            void Observe(string concept, bool wanted)
            {
                if (!observations.TryGetValue(concept, out var counts)) observations[concept] = counts = new int[4];
                counts[wanted ? present.Contains(concept) ? 0 : 2 : present.Contains(concept) ? 1 : 3]++;
                decisions++;
            }
        }
        var names = new[] { "truePositive", "falsePositive", "falseNegative", "trueNegative" };
        var expectedConcepts = expected.GetProperty("concepts").EnumerateArray().ToArray();
        if (postings != expected.GetProperty("postingCount").GetInt32() || observations.Count != expectedConcepts.Length)
            throw new InvalidDataException("Curated inventory drift.");
        foreach (var concept in expectedConcepts)
            if (!observations.TryGetValue(concept.GetProperty("conceptId").GetString()!, out var counts) || names.Where((name, i) => concept.GetProperty(name).GetInt32() != counts[i]).Any())
                throw new InvalidDataException("Curated confusion matrix drift.");

        var files = 0; var records = 0; var classified = 0; var pending = 0; var stale = 0;
        var semantic = new SemanticClassificationService(catalog, snapshot.Matcher);
        var timer = Stopwatch.StartNew();
        foreach (var path in Directory.EnumerateFiles(Path.GetFullPath(cacheRoot), "*.json", SearchOption.AllDirectories))
        {
            JobsCacheDocument? document;
            try { document = JsonSerializer.Deserialize<JobsCacheDocument>(await File.ReadAllBytesAsync(path), Json); }
            catch (JsonException) { continue; }
            if (document?.Jobs is not { Count: > 0 }) continue;
            files++;
            foreach (var original in document.Jobs)
            {
                records++;
                var html = original.DescriptionHtml;
                if (string.IsNullOrWhiteSpace(html) && !string.IsNullOrWhiteSpace(original.CompressedDescriptionHtml))
                {
                    using var input = new MemoryStream(Convert.FromBase64String(original.CompressedDescriptionHtml));
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip); html = await reader.ReadToEndAsync();
                }
                if (string.IsNullOrWhiteSpace(html)) { pending++; continue; }
                var job = original with { DescriptionHtml = html };
                var result = snapshot.Classify(job.Title, html, job.RemoteWork, job.ExtendedLocationRequirement);
                if (result.TimedOutRuleIds.Count != 0) throw new InvalidDataException("Cached matching timed out.");
                if (!semantic.IsCurrent(job)) stale++;
                classified++;
            }
        }
        if (classified == 0) throw new InvalidDataException("Cache benchmark classified no postings.");
        return new { authority = snapshot.Identity, curatedExact = true, curatedPostings = postings, curatedDecisions = decisions,
            cacheFiles = files, cacheRecords = records, classifiedJobs = classified, pendingMissingDescription = pending,
            staleRecords = stale, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            jobsPerSecond = classified / timer.Elapsed.TotalSeconds, cacheWritten = false, providersCalled = false };
    }
}
